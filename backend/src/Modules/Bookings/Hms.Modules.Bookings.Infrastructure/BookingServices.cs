using System.Data.Common;
using System.Text.Json;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Pricing.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using GuestContact = Hms.Modules.Bookings.Domain.GuestContact;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>Подбор свободных номеров и расчёт стоимости (BOOK-2, BOOK-4). Только на backend.</summary>
internal sealed class AvailabilityService(IRoomDirectory rooms, Stays stays) : IAvailabilityService
{
    public async Task<Result<IReadOnlyList<AvailableRoomDto>>> FindRoomsAsync(AvailabilityQuery q, CancellationToken ct)
    {
        var stay = await stays.ResolveAsync(q, ct);
        if (stay.IsFailure)
        {
            return stay.Error;
        }

        var free = await FreeRoomsAsync(stay.Value, (q.Adults ?? 1) + (q.Children ?? 0), q.RoomTypeId, ct);
        var result = new List<AvailableRoomDto>(free.Count);
        foreach (var room in free)
        {
            var quote = await stays.PriceAsync(room, stay.Value, null, [], ct);
            if (quote.IsSuccess)
            {
                result.Add(new AvailableRoomDto(room.Id, room.Number, room.Floor, room.RoomTypeId, room.RoomTypeName, room.Capacity, quote.Value));
            }
        }

        return result.OrderBy(r => r.Quote.Total).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
    }

    public async Task<Result<IReadOnlyList<PublicAvailabilityDto>>> FindPublicAsync(AvailabilityQuery q, CancellationToken ct)
    {
        var found = await FindRoomsAsync(q, ct);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var hourly = q.IsHourly();
        var nights = hourly ? 0 : q.CheckOut!.Value.DayNumber - q.CheckIn!.Value.DayNumber;
        var units = hourly ? q.Hours!.Value : nights;

        // Сайту — только категории, количество и цена «от»: номера комнат и занятость конкретных номеров не раскрываются.
        return found.Value
            .GroupBy(r => (r.RoomTypeId, r.RoomTypeName))
            .Select(g => new PublicAvailabilityDto(g.Key.RoomTypeId, g.Key.RoomTypeName, g.Max(r => r.Capacity), g.Count(), nights, g.Min(r => r.Quote.Total),
                hourly ? StayKinds.Hourly : StayKinds.Nightly, units))
            .OrderBy(t => t.TotalFrom)
            .ToList();
    }

    public async Task<Result<PriceQuote>> QuoteAsync(QuoteRequest r, CancellationToken ct)
    {
        var room = await rooms.GetAsync(r.RoomId, ct);
        if (room is null)
        {
            return BookingErrors.RoomNotFound;
        }

        if (r.Adults + r.Children > room.Capacity)
        {
            return BookingErrors.CapacityExceeded(room.Capacity);
        }

        if (r.IsHourly() && !room.SellsHourly)
        {
            return BookingErrors.RoomNotHourly;
        }

        var stay = await stays.ResolveAsync(r, ct);
        return stay.IsFailure ? stay.Error : await stays.PriceAsync(room, stay.Value, r.PromoCode, r.Services ?? [], ct);
    }

    public async Task<Result<PriceQuote>> QuotePublicAsync(PublicQuoteRequest r, CancellationToken ct)
    {
        var stay = await stays.ResolveAsync(r, ct);
        if (stay.IsFailure)
        {
            return stay.Error;
        }

        var free = await FreeRoomsAsync(stay.Value, r.Adults + r.Children, r.RoomTypeId, ct);
        var cheapest = free
            .OrderBy(x => r.IsHourly() ? x.HourlyPriceOverride ?? x.HourlyBasePrice : x.PriceOverride ?? x.BasePrice)
            .FirstOrDefault();
        return cheapest is null
            ? BookingErrors.RoomTypeUnavailable
            : await stays.PriceAsync(cheapest, stay.Value, r.PromoCode, r.Services ?? [], ct);
    }

    /// <summary>Номера в продаже (для часов — с ценой часа), вмещающие гостей и не занятые на это время с учётом уборки.</summary>
    private async Task<List<RoomInfo>> FreeRoomsAsync(StayPeriod stay, int guests, Guid? roomTypeId, CancellationToken ct)
    {
        var candidates = (await rooms.ListSellableAsync(roomTypeId, ct))
            .Where(r => r.Capacity >= guests && (stay.Kind == BookingKind.Nightly || r.SellsHourly))
            .ToList();
        var settings = await stays.SettingsAsync(ct);
        var busy = new HashSet<Guid>();
        foreach (var group in candidates.GroupBy(r => Stays.ForRoom(stay, settings, r).BlockedUntil))
        {
            busy.UnionWith(await stays.BusyRoomsAsync(group.Select(r => r.Id).ToList(), stay with { BlockedUntil = group.Key }, null, ct));
        }

        return candidates.Where(r => !busy.Contains(r.Id)).ToList();
    }
}

internal sealed class BookingService(
    DbContext db,
    IHotelContext hotel,
    IRoomDirectory rooms,
    Stays stays,
    IUserDirectory users,
    IGuestDirectory guests,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IHotelClock clock,
    IUserAccessProvider access) : IBookingService
{
    public async Task<PagedResult<BookingListItemDto>> ListAsync(BookingQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Set<Booking>().AsNoTracking().Where(b => b.HotelId == hotelId);

        if (!string.IsNullOrWhiteSpace(q.Status) && Enum.TryParse<BookingStatus>(q.Status, out var status))
        {
            query = query.Where(b => b.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(q.Kind) && Enum.TryParse<BookingKind>(q.Kind, ignoreCase: true, out var kind))
        {
            query = query.Where(b => b.Kind == kind);
        }

        // Пересечение с периодом [From, To]: бронь, проживание которой затрагивает хотя бы один день периода.
        if (q.From is { } from)
        {
            var fromUtc = clock.ToUtc(from, TimeOnly.MinValue);
            query = query.Where(b => b.EndsAt > fromUtc);
        }

        if (q.To is { } to)
        {
            var toUtc = clock.ToUtc(to.AddDays(1), TimeOnly.MinValue);
            query = query.Where(b => b.StartsAt < toUtc);
        }

        if (q.RoomId is { } roomId)
        {
            query = query.Where(b => b.Rooms.Any(r => r.RoomId == roomId));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = $"%{q.Search.Trim().ToLowerInvariant()}%";
            var numberLike = s.Replace('c', 'с');
            // Телефон — по цифрам: «0555 12», «+996 555 12» и «55512» находят одну бронь.
            var phoneLike = PhoneNumber.SearchDigits(q.Search) is { } digits ? $"%{digits}%" : null;
#pragma warning disable CA1304, CA1311 // ToLower транслируется в SQL lower()
            query = query.Where(b => EF.Functions.Like(b.Number.ToLower(), numberLike) || EF.Functions.Like(b.GuestName.ToLower(), s)
                || (b.GuestPhone != null && (EF.Functions.Like(b.GuestPhone, s) || (phoneLike != null && EF.Functions.Like(b.GuestPhone, phoneLike))))
                || (b.GuestEmail != null && EF.Functions.Like(b.GuestEmail.ToLower(), s)));
#pragma warning restore CA1304, CA1311
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Number)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(b => new { Booking = b, RoomId = b.Rooms.Select(r => r.RoomId).First() })
            .ToListAsync(ct);
        var roomInfo = await rooms.GetManyAsync(items.Select(i => i.RoomId), ct);

        return new PagedResult<BookingListItemDto>(
            items.Select(i =>
            {
                var b = i.Booking;
                var room = roomInfo.GetValueOrDefault(i.RoomId);
                return new BookingListItemDto(b.Id, b.Number, b.Status.ToString(), b.CheckIn, b.CheckOut, b.Nights,
                    b.GuestName, b.GuestPhone, room?.Number ?? "—", room?.RoomTypeName ?? "—", b.TotalAmount, b.CreatedAt,
                    b.Kind.ToString(), stays.Format(b.StartsAt), stays.Format(b.EndsAt), b.Hours, b.GuestId);
            }).ToList(),
            page.Page,
            page.PageSize,
            total);
    }

    public async Task<Result<BookingDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: false, ct);
        return booking is null ? BookingErrors.NotFound : await ToDtoAsync(booking, ct);
    }

    public Task<Result<BookingDto>> CreateAsync(CreateBookingRequest r, CancellationToken ct) => CreateAsync(r, BookingSource.Admin, retryOnDeadlock: true, ct);

    /// <summary>Бронь с сайта (кабинет гостя): источник Web, подтверждение — по настройке гостиницы.</summary>
    internal Task<Result<BookingDto>> CreateWebAsync(CreateBookingRequest r, CancellationToken ct) => CreateAsync(r, BookingSource.Web, retryOnDeadlock: true, ct);

    private async Task<Result<BookingDto>> CreateAsync(CreateBookingRequest r, BookingSource source, bool retryOnDeadlock, CancellationToken ct)
    {
        var room = await rooms.GetAsync(r.RoomId, ct);
        if (room is null)
        {
            return BookingErrors.RoomNotFound;
        }

        if (!room.IsSellable)
        {
            return BookingErrors.RoomNotSellable;
        }

        if (r.IsHourly() && !room.SellsHourly)
        {
            return BookingErrors.RoomNotHourly;
        }

        if (r.Adults + r.Children > room.Capacity)
        {
            return BookingErrors.CapacityExceeded(room.Capacity);
        }

        var resolved = await stays.ResolveAsync(r, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var stay = Stays.ForRoom(resolved.Value, await stays.SettingsAsync(ct), room);

        // Быстрая проверка для понятной ошибки; гарантию против гонок даёт ограничение EXCLUDE в БД.
        if ((await stays.BusyRoomsAsync([room.Id], stay, null, ct)).Count > 0)
        {
            return BookingErrors.RoomUnavailable;
        }

        var quote = await stays.PriceAsync(room, stay, r.PromoCode, r.Services ?? [], ct);
        if (quote.IsFailure)
        {
            return quote.Error;
        }

        var q = quote.Value;
        var price = ToBookingPrice(q, r.PromoCode);

        var guest = await ResolveGuestAsync(r, ct);
        if (guest.IsFailure)
        {
            return guest.Error;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = clock.UtcNow;
        var (number, numberYear) = await NextNumberAsync(hotelId, stay.Kind, ct);
        var settings = await stays.SettingsAsync(ct);

        // В брони — снимок контакта на момент брони; карточка гостя (GuestId) живёт в модуле Guests.
        var contact = guest.Value.Contact;
        var booking = Booking.Create(hotelId, number, source, stay, r.Adults, r.Children,
            contact, guest.Value.GuestId, r.Comment, room.Id, room.RoomTypeId, price, r.Confirm, currentUser.UserId, now,
            BookingRules.HoldUntil(settings, stay.Kind, stay.StartsAt, now), numberYear);
        db.Set<Booking>().Add(booking);
        audit.Record(new AuditEntry("booking.created", "Booking", booking.Id.ToString(),
            After: new { booking.Number, Kind = stay.Kind.ToString(), Status = booking.Status.ToString(), room = room.Number, stay.StartsAt, stay.EndsAt, booking.TotalAmount }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (SqlState(ex) == ExclusionViolation)
        {
            // Параллельный запрос успел занять номер на пересекающееся время.
            db.ChangeTracker.Clear();
            return BookingErrors.RoomUnavailable;
        }
        catch (Exception ex) when (SqlState(ex) == DeadlockDetected)
        {
            // Одновременные вставки пересекающихся броней: PostgreSQL прерывает одну из ждущих друг друга транзакций.
            // Номер, скорее всего, занят победившей бронью; если нет (она тоже откатилась) — повторяем один раз.
            db.ChangeTracker.Clear();
            if (!retryOnDeadlock || (await stays.BusyRoomsAsync([room.Id], stay, null, ct)).Count > 0)
            {
                return BookingErrors.RoomUnavailable;
            }

            return await CreateAsync(r, source, retryOnDeadlock: false, ct);
        }

        return await ToDtoAsync(booking, ct);
    }

    public async Task<Result<BookingDto>> UpdateAsync(Guid id, UpdateBookingRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var room = await rooms.GetAsync(booking.Rooms[0].RoomId, ct);
        if (room is not null && r.Adults + r.Children > room.Capacity)
        {
            return BookingErrors.CapacityExceeded(room.Capacity);
        }

        if (r.Version is { } version)
        {
            db.Entry(booking).Property(b => b.Version).OriginalValue = version;
        }

        var phone = string.IsNullOrWhiteSpace(r.GuestPhone) ? null : PhoneNumber.Parse(r.GuestPhone).Value.Value;
        var email = string.IsNullOrWhiteSpace(r.GuestEmail) ? null : r.GuestEmail.Trim().ToLowerInvariant();
        booking.UpdateDetails(new GuestContact(r.GuestName, phone, email), r.Adults, r.Children, r.Comment, currentUser.UserId, clock.UtcNow);
        // Без персональных данных: только состав гостей.
        audit.Record(new AuditEntry("booking.updated", "Booking", id.ToString(), After: new { r.Adults, r.Children }));
        return await SaveAsync(booking, ct);
    }

    public async Task<Result<BookingDto>> ConfirmAsync(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var result = booking.Confirm(currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("booking.confirmed", "Booking", id.ToString()));
        return await SaveAsync(booking, ct);
    }

    public async Task<Result<BookingDto>> CancelAsync(Guid id, CancelBookingRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var now = clock.UtcNow;
        var settings = await stays.SettingsAsync(ct);
        var terms = BookingPenalties.ForCancel(booking, BookingRules.Penalties(settings, booking.Kind), now);
        var waive = r.WaivePenalty && !terms.IsFree;
        if (waive && !await CanWaiveAsync(ct))
        {
            return BookingErrors.PenaltyWaiveForbidden;
        }

        var result = booking.Cancel(r.Reason, terms.Penalty, waive, currentUser.UserId, now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("booking.cancelled", "Booking", id.ToString(),
            After: new { r.Reason, terms.Penalty, Waived = waive }));
        return await SaveAsync(booking, ct);
    }

    public async Task<Result<CancellationQuoteDto>> QuoteCancellationAsync(Guid id, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: false, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var rules = BookingRules.Penalties(await stays.SettingsAsync(ct), booking.Kind);
        var terms = BookingPenalties.ForCancel(booking, rules, clock.UtcNow);
        return new CancellationQuoteDto(
            terms.IsFree,
            terms.FreeUntil is { } free ? stays.Format(free) : null,
            terms.Penalty,
            terms.Units,
            booking.Kind == BookingKind.Hourly ? "Hour" : "Night",
            await CanWaiveAsync(ct),
            BookingPenalties.ForNoShow(booking, rules));
    }

    public async Task<Result<BookingDto>> MarkNoShowAsync(Guid id, NoShowRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (r.Version is { } version)
        {
            db.Entry(booking).Property(b => b.Version).OriginalValue = version;
        }

        var penalty = BookingPenalties.ForNoShow(booking, BookingRules.Penalties(await stays.SettingsAsync(ct), booking.Kind));
        var waive = r.WaivePenalty && penalty > 0;
        if (waive && !await CanWaiveAsync(ct))
        {
            return BookingErrors.PenaltyWaiveForbidden;
        }

        var result = booking.MarkNoShow(penalty, waive, currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("booking.no_show", "Booking", id.ToString(), After: new { booking.Number, Penalty = penalty, Waived = waive, Auto = false }));
        return await SaveAsync(booking, ct);
    }

    public async Task<Result<RescheduleQuoteDto>> QuoteRescheduleAsync(Guid id, RescheduleBookingRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: false, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var plan = await PlanRescheduleAsync(booking, r, ct);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var p = plan.Value;
        return new RescheduleQuoteDto(stays.Format(p.Stay.StartsAt), stays.Format(p.Stay.EndsAt), p.Room.Id, p.Room.Number, booking.TotalAmount, p.Quote.Total, p.Quote);
    }

    public async Task<Result<BookingDto>> RescheduleAsync(Guid id, RescheduleBookingRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (r.Version is { } version)
        {
            db.Entry(booking).Property(b => b.Version).OriginalValue = version;
        }

        var plan = await PlanRescheduleAsync(booking, r, ct);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var p = plan.Value;
        var before = new { booking.StartsAt, booking.EndsAt, booking.Rooms[0].RoomId, booking.TotalAmount };
        var result = booking.Reschedule(p.Stay, p.Room.Id, p.Room.RoomTypeId, ToBookingPrice(p.Quote, p.PromoCode), currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("booking.rescheduled", "Booking", id.ToString(),
            Before: before, After: new { booking.StartsAt, booking.EndsAt, RoomId = p.Room.Id, booking.TotalAmount }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }
        catch (Exception ex) when (SqlState(ex) == ExclusionViolation)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.RoomUnavailable;
        }
        catch (Exception ex) when (SqlState(ex) == DeadlockDetected)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(booking, ct);
    }

    /// <summary>
    /// План переноса: номер подходит (продаётся, вмещает, по часам — если почасовая), время свободно (кроме самой брони),
    /// цена — по текущим ценам с тем же промокодом и услугами; если промокод на новое время не действует — без него.
    /// </summary>
    private async Task<Result<ReschedulePlan>> PlanRescheduleAsync(Booking booking, RescheduleBookingRequest r, CancellationToken ct)
    {
        if (!BookingStatusMachine.CanReschedule(booking.Status))
        {
            return Error.Conflict("booking.reschedule_not_allowed", "Перенести можно только бронь до заселения.");
        }

        if (r.IsHourly() != (booking.Kind == BookingKind.Hourly))
        {
            return Error.Validation("booking.reschedule_kind", "Нельзя сменить вид брони (часы/сутки) — отмените и создайте новую.");
        }

        var room = await rooms.GetAsync(r.RoomId, ct);
        if (room is null)
        {
            return BookingErrors.RoomNotFound;
        }

        if (!room.IsSellable)
        {
            return BookingErrors.RoomNotSellable;
        }

        if (booking.Kind == BookingKind.Hourly && !room.SellsHourly)
        {
            return BookingErrors.RoomNotHourly;
        }

        if (booking.Adults + booking.Children > room.Capacity)
        {
            return BookingErrors.CapacityExceeded(room.Capacity);
        }

        var resolved = await stays.ResolveAsync(r, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var stay = Stays.ForRoom(resolved.Value, await stays.SettingsAsync(ct), room);
        if ((await stays.BusyRoomsAsync([room.Id], stay, booking.Id, ct)).Count > 0)
        {
            return BookingErrors.RoomUnavailable;
        }

        var services = booking.Services.Select(x => new ServiceLine(x.ServiceId, x.Quantity)).ToList();
        var promo = booking.PromoCode;
        var quote = await stays.PriceAsync(room, stay, promo, services, ct);
        if (quote.IsFailure && promo is not null && quote.Error.Code.StartsWith("pricing.promo", StringComparison.Ordinal))
        {
            promo = null;
            quote = await stays.PriceAsync(room, stay, null, services, ct);
        }

        return quote.IsFailure ? quote.Error : new ReschedulePlan(stay, room, quote.Value, quote.Value.DiscountId is null ? null : promo);
    }

    private static BookingPrice ToBookingPrice(PriceQuote q, string? promoCode) => new(
        q.AccommodationTotal,
        q.DiscountAmount,
        q.ServicesTotal,
        q.Total,
        q.DiscountId,
        q.DiscountId is null ? null : promoCode?.Trim().ToUpperInvariant(),
        q.Services.Select(x => new BookingPriceServiceLine(x.ServiceId, x.Name, x.Quantity, x.UnitPrice, x.PerNight, x.Total)).ToList(),
        JsonSerializer.Serialize(q, JsonSerializerOptions.Web));

    /// <summary>Есть ли у текущего сотрудника право отменять без штрафа.</summary>
    private async Task<bool> CanWaiveAsync(CancellationToken ct) =>
        currentUser.UserId is { } userId
        && await access.GetAsync(userId, ct) is { } user
        && user.Permissions.Contains(Permissions.BookingCancelPenaltyOverride);

    public async Task<Result<ExtensionQuoteDto>> QuoteExtensionAsync(Guid id, int units, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: false, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var plan = await PlanExtensionAsync(booking, units, ct);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var p = plan.Value;
        return new ExtensionQuoteDto(booking.Kind.ToString(), units, stays.Format(p.Stay.EndsAt), p.Price.Total, p.Price, p.MaxUnits,
            p.NextStart is { } next ? stays.Format(next) : null);
    }

    public async Task<Result<BookingDto>> ExtendAsync(Guid id, ExtendBookingRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(id, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (r.Version is { } version)
        {
            db.Entry(booking).Property(b => b.Version).OriginalValue = version;
        }

        var plan = await PlanExtensionAsync(booking, r.Units, ct);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var p = plan.Value;
        var result = booking.Extend(p.Stay, r.Units, p.Price.Total, JsonSerializer.Serialize(p.Price, JsonSerializerOptions.Web), currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("booking.extended", "Booking", id.ToString(),
            After: new { booking.Number, Kind = booking.Kind.ToString(), r.Units, p.Stay.EndsAt, Amount = p.Price.Total }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }
        catch (Exception ex) when (SqlState(ex) == ExclusionViolation)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ExtensionConflict(stays.Format(p.Stay.EndsAt));
        }
        catch (Exception ex) when (SqlState(ex) == DeadlockDetected)
        {
            // Номер в этот момент бронируют параллельно — пусть пользователь обновит и повторит.
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(booking, ct);
    }

    public async Task<Result<IReadOnlyList<BookingHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        if (!await db.Set<Booking>().AnyAsync(b => b.Id == id && b.HotelId == hotelId, ct))
        {
            return BookingErrors.NotFound;
        }

        var history = await db.Set<BookingHistoryEntry>().AsNoTracking()
            .Where(h => h.BookingId == id)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(ct);
        var names = await users.GetNamesAsync(history.Where(h => h.ChangedBy != null).Select(h => h.ChangedBy!.Value), ct);
        return history.Select(h => new BookingHistoryDto(
                h.Id, h.EventType, Parse(h.OldValues), Parse(h.NewValues), h.ChangedBy,
                h.ChangedBy is { } by && names.TryGetValue(by, out var name) ? name : null, h.ChangedAt, h.Comment))
            .ToList();
    }

    /// <summary>
    /// План продления: новый период, цена добавленного времени (по текущим ценам, без скидки и услуг),
    /// сколько максимум можно добавить до следующей брони. Ошибка — если номер дальше занят или нарушены правила.
    /// </summary>
    private async Task<Result<ExtensionPlan>> PlanExtensionAsync(Booking booking, int units, CancellationToken ct)
    {
        if (!BookingStatusMachine.CanExtend(booking.Status))
        {
            return Error.Conflict("booking.extend_not_allowed", "Продлить можно только подтверждённую бронь или бронь заселённого гостя.");
        }

        if (units < 1)
        {
            return Error.Validation("booking.extend_invalid", "Продление — минимум на 1.");
        }

        var settings = await stays.SettingsAsync(ct);
        var line = booking.Rooms[0];
        var room = await rooms.GetAsync(line.RoomId, ct);
        if (room is null)
        {
            return BookingErrors.RoomNotFound;
        }

        StayPeriod extended;
        StayPeriod added;
        if (booking.Kind == BookingKind.Hourly)
        {
            if ((booking.Hours ?? 0) + units > settings.HourlyMaxHours)
            {
                return BookingErrors.ExtensionTooLong(settings.HourlyMaxHours);
            }

            if (!room.SellsHourly)
            {
                return BookingErrors.RoomNotHourly;
            }

            var start = stays.Local(booking.StartsAt);
            extended = stays.Hourly(settings, start, (booking.Hours ?? 0) + units);
            added = stays.Hourly(settings, stays.Local(booking.EndsAt), units);
        }
        else
        {
            if (booking.Nights + units > BookingErrors.MaxNights)
            {
                return Error.BusinessRule("booking.extension_too_long", $"Не более {BookingErrors.MaxNights} ночей в одной брони.");
            }

            var full = stays.Nightly(settings, booking.CheckIn, booking.CheckOut.AddDays(units));
            extended = full with { StartsAt = booking.StartsAt };
            added = stays.Nightly(settings, booking.CheckOut, booking.CheckOut.AddDays(units));
        }

        extended = Stays.ForRoom(extended, settings, room);
        var nextStart = await stays.NextBookingStartAsync(line.RoomId, booking.Id, line.EndsAt, ct);
        var buffer = extended.BlockedUntil - extended.EndsAt;
        int? maxUnits = null;
        if (nextStart is { } next)
        {
            var latestEnd = next - buffer;
            maxUnits = booking.Kind == BookingKind.Hourly
                ? (int)Math.Floor((latestEnd - line.EndsAt).TotalHours)
                : CountNights(settings, booking.CheckOut, latestEnd);
            if (extended.BlockedUntil > next)
            {
                return BookingErrors.ExtensionConflict(stays.Format(latestEnd));
            }
        }

        var price = await stays.PriceAsync(room, added, null, [], ct);
        if (price.IsFailure)
        {
            return price.Error;
        }

        return new ExtensionPlan(extended, price.Value, maxUnits is { } max ? Math.Max(0, max) : null, nextStart);
    }

    /// <summary>Сколько ночей можно добавить, чтобы выезд (в час выезда) не был позже <paramref name="latestEnd"/>.</summary>
    private int CountNights(HotelSettingsSnapshot settings, DateOnly checkOut, DateTimeOffset latestEnd)
    {
        var nights = 0;
        while (nights < BookingErrors.MaxExtendNights && clock.ToUtc(checkOut.AddDays(nights + 1), settings.CheckOutTime) <= latestEnd)
        {
            nights++;
        }

        return nights;
    }

    private async Task<Result<BookingDto>> SaveAsync(Booking booking, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(booking, ct);
    }

    /// <summary>
    /// Следующий номер «Ч-2111-07»: счётчик за день гостиницы увеличивается одной командой БД (INSERT … ON CONFLICT DO UPDATE),
    /// поэтому параллельные брони не получат одинаковый номер. Пропуски возможны (бронь не сохранилась) — это нормально.
    /// </summary>
    private async Task<(string Number, int Year)> NextNumberAsync(Guid hotelId, BookingKind kind, CancellationToken ct)
    {
        var day = clock.Today;
        var values = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO bookings.booking_number_counters (hotel_id, day, last) VALUES ({hotelId}, {day}, 1)
                ON CONFLICT (hotel_id, day) DO UPDATE SET last = booking_number_counters.last + 1
                RETURNING last AS "Value"
                """)
            .ToListAsync(ct);
        return (BookingNumbers.Format(kind, day, values[0]), day.Year);
    }

    private async Task<Booking?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Booking>()
            .Include(b => b.Rooms).Include(b => b.Services).Include(b => b.Extensions)
            .Where(b => b.Id == id && b.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<BookingDto> ToDtoAsync(Booking b, CancellationToken ct)
    {
        var line = b.Rooms[0];
        var room = await rooms.GetAsync(line.RoomId, ct);
        var actions = new List<string>();
        if (BookingStatusMachine.CanTransition(b.Status, BookingStatus.Confirmed))
        {
            actions.Add("confirm");
        }

        if (BookingStatusMachine.CanExtend(b.Status))
        {
            actions.Add("extend");
        }

        if (BookingStatusMachine.CanTransition(b.Status, BookingStatus.Cancelled))
        {
            actions.Add("cancel");
        }

        if (BookingStatusMachine.CanReschedule(b.Status))
        {
            actions.Add("reschedule");
        }

        if (b.Status == BookingStatus.Confirmed && clock.UtcNow >= b.StartsAt)
        {
            actions.Add("noShow");
        }

        // Заселить — подтверждённую бронь (окно и готовность номера проверит превью); выселить — заселённого гостя.
        if (b.Status == BookingStatus.Confirmed && clock.UtcNow < b.EndsAt)
        {
            actions.Add("checkIn");
        }

        if (b.Status == BookingStatus.CheckedIn)
        {
            actions.Add("checkOut");
        }

        var authors = await users.GetNamesAsync(b.Extensions.Where(e => e.CreatedBy != null).Select(e => e.CreatedBy!.Value), ct);
        var extensions = b.ActiveExtensions
            .OrderBy(e => e.CreatedAt)
            .Select(e => new BookingExtensionDto(e.Unit == BookingKind.Hourly ? "Hour" : "Night", e.Quantity, stays.Format(e.PreviousEndsAt), stays.Format(e.NewEndsAt),
                e.Amount, e.CreatedBy is { } by && authors.TryGetValue(by, out var n) ? n : null, e.CreatedAt))
            .ToList();

        return new BookingDto(
            b.Id, b.Number, b.Status.ToString(), b.Source.ToString(), b.CheckIn, b.CheckOut, b.Nights,
            b.Adults, b.Children, b.GuestName, b.GuestPhone, b.GuestEmail, b.Comment,
            new BookingRoomDto(line.RoomId, room?.Number ?? "—", line.RoomTypeId, room?.RoomTypeName ?? "—"),
            b.AccommodationAmount, b.DiscountAmount, b.ServicesAmount, b.TotalAmount, b.PromoCode,
            JsonSerializer.Deserialize<PriceQuote>(b.PriceSnapshot, JsonSerializerOptions.Web),
            b.Services.Select(s => new BookingServiceDto(s.ServiceId, s.Name, s.Quantity, s.UnitPrice, s.PerNight, s.Total)).ToList(),
            actions, b.ConfirmedAt, b.CancelledAt, b.CancelReason, b.CreatedAt, b.Version,
            b.Kind.ToString(), stays.Format(b.StartsAt), stays.Format(b.EndsAt), b.Hours, b.ExtensionsAmount, extensions, b.GuestId,
            b.HoldUntil is { } hold ? stays.Format(hold) : null, b.PenaltyAmount, b.PenaltyKind?.ToString(), b.PenaltyWaived,
            b.ActualCheckInAt is { } checkedIn ? stays.Format(checkedIn) : null, b.ActualCheckOutAt is { } checkedOut ? stays.Format(checkedOut) : null, b.EarlyDepartureCredit);
    }

    private static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// Гость брони: выбранный из базы (контакт — из запроса, если указан, иначе из карточки)
    /// или найденный/созданный по контакту из запроса.
    /// </summary>
    private async Task<Result<(Guid GuestId, GuestContact Contact)>> ResolveGuestAsync(CreateBookingRequest r, CancellationToken ct)
    {
        var phone = string.IsNullOrWhiteSpace(r.GuestPhone) ? null : PhoneNumber.Parse(r.GuestPhone).Value.Value;
        var email = string.IsNullOrWhiteSpace(r.GuestEmail) ? null : r.GuestEmail.Trim().ToLowerInvariant();
        if (r.GuestId is { } guestId)
        {
            var known = await guests.GetAsync(guestId, ct);
            if (known is null)
            {
                return GuestErrors.NotFound;
            }

            if (known.IsAnonymized)
            {
                return GuestErrors.Anonymized;
            }

            var name = string.IsNullOrWhiteSpace(r.GuestName) ? known.FullName : r.GuestName;
            return (known.Id, new GuestContact(name, phone ?? known.Phone, email ?? known.Email));
        }

        var found = await guests.FindOrCreateAsync(new Hms.Modules.Guests.Contracts.GuestContact(r.GuestName, phone, email), ct);
        return found.IsFailure ? found.Error : (found.Value.Id, new GuestContact(r.GuestName, phone, email));
    }

    /// <summary>SQLSTATE нарушения ограничения EXCLUDE (пересечение броней).</summary>
    private const string ExclusionViolation = "23P01";

    /// <summary>SQLSTATE взаимной блокировки транзакций PostgreSQL.</summary>
    private const string DeadlockDetected = "40P01";

    /// <summary>
    /// Код ошибки PostgreSQL в цепочке исключений: EF оборачивает его в DbUpdateException,
    /// а при исчерпании автоповторов (временные сбои, deadlock) — ещё и в InvalidOperationException.
    /// </summary>
    private static string? SqlState(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is DbException { SqlState: { } state })
            {
                return state;
            }
        }

        return null;
    }

    private sealed record ReschedulePlan(StayPeriod Stay, RoomInfo Room, PriceQuote Quote, string? PromoCode);

    private sealed record ExtensionPlan(StayPeriod Stay, PriceQuote Price, int? MaxUnits, DateTimeOffset? NextStart);
}
