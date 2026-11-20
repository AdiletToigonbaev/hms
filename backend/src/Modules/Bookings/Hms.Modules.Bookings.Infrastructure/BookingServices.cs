using System.Data.Common;
using System.Text.Json;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Pricing.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>Подбор свободных номеров и расчёт стоимости (BOOK-2, BOOK-4). Только на backend.</summary>
internal sealed class AvailabilityService(DbContext db, IRoomDirectory rooms, IPriceCalculator prices, IHotelClock clock) : IAvailabilityService
{
    public async Task<Result<IReadOnlyList<AvailableRoomDto>>> FindRoomsAsync(AvailabilityQuery q, CancellationToken ct)
    {
        if (q.CheckIn < clock.Today)
        {
            return BookingErrors.PastDates;
        }

        var stay = new DateRange(q.CheckIn, q.CheckOut);
        var guests = (q.Adults ?? 1) + (q.Children ?? 0);
        var free = await FreeRoomsAsync(stay, guests, q.RoomTypeId, ct);

        var result = new List<AvailableRoomDto>(free.Count);
        foreach (var room in free)
        {
            var quote = await prices.QuoteAsync(new PriceRequest(room.RoomTypeId, room.BasePrice, room.PriceOverride, stay, null, []), ct);
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

        // Сайту — только категории, количество и цена «от»: номера комнат и занятость конкретных номеров не раскрываются.
        return found.Value
            .GroupBy(r => (r.RoomTypeId, r.RoomTypeName))
            .Select(g => new PublicAvailabilityDto(g.Key.RoomTypeId, g.Key.RoomTypeName, g.Max(r => r.Capacity), g.Count(), q.CheckOut.DayNumber - q.CheckIn.DayNumber, g.Min(r => r.Quote.Total)))
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

        return await prices.QuoteAsync(
            new PriceRequest(room.RoomTypeId, room.BasePrice, room.PriceOverride, new DateRange(r.CheckIn, r.CheckOut), r.PromoCode, r.Services ?? []), ct);
    }

    public async Task<Result<PriceQuote>> QuotePublicAsync(PublicQuoteRequest r, CancellationToken ct)
    {
        if (r.CheckIn < clock.Today)
        {
            return BookingErrors.PastDates;
        }

        var stay = new DateRange(r.CheckIn, r.CheckOut);
        var free = await FreeRoomsAsync(stay, r.Adults + r.Children, r.RoomTypeId, ct);
        var cheapest = free.OrderBy(x => x.PriceOverride ?? x.BasePrice).FirstOrDefault();
        if (cheapest is null)
        {
            return BookingErrors.RoomTypeUnavailable;
        }

        return await prices.QuoteAsync(new PriceRequest(cheapest.RoomTypeId, cheapest.BasePrice, cheapest.PriceOverride, stay, r.PromoCode, r.Services ?? []), ct);
    }

    /// <summary>Номера в продаже, вмещающие гостей и не занятые блокирующими бронями на даты [CheckIn, CheckOut).</summary>
    internal async Task<List<RoomInfo>> FreeRoomsAsync(DateRange stay, int guests, Guid? roomTypeId, CancellationToken ct)
    {
        var candidates = (await rooms.ListSellableAsync(roomTypeId, ct)).Where(r => r.Capacity >= guests).ToList();
        var ids = candidates.Select(r => r.Id).ToList();
        var busy = (await db.Set<BookingRoom>().AsNoTracking()
                .Where(b => b.IsBlocking && ids.Contains(b.RoomId) && b.CheckIn < stay.End && b.CheckOut > stay.Start)
                .Select(b => b.RoomId)
                .ToListAsync(ct))
            .ToHashSet();
        return candidates.Where(r => !busy.Contains(r.Id)).ToList();
    }
}

internal sealed class BookingService(
    DbContext db,
    IHotelContext hotel,
    IRoomDirectory rooms,
    IPriceCalculator prices,
    IUserDirectory users,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IHotelClock clock) : IBookingService
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

        // Пересечение с периодом [From, To]: бронь, у которой проживание затрагивает хотя бы один день периода.
        if (q.From is { } from)
        {
            query = query.Where(b => b.CheckOut > from);
        }

        if (q.To is { } to)
        {
            query = query.Where(b => b.CheckIn <= to);
        }

        if (q.RoomId is { } roomId)
        {
            query = query.Where(b => b.Rooms.Any(r => r.RoomId == roomId));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = $"%{q.Search.Trim().ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311 // ToLower транслируется в SQL lower()
            query = query.Where(b => EF.Functions.Like(b.Number, s) || EF.Functions.Like(b.GuestName.ToLower(), s)
                || (b.GuestPhone != null && EF.Functions.Like(b.GuestPhone, s)) || (b.GuestEmail != null && EF.Functions.Like(b.GuestEmail, s)));
#pragma warning restore CA1304, CA1311
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(b => b.CheckIn).ThenBy(b => b.Number)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(b => new { Booking = b, RoomId = b.Rooms.Select(r => r.RoomId).First() })
            .ToListAsync(ct);
        var roomInfo = await rooms.GetManyAsync(items.Select(i => i.RoomId), ct);

        return new PagedResult<BookingListItemDto>(
            items.Select(i =>
            {
                var b = i.Booking;
                var room = roomInfo.GetValueOrDefault(i.RoomId);
                return new BookingListItemDto(b.Id, b.Number, b.Status.ToString(), b.CheckIn, b.CheckOut, b.CheckOut.DayNumber - b.CheckIn.DayNumber,
                    b.GuestName, b.GuestPhone, room?.Number ?? "—", room?.RoomTypeName ?? "—", b.TotalAmount, b.CreatedAt);
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

    public async Task<Result<BookingDto>> CreateAsync(CreateBookingRequest r, CancellationToken ct)
    {
        if (r.CheckIn < clock.Today)
        {
            return BookingErrors.PastDates;
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

        if (r.Adults + r.Children > room.Capacity)
        {
            return BookingErrors.CapacityExceeded(room.Capacity);
        }

        var stay = new DateRange(r.CheckIn, r.CheckOut);

        // Быстрая проверка для понятной ошибки; гарантию против гонок даёт ограничение EXCLUDE в БД.
        if (await db.Set<BookingRoom>().AnyAsync(b => b.RoomId == room.Id && b.IsBlocking && b.CheckIn < stay.End && b.CheckOut > stay.Start, ct))
        {
            return BookingErrors.RoomUnavailable;
        }

        var quote = await prices.QuoteAsync(new PriceRequest(room.RoomTypeId, room.BasePrice, room.PriceOverride, stay, r.PromoCode, r.Services ?? []), ct);
        if (quote.IsFailure)
        {
            return quote.Error;
        }

        var q = quote.Value;
        var price = new BookingPrice(
            q.AccommodationTotal,
            q.DiscountAmount,
            q.ServicesTotal,
            q.Total,
            q.DiscountId,
            q.DiscountId is null ? null : r.PromoCode?.Trim().ToUpperInvariant(),
            q.Services.Select(s => new BookingPriceServiceLine(s.ServiceId, s.Name, s.Quantity, s.UnitPrice, s.PerNight, s.Total)).ToList(),
            JsonSerializer.Serialize(q, JsonSerializerOptions.Web));

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = clock.UtcNow;
        var number = await NextNumberAsync(now, ct);
        var phone = string.IsNullOrWhiteSpace(r.GuestPhone) ? null : PhoneNumber.Parse(r.GuestPhone).Value.Value;
        var email = string.IsNullOrWhiteSpace(r.GuestEmail) ? null : r.GuestEmail.Trim().ToLowerInvariant();

        var booking = Booking.Create(hotelId, number, BookingSource.Admin, stay, r.Adults, r.Children,
            new GuestContact(r.GuestName, phone, email), r.Comment, room.Id, room.RoomTypeId, price, r.Confirm, currentUser.UserId, now);
        db.Set<Booking>().Add(booking);
        audit.Record(new AuditEntry("booking.created", "Booking", booking.Id.ToString(),
            After: new { booking.Number, Status = booking.Status.ToString(), room = room.Number, stay.Start, stay.End, booking.TotalAmount }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23P01" })
        {
            // Параллельный запрос успел занять номер на пересекающиеся даты.
            db.ChangeTracker.Clear();
            return BookingErrors.RoomUnavailable;
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
        audit.Record(new AuditEntry("booking.updated", "Booking", id.ToString(), After: new { r.GuestName, r.Adults, r.Children }));
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

        var result = booking.Cancel(r.Reason, currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("booking.cancelled", "Booking", id.ToString(), After: new { r.Reason }));
        return await SaveAsync(booking, ct);
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

    private async Task<string> NextNumberAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Последовательность PostgreSQL: номера уникальны и без гонок при параллельном создании.
        var value = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{BookingsModelConfigurator.Schema}.{BookingsModelConfigurator.NumberSequence}') AS \"Value\"")
            .SingleAsync(ct);
        return $"{now:yy}-{value:D6}";
    }

    private async Task<Booking?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Booking>().Include(b => b.Rooms).Include(b => b.Services).Where(b => b.Id == id && b.HotelId == hotelId);
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

        if (BookingStatusMachine.CanTransition(b.Status, BookingStatus.Cancelled))
        {
            actions.Add("cancel");
        }

        return new BookingDto(
            b.Id, b.Number, b.Status.ToString(), b.Source.ToString(), b.CheckIn, b.CheckOut, b.CheckOut.DayNumber - b.CheckIn.DayNumber,
            b.Adults, b.Children, b.GuestName, b.GuestPhone, b.GuestEmail, b.Comment,
            new BookingRoomDto(line.RoomId, room?.Number ?? "—", line.RoomTypeId, room?.RoomTypeName ?? "—"),
            b.AccommodationAmount, b.DiscountAmount, b.ServicesAmount, b.TotalAmount, b.PromoCode,
            JsonSerializer.Deserialize<PriceQuote>(b.PriceSnapshot, JsonSerializerOptions.Web),
            b.Services.Select(s => new BookingServiceDto(s.ServiceId, s.Name, s.Quantity, s.UnitPrice, s.PerNight, s.Total)).ToList(),
            actions, b.ConfirmedAt, b.CancelledAt, b.CancelReason, b.CreatedAt, b.Version);
    }

    private static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
