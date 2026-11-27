using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Кабинет гостя (ТЗ §3.4): гость видит и меняет только свои брони. Чужая бронь — 404, как несуществующая
/// (не раскрываем, что такой id есть). Правила те же, что у администратора: расчёт цены, отмена, продление — общий код.
/// </summary>
internal sealed class GuestBookingService(
    DbContext db,
    IHotelContext hotel,
    ICurrentGuest currentGuest,
    BookingService bookings,
    Stays stays,
    IOptions<GuestBookingOptions> options) : IGuestBookingService
{
    private static readonly BookingStatus[] Current =
        [BookingStatus.Pending, BookingStatus.AwaitingPayment, BookingStatus.Confirmed, BookingStatus.CheckedIn];

    /// <summary>Брони, которые держат номер, но гость ещё не заселён, — их считает лимит.</summary>
    private static readonly BookingStatus[] NotStarted =
        [BookingStatus.Pending, BookingStatus.AwaitingPayment, BookingStatus.Confirmed];

    private static readonly Error NoGuestCard = Error.Forbidden("guest.profile_required", "Бронировать на сайте может гость с подтверждённым профилем.");

    public async Task<Result<MyBookingDto>> CreateAsync(GuestCreateBookingRequest r, CancellationToken ct)
    {
        if (await currentGuest.GetAsync(ct) is not { } guest)
        {
            return NoGuestCard;
        }

        // Защита номерного фонда: каждая бронь с сайта держит номер (неподтверждённая — до решения администратора),
        // поэтому один аккаунт не может набрать больше N незаселённых броней. Больше — через гостиницу.
        // Лимит проверяется до вставки; гонку параллельных запросов сдерживает ограничение частоты на эндпоинте.
        var limit = options.Value.MaxActiveWebBookingsPerGuest;
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var active = await db.Set<Booking>().CountAsync(
            b => b.HotelId == hotelId && b.GuestId == guest.Id && b.Source == BookingSource.Web && NotStarted.Contains(b.Status), ct);
        if (active >= limit)
        {
            return BookingErrors.TooManyWebBookings(limit);
        }

        // Номер выбирает гость (решение 2026-11-23). Занят — 409 booking.room_unavailable: другой номер без спроса не подставляем.
        // Проверки номера и времени — в общем коде создания брони.
        var settings = await stays.SettingsAsync(ct);
        var created = await bookings.CreateWebAsync(
            new CreateBookingRequest(r.RoomId, r.CheckIn, r.CheckOut, r.Adults, r.Children, guest.FullName, guest.Phone, guest.Email,
                string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(), r.PromoCode, r.Services,
                settings.AutoConfirmWebBookings, r.Kind, r.Date, r.StartHour, r.Hours, guest.Id),
            ct);
        return created.Map(ToGuest);
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

        return new MyBookingDto(
            b.Id, b.Number, b.Status, b.Kind, b.StartsAt, b.EndsAt, b.Hours, b.Nights,
            b.Room.RoomTypeId, b.Room.RoomTypeName, b.Room.Number, b.Adults, b.Children, b.Price,
            b.Extensions.Select(x => new MyBookingExtensionDto(x.Kind, x.Quantity, x.NewEndsAt, x.Amount)).ToList(),
            b.TotalAmount, b.PenaltyAmount, b.PenaltyKind, b.EarlyDepartureCredit, b.HoldUntil, b.CancelReason, b.CreatedAt,
            actions, b.Version);
    }
}

/// <summary>Ограничения броней гостя на сайте (appsettings, секция <c>GuestBookings</c>).</summary>
public sealed class GuestBookingOptions
{
    public const string Section = "GuestBookings";

    /// <summary>Сколько незаселённых броней с сайта (ждёт подтверждения / оплаты / подтверждена) может держать один аккаунт.</summary>
    public int MaxActiveWebBookingsPerGuest { get; set; } = 3;
}
