using System.Globalization;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Reporting.Application;
using Hms.Modules.Reporting.Domain;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Time;
using Hms.SharedKernel;

namespace Hms.Modules.Reporting.Infrastructure;

/// <summary>Данные периода с применёнными фильтрами — общая основа дашборда и отчётов.</summary>
internal sealed record PeriodData(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PeriodDay> Days,
    IReadOnlyList<RoomInfo> Rooms,
    IReadOnlyList<BookingFact> Bookings,
    IReadOnlyList<PaymentFact> Payments,
    IReadOnlyList<RefundFact> Refunds,
    IReadOnlyList<ExpenseFact> Expenses,
    bool ExpensesApply,
    IReadOnlyList<string> Filters,
    int? FromHour = null,
    int? ToHour = null)
{
    public DateTimeOffset Start => Days[0].Start;

    public DateTimeOffset End => Days[^1].End;

    /// <summary>Доступные номера: не в архиве и не «вне продажи».</summary>
    public int AvailableRooms => Rooms.Count(r => r.Status != RoomStatus.OutOfService);
}

/// <summary>Загрузка фактов из модулей (только через контракты) и расчётные помощники.</summary>
internal sealed class ReportData(IBookingFacts bookings, IFinanceFacts finance, IRoomDirectory rooms, IHotelClock clock)
{
    public const int MaxDays = 366;

    public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Период: явные даты или day/week/month/year вокруг даты; по умолчанию — с начала месяца по сегодня.</summary>
    public Result<(DateOnly From, DateOnly To)> Resolve(DateOnly? from, DateOnly? to, string? period, DateOnly? date)
    {
        var today = clock.Today;
        (DateOnly From, DateOnly To) range;
        if (period is not null)
        {
            var a = date ?? today;
            range = period switch
            {
                "day" => (a, a),
                "week" => (a.AddDays(-(((int)a.DayOfWeek + 6) % 7)), a.AddDays(6 - (((int)a.DayOfWeek + 6) % 7))),
                "year" => (new DateOnly(a.Year, 1, 1), new DateOnly(a.Year, 12, 31)),
                _ => (new DateOnly(a.Year, a.Month, 1), new DateOnly(a.Year, a.Month, 1).AddMonths(1).AddDays(-1)),
            };
        }
        else
        {
            range = (from ?? new DateOnly(today.Year, today.Month, 1), to ?? today);
        }

        if (range.From > range.To)
        {
            return ReportErrors.PeriodInvalid;
        }

        if (range.To.DayNumber - range.From.DayNumber + 1 > MaxDays)
        {
            return ReportErrors.PeriodTooLong;
        }

        return range;
    }

    /// <summary>Часы: начало 0–23, окончание 1–24; в один день окончание позже начала.</summary>
    public static bool HoursValid(DateOnly from, DateOnly to, int? fromHour, int? toHour) =>
        fromHour is null or (>= 0 and <= 23)
        && toHour is null or (>= 1 and <= 24)
        && (from != to || (toHour ?? 24) > (fromHour ?? 0));

    /// <summary>
    /// Сутки периода в UTC. С часами — первый день начинается в FromHour, последний заканчивается в ToHour
    /// («сегодня с 06:00 до 22:00»); промежуточные дни — целиком.
    /// </summary>
    public IReadOnlyList<PeriodDay> Days(DateOnly from, DateOnly to, int? fromHour = null, int? toHour = null)
    {
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Select(d => new PeriodDay(d, clock.ToUtc(d, TimeOnly.MinValue), clock.ToUtc(d.AddDays(1), TimeOnly.MinValue)))
            .ToList();
        if (fromHour is { } fh)
        {
            days[0] = days[0] with { Start = clock.ToUtc(from, TimeOnly.MinValue).AddHours(fh) };
        }

        if (toHour is { } th)
        {
            days[^1] = days[^1] with { End = clock.ToUtc(to, TimeOnly.MinValue).AddHours(th) };
        }

        return days;
    }

    public async Task<PeriodData> LoadAsync(DateOnly from, DateOnly to, Guid? roomTypeId, string? method, CancellationToken ct, int? fromHour = null, int? toHour = null)
    {
        var days = Days(from, to, fromHour, toHour);
        var start = days[0].Start;
        var end = days[^1].End;
        var allRooms = await rooms.ListActiveAsync(ct);
        var bookingFacts = await bookings.ListAsync(start, end, ct);
        var payments = await finance.PaymentsAsync(start, end, ct);
        var refunds = await finance.RefundsAsync(start, end, ct);
        var expenses = await finance.ExpensesAsync(from, to, ct);
        var filters = new List<string>();
        var expensesApply = true;

        if (roomTypeId is { } typeId)
        {
            var typeName = allRooms.FirstOrDefault(r => r.RoomTypeId == typeId)?.RoomTypeName ?? "—";
            filters.Add($"Категория: {typeName}");
            allRooms = allRooms.Where(r => r.RoomTypeId == typeId).ToList();
            bookingFacts = bookingFacts.Where(b => b.RoomTypeId == typeId).ToList();
            // Платежи и возвраты — по броням этой категории (в том числе прошлых броней, оплаченных в периоде).
            var ids = payments.Select(p => p.BookingId).Concat(refunds.Select(r => r.BookingId)).OfType<Guid>().Distinct().ToList();
            var types = ids.Count == 0 ? new Dictionary<Guid, Guid>() : await bookings.RoomTypesAsync(ids, ct);
            bool Of(Guid? bookingId) => bookingId is { } id && types.GetValueOrDefault(id) == typeId;
            payments = payments.Where(p => Of(p.BookingId)).ToList();
            refunds = refunds.Where(r => Of(r.BookingId)).ToList();
            // Расходы гостиницы на категории не делятся — с фильтром по категории не учитываются.
            expenses = [];
            expensesApply = false;
        }

        if (Enum.TryParse<PaymentMethod>(method, out var m))
        {
            filters.Add($"Способ оплаты: {Labels.Method(m)}");
            payments = payments.Where(p => p.Method == m).ToList();
            refunds = refunds.Where(r => r.Method == m).ToList();
            expenses = expenses.Where(e => e.Method == m).ToList();
        }

        if ((fromHour is not null || toHour is not null) && expensesApply && expenses.Count > 0)
        {
            // У расходов есть только дата — они учитываются за целые дни периода.
            filters.Add("Расходы — за целые дни (у расходов нет времени)");
        }

        return new PeriodData(from, to, days, allRooms, bookingFacts, payments, refunds, expenses, expensesApply, filters, fromHour, toHour);
    }

    /// <summary>Бронь → вход расчёта: стоимость, проживание без услуг, штраф на дату отмены/неявки.</summary>
    public StayInput ToStay(BookingFact b) => new(
        b.Kind == "Hourly",
        b.Status switch
        {
            BookingStatus.Pending or BookingStatus.AwaitingPayment => StayStatus.Pending,
            BookingStatus.Confirmed => StayStatus.Confirmed,
            BookingStatus.CheckedIn => StayStatus.CheckedIn,
            BookingStatus.CheckedOut => StayStatus.CheckedOut,
            BookingStatus.Cancelled => StayStatus.Cancelled,
            BookingStatus.NoShow => StayStatus.NoShow,
            _ => StayStatus.Expired,
        },
        b.StartsAt,
        b.EndsAt,
        DateOnly.FromDateTime(clock.ToHotelTime(b.StartsAt).DateTime),
        DateOnly.FromDateTime(clock.ToHotelTime(b.EndsAt).DateTime),
        b.TotalAmount,
        b.AccommodationAmount - b.DiscountAmount + b.ExtensionsAmount - b.EarlyDepartureCredit + b.RoomChangeAmount,
        b.PenaltyAmount,
        b.Status == BookingStatus.Cancelled ? b.CancelledAt : b.Status == BookingStatus.NoShow ? b.StartsAt : null);

    public KpiResult Kpi(PeriodData d) => Domain.Kpi.Calculate(
        d.Days,
        d.AvailableRooms,
        d.Bookings.Select(ToStay).ToList(),
        d.Payments.Select(p => new MoneyInput(p.PaidAt, p.Amount)).ToList(),
        d.Refunds.Where(r => r.Status == "Paid" && r.PaidAt is not null).Select(r => new MoneyInput(r.PaidAt!.Value, r.Amount)).ToList(),
        d.Expenses.Select(e => new ExpenseInput(e.Date, e.Amount)).ToList());

    public IReadOnlyList<DayPoint> Daily(PeriodData d) => Domain.Kpi.Daily(
        d.Days,
        d.AvailableRooms,
        d.Bookings.Select(ToStay).ToList(),
        d.Payments.Select(p => new MoneyInput(p.PaidAt, p.Amount)).ToList(),
        d.Expenses.Select(e => new ExpenseInput(e.Date, e.Amount)).ToList());

    /// <summary>Время гостиницы «yyyy-MM-ddTHH:mm».</summary>
    public string Format(DateTimeOffset utc) => clock.ToHotelTime(utc).ToString("yyyy-MM-dd'T'HH:mm", Invariant);

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", Invariant);

    public DateTimeOffset UtcNow => clock.UtcNow;

    public DateOnly Today => clock.Today;
}

/// <summary>Русские подписи значений в отчётах (как в интерфейсе).</summary>
internal static class Labels
{
    public static string Method(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "Наличные",
        PaymentMethod.BankTransfer => "Банковский перевод",
        PaymentMethod.CardPos => "Карта (терминал)",
        PaymentMethod.MBank => "MBANK",
        PaymentMethod.Optima => "Optima",
        _ => m.ToString(),
    };

    public static string Status(BookingStatus s) => s switch
    {
        BookingStatus.Pending => "Ожидает подтверждения",
        BookingStatus.AwaitingPayment => "Ожидает оплаты",
        BookingStatus.Confirmed => "Подтверждена",
        BookingStatus.CheckedIn => "Гость заселён",
        BookingStatus.CheckedOut => "Гость выехал",
        BookingStatus.Cancelled => "Отменена",
        BookingStatus.NoShow => "Неявка",
        BookingStatus.Expired => "Истекла",
        _ => s.ToString(),
    };

    public static string Kind(string kind) => kind == "Hourly" ? "Почасовая" : "Посуточная";

    public static string Source(string source) => source == "Web" ? "Сайт" : "Администратор";

    public static string PaymentType(string type) => type switch
    {
        "Prepayment" => "Предоплата",
        "Stay" => "Проживание",
        "Service" => "Услуги",
        "Penalty" => "Штраф",
        "Other" => "Прочее",
        "Reversal" => "Аннулирование",
        _ => type,
    };

    public static string PaymentStatus(string status) => status switch
    {
        "Paid" => "Оплачен",
        "RefundPending" => "Ожидает возврата",
        "PartiallyRefunded" => "Частично возвращён",
        "Refunded" => "Возвращён",
        "Reversed" => "Аннулирован",
        _ => status,
    };

    public static string RefundStatus(string status) => status switch
    {
        "Requested" => "Ждёт решения",
        "Approved" => "Одобрен",
        "Rejected" => "Отклонён",
        "Paid" => "Выплачен",
        _ => status,
    };

    public static string Role(string role) => role switch
    {
        "Owner" => "Владелец",
        "Administrator" => "Администратор",
        "Housekeeper" => "Уборщик",
        _ => role,
    };
}
