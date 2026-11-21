using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Кабинет гостя (ТЗ §3.4): гость видит и меняет только свои брони. Чужая бронь — 404, как несуществующая
/// (не раскрываем, что такой id есть). Правила те же, что у администратора: расчёт цены, отмена, продление — общий код.
/// </summary>
internal sealed class GuestBookingService(
    DbContext db,
    IHotelContext hotel,
    IRoomDirectory rooms,
    ICurrentGuest currentGuest,
    BookingService bookings,
    Stays stays) : IGuestBookingService
{
    /// <summary>Сколько раз пробовать другой свободный номер категории, если выбранный успели занять.</summary>
    private const int RoomAttempts = 3;

    private static readonly BookingStatus[] Current =
        [BookingStatus.Pending, BookingStatus.AwaitingPayment, BookingStatus.Confirmed, BookingStatus.CheckedIn];

    private static readonly Error NoGuestCard = Error.Forbidden("guest.profile_required", "Бронировать на сайте может гость с подтверждённым профилем.");

    public async Task<Result<MyBookingDto>> CreateAsync(GuestCreateBookingRequest r, CancellationToken ct)
    {
        if (await currentGuest.GetAsync(ct) is not { } guest)
        {
            return NoGuestCard;
        }

        var stay = await stays.ResolveAsync(r, ct);
        if (stay.IsFailure)
        {
            return stay.Error;
        }

        // Номер подбирает система: свободные номера категории, сначала дешёвые. Номер комнаты гостю не показывается до заселения.
        var settings = await stays.SettingsAsync(ct);
        var hourly = stay.Value.Kind == BookingKind.Hourly;
        var candidates = (await rooms.ListSellableAsync(r.RoomTypeId, ct))
            .Where(x => x.Capacity >= r.Adults + r.Children && (!hourly || x.SellsHourly))
            .OrderBy(x => hourly ? x.HourlyPriceOverride ?? x.HourlyBasePrice : x.PriceOverride ?? x.BasePrice)
            .ThenBy(x => x.Number, StringComparer.Ordinal)
            .ToList();
        var attempts = 0;
        foreach (var room in candidates)
        {
            if ((await stays.BusyRoomsAsync([room.Id], Stays.ForRoom(stay.Value, settings, room), null, ct)).Count > 0)
            {
                continue;
            }

            var created = await bookings.CreateWebAsync(
                new CreateBookingRequest(room.Id, r.CheckIn, r.CheckOut, r.Adults, r.Children, guest.FullName, guest.Phone, guest.Email,
                    string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(), r.PromoCode, r.Services,
                    settings.AutoConfirmWebBookings, r.Kind, r.Date, r.StartHour, r.Hours, guest.Id),
                ct);
            if (created.IsSuccess)
            {
                return ToGuest(created.Value);
            }

            // Номер заняли параллельно — пробуем следующий; любая другая ошибка (промокод, правила) — сразу гостю.
            if (created.Error.Code != BookingErrors.RoomUnavailable.Code || ++attempts >= RoomAttempts)
            {
                return created.Error.Code == BookingErrors.RoomUnavailable.Code ? BookingErrors.RoomTypeUnavailable : created.Error;
            }
        }

        return BookingErrors.RoomTypeUnavailable;
    }

    public async Task<IReadOnlyList<MyBookingDto>> ListAsync(GuestBookingQuery q, CancellationToken ct)
    {
        if (await currentGuest.GetAsync(ct) is not { } guest)
        {
            return [];
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Booking>().AsNoTracking().Where(b => b.HotelId == hotelId && b.GuestId == guest.Id);
        query = q.Scope == "history"
            ? query.Where(b => !Current.Contains(b.Status)).OrderByDescending(b => b.StartsAt)
            : query.Where(b => Current.Contains(b.Status)).OrderBy(b => b.StartsAt);
        var ids = await query.Take(100).Select(b => b.Id).ToListAsync(ct);
        var result = new List<MyBookingDto>(ids.Count);
        foreach (var id in ids)
        {
            if ((await bookings.GetAsync(id, ct)) is { IsSuccess: true } dto)
            {
                result.Add(ToGuest(dto.Value));
            }
        }

        return result;
    }

    public async Task<Result<MyBookingDto>> GetAsync(Guid id, CancellationToken ct) =>
        await OwnedAsync(id, ct) is { } error ? error : (await bookings.GetAsync(id, ct)).Map(ToGuest);

    public async Task<Result<CancellationQuoteDto>> QuoteCancellationAsync(Guid id, CancellationToken ct)
    {
        if (await OwnedAsync(id, ct) is { } error)
        {
            return error;
        }

        // Гость не может снять штраф — признак «можно без штрафа» ему не показываем.
        return (await bookings.QuoteCancellationAsync(id, ct)).Map(q => q with { CanWaive = false });
    }

    public async Task<Result<MyBookingDto>> CancelAsync(Guid id, GuestCancelRequest r, CancellationToken ct)
    {
        if (await OwnedAsync(id, ct) is { } error)
        {
            return error;
        }

        var reason = string.IsNullOrWhiteSpace(r.Reason) ? "Отменено гостем на сайте" : $"Гость: {r.Reason.Trim()}";
        return (await bookings.CancelAsync(id, new CancelBookingRequest(reason, WaivePenalty: false), ct)).Map(ToGuest);
    }

    public async Task<Result<ExtensionQuoteDto>> QuoteExtensionAsync(Guid id, int units, CancellationToken ct) =>
        await OwnedAsync(id, ct) is { } error ? error : await bookings.QuoteExtensionAsync(id, units, ct);

    public async Task<Result<MyBookingDto>> ExtendAsync(Guid id, ExtendBookingRequest r, CancellationToken ct) =>
        await OwnedAsync(id, ct) is { } error ? error : (await bookings.ExtendAsync(id, r, ct)).Map(ToGuest);

    /// <summary>Бронь принадлежит текущему гостю? Иначе — «не найдена» (защита от перебора чужих id).</summary>
    private async Task<Error?> OwnedAsync(Guid id, CancellationToken ct)
    {
        if (await currentGuest.GetAsync(ct) is not { } guest)
        {
            return BookingErrors.NotFound;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Booking>().AnyAsync(b => b.Id == id && b.HotelId == hotelId && b.GuestId == guest.Id, ct) ? null : BookingErrors.NotFound;
    }

    private static MyBookingDto ToGuest(BookingDto b)
    {
        var actions = new List<string>();
        if (b.AllowedActions.Contains("cancel"))
        {
            actions.Add("cancel");
        }

        if (b.AllowedActions.Contains("extend"))
        {
            actions.Add("extend");
        }

        var inRoom = b.Status is nameof(BookingStatus.CheckedIn) or nameof(BookingStatus.CheckedOut);
        return new MyBookingDto(
            b.Id, b.Number, b.Status, b.Kind, b.StartsAt, b.EndsAt, b.Hours, b.Nights,
            b.Room.RoomTypeId, b.Room.RoomTypeName, inRoom ? b.Room.Number : null, b.Adults, b.Children, b.Price,
            b.Extensions.Select(x => new MyBookingExtensionDto(x.Kind, x.Quantity, x.NewEndsAt, x.Amount)).ToList(),
            b.TotalAmount, b.PenaltyAmount, b.PenaltyKind, b.EarlyDepartureCredit, b.HoldUntil, b.CancelReason, b.CreatedAt,
            actions, b.Version);
    }
}
