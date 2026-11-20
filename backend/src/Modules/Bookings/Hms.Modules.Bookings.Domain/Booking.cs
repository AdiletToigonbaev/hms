using Hms.Modules.Bookings.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Bookings.Domain;

public enum BookingSource
{
    Admin,
    Web,
}

/// <summary>
/// Переходы статусов брони (docs/database/database-design.md §4). Остальные запрещены.
/// </summary>
public static class BookingStatusMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending] = [BookingStatus.AwaitingPayment, BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Expired],
        [BookingStatus.AwaitingPayment] = [BookingStatus.Confirmed, BookingStatus.Expired, BookingStatus.Cancelled],
        [BookingStatus.Confirmed] = [BookingStatus.CheckedIn, BookingStatus.Cancelled, BookingStatus.NoShow],
        [BookingStatus.CheckedIn] = [BookingStatus.CheckedOut],
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Занимает ли бронь номер (участвует в ограничении против пересечений).</summary>
    public static bool IsBlocking(BookingStatus status) =>
        status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed or BookingStatus.CheckedIn;
}

/// <summary>Контакт гостя в брони. Карточка гостя в CRM и связь с ней — Sprint 4 (модуль Guests).</summary>
public sealed record GuestContact(string FullName, string? Phone, string? Email);

/// <summary>
/// Бронь (ТЗ §5). Стоимость фиксируется снимком при создании (BOOK-4); все изменения пишутся в историю (BOOK-7).
/// Номер на даты [CheckIn, CheckOut) занимает строка <see cref="BookingRoom"/> — по ней работает ограничение БД.
/// </summary>
public sealed class Booking
{
    private readonly List<BookingRoom> _rooms = [];
    private readonly List<BookingServiceLine> _services = [];
    private readonly List<BookingHistoryEntry> _history = [];

    private Booking()
    {
        Number = string.Empty;
        GuestName = string.Empty;
        PriceSnapshot = "{}";
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    /// <summary>Номер для людей: «26-000042» (год + порядковый номер).</summary>
    public string Number { get; private set; }

    public BookingStatus Status { get; private set; }

    public BookingSource Source { get; private set; }

    public DateOnly CheckIn { get; private set; }

    public DateOnly CheckOut { get; private set; }

    public int Adults { get; private set; }

    public int Children { get; private set; }

    public string GuestName { get; private set; }

    public string? GuestPhone { get; private set; }

    public string? GuestEmail { get; private set; }

    /// <summary>Карточка гостя (появится в Sprint 4).</summary>
    public Guid? GuestId { get; private set; }

    public string? Comment { get; private set; }

    public Guid? DiscountId { get; private set; }

    public string? PromoCode { get; private set; }

    public decimal AccommodationAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal ServicesAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>Снимок расчёта (цены по ночам, тарифы, скидка, услуги) — JSON.</summary>
    public string PriceSnapshot { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<BookingRoom> Rooms => _rooms;

    public IReadOnlyList<BookingServiceLine> Services => _services;

    public IReadOnlyList<BookingHistoryEntry> History => _history;

    public DateRange Stay => new(CheckIn, CheckOut);

    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    public static Booking Create(
        Guid hotelId,
        string number,
        BookingSource source,
        DateRange stay,
        int adults,
        int children,
        GuestContact guest,
        string? comment,
        Guid roomId,
        Guid roomTypeId,
        BookingPrice price,
        bool confirm,
        Guid? createdBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(guest);
        ArgumentNullException.ThrowIfNull(price);
        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            Number = number,
            Source = source,
            Status = confirm ? BookingStatus.Confirmed : BookingStatus.Pending,
            CheckIn = stay.Start,
            CheckOut = stay.End,
            Adults = adults,
            Children = children,
            GuestName = guest.FullName.Trim(),
            GuestPhone = guest.Phone,
            GuestEmail = guest.Email,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            DiscountId = price.DiscountId,
            PromoCode = price.PromoCode,
            AccommodationAmount = price.Accommodation,
            DiscountAmount = price.Discount,
            ServicesAmount = price.Services,
            TotalAmount = price.Total,
            PriceSnapshot = price.SnapshotJson,
            ConfirmedAt = confirm ? now : null,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        booking._rooms.Add(new BookingRoom(booking.Id, roomId, roomTypeId, stay, BookingStatusMachine.IsBlocking(booking.Status)));
        booking._services.AddRange(price.ServiceLines.Select(s => new BookingServiceLine(booking.Id, s.ServiceId, s.Name, s.Quantity, s.UnitPrice, s.PerNight, s.Total, createdBy, now)));
        booking.AddHistory("created", null, new { Status = booking.Status.ToString(), stay.Start, stay.End, roomId, price.Total }, createdBy, now, null);
        return booking;
    }

    public Result Confirm(Guid? by, DateTimeOffset now)
    {
        var result = ChangeStatus(BookingStatus.Confirmed, by, now, null);
        if (result.IsSuccess)
        {
            ConfirmedAt = now;
        }

        return result;
    }

    public Result Cancel(string reason, Guid? by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("booking.reason_required", "Укажите причину отмены.");
        }

        var result = ChangeStatus(BookingStatus.Cancelled, by, now, reason.Trim());
        if (result.IsSuccess)
        {
            CancelledAt = now;
            CancelReason = reason.Trim();
        }

        return result;
    }

    /// <summary>Смена статуса по машине переходов; пересчитывает блокировку номера в той же единице работы.</summary>
    public Result ChangeStatus(BookingStatus target, Guid? by, DateTimeOffset now, string? comment)
    {
        if (!BookingStatusMachine.CanTransition(Status, target))
        {
            return Error.Conflict("booking.invalid_transition", $"Нельзя перевести бронь из статуса «{Status}» в «{target}».");
        }

        var from = Status;
        Status = target;
        foreach (var room in _rooms)
        {
            room.SetBlocking(BookingStatusMachine.IsBlocking(target));
        }

        UpdatedAt = now;
        AddHistory("status_changed", new { Status = from.ToString() }, new { Status = target.ToString() }, by, now, comment);
        return Result.Success();
    }

    public void UpdateDetails(GuestContact guest, int adults, int children, string? comment, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(guest);
        var before = new { GuestName, GuestPhone, GuestEmail, Adults, Children, Comment };
        GuestName = guest.FullName.Trim();
        GuestPhone = guest.Phone;
        GuestEmail = guest.Email;
        Adults = adults;
        Children = children;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAt = now;
        AddHistory("details_changed", before, new { GuestName, GuestPhone, GuestEmail, Adults, Children, Comment }, by, now, null);
    }

    private void AddHistory(string eventType, object? oldValues, object? newValues, Guid? by, DateTimeOffset now, string? comment) =>
        _history.Add(new BookingHistoryEntry(Id, eventType, oldValues, newValues, by, now, comment));
}

/// <summary>Номер в брони. <see cref="IsBlocking"/> = бронь занимает номер (Pending, AwaitingPayment, Confirmed, CheckedIn).</summary>
public sealed class BookingRoom
{
    private BookingRoom()
    {
    }

    public BookingRoom(Guid bookingId, Guid roomId, Guid roomTypeId, DateRange stay, bool isBlocking)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        RoomId = roomId;
        RoomTypeId = roomTypeId;
        CheckIn = stay.Start;
        CheckOut = stay.End;
        IsBlocking = isBlocking;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public Guid RoomId { get; private set; }

    public Guid RoomTypeId { get; private set; }

    public DateOnly CheckIn { get; private set; }

    public DateOnly CheckOut { get; private set; }

    public bool IsBlocking { get; private set; }

    internal void SetBlocking(bool blocking) => IsBlocking = blocking;
}

public sealed class BookingServiceLine
{
    private BookingServiceLine()
    {
        Name = string.Empty;
    }

    public BookingServiceLine(Guid bookingId, Guid serviceId, string name, int quantity, decimal unitPrice, bool perNight, decimal total, Guid? addedBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        ServiceId = serviceId;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        PerNight = perNight;
        Total = total;
        AddedBy = addedBy;
        AddedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public Guid ServiceId { get; private set; }

    /// <summary>Название на момент заказа (снимок).</summary>
    public string Name { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public bool PerNight { get; private set; }

    public decimal Total { get; private set; }

    public Guid? AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}

/// <summary>Запись истории брони (BOOK-7). Только добавление.</summary>
public sealed class BookingHistoryEntry
{
    private BookingHistoryEntry()
    {
        EventType = string.Empty;
    }

    public BookingHistoryEntry(Guid bookingId, string eventType, object? oldValues, object? newValues, Guid? changedBy, DateTimeOffset at, string? comment)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        EventType = eventType;
        OldValues = oldValues is null ? null : System.Text.Json.JsonSerializer.Serialize(oldValues, System.Text.Json.JsonSerializerOptions.Web);
        NewValues = newValues is null ? null : System.Text.Json.JsonSerializer.Serialize(newValues, System.Text.Json.JsonSerializerOptions.Web);
        ChangedBy = changedBy;
        ChangedAt = at;
        Comment = comment;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public string EventType { get; private set; }

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Comment { get; private set; }
}

/// <summary>Зафиксированная стоимость брони (из расчёта модуля Pricing).</summary>
public sealed record BookingPrice(
    decimal Accommodation,
    decimal Discount,
    decimal Services,
    decimal Total,
    Guid? DiscountId,
    string? PromoCode,
    IReadOnlyList<BookingPriceServiceLine> ServiceLines,
    string SnapshotJson);

public sealed record BookingPriceServiceLine(Guid ServiceId, string Name, int Quantity, decimal UnitPrice, bool PerNight, decimal Total);
