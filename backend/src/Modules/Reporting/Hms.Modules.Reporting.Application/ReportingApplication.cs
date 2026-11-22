using FluentValidation;
using Hms.Modules.Reporting.Domain;
using Hms.SharedKernel;

namespace Hms.Modules.Reporting.Application;

/// <summary>
/// Период и фильтры отчёта (ТЗ §12). Период — либо <see cref="From"/>..<see cref="To"/> (даты гостиницы включительно),
/// либо <see cref="Period"/> day|week|month|year вокруг даты <see cref="Date"/> (по умолчанию — сегодня).
/// Без периода — с начала текущего месяца по сегодня.
/// </summary>
public sealed record ReportQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Period = null,
    DateOnly? Date = null,
    Guid? RoomTypeId = null,
    string? PaymentMethod = null,
    string? BookingStatus = null,
    Guid? StaffId = null,
    int? FromHour = null,
    int? ToHour = null);

/// <summary>Колонка отчёта. Type: text | money | int | number | percent | date | datetime.</summary>
public sealed record ReportColumn(string Key, string Title, string Type);

/// <summary>Показатель в шапке отчёта (итог за период). Hint — что это и как считается, Example — пример с числами (подсказка «?»).</summary>
public sealed record ReportSummaryItem(string Label, decimal? Value, string Type, string? Hint = null, string? Example = null);

/// <summary>
/// Отчёт как таблица: одинаково показывается на сайте и выгружается в Excel/PDF.
/// Значения ячеек: строки, числа (деньги — decimal) или null; даты — строки «yyyy-MM-dd», время — «yyyy-MM-ddTHH:mm» (гостиница).
/// </summary>
public sealed record ReportTable(
    string Type,
    string Title,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Filters,
    IReadOnlyList<ReportSummaryItem> Summary,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals,
    string? Note = null,
    int? FromHour = null,
    int? ToHour = null);

/// <summary>Показатели дашборда владельца с подписями основы расчёта (database-design §8).</summary>
public sealed record OwnerDashboardDto(
    DateOnly From,
    DateOnly To,
    int AvailableRooms,
    KpiResult Kpi,
    decimal Outstanding,
    int OutstandingBookings,
    decimal RefundsRequested,
    int RefundsRequestedCount,
    IReadOnlyList<DayPoint> Daily,
    IReadOnlyDictionary<string, decimal> CashByMethod,
    IReadOnlyDictionary<string, int> BookingsByStatus,
    IReadOnlyList<NamedAmount> RevenueByRoomType,
    IReadOnlyList<NamedAmount> ExpensesByCategory,
    IReadOnlyDictionary<string, int> BookingsByKind,
    int? FromHour = null,
    int? ToHour = null);

public sealed record NamedAmount(string Name, decimal Amount);

/// <summary>Строка списка «сегодня» на операционном дашборде. Время — гостиницы.</summary>
public sealed record TodayBookingDto(Guid BookingId, string Number, string Kind, string Status, string StartsAt, string EndsAt, string RoomNumber, string? GuestName);

/// <summary>Операционный дашборд администратора на дату: заезды, выезды, проживают, номера, уборка, ожидают подтверждения.</summary>
public sealed record OperationsDashboardDto(
    DateOnly Date,
    IReadOnlyList<TodayBookingDto> Arrivals,
    IReadOnlyList<TodayBookingDto> Departures,
    int InHouse,
    int PendingConfirmations,
    IReadOnlyDictionary<string, int> RoomsByStatus,
    int OpenCleaningTasks,
    decimal OccupancyToday,
    int BookingsCreatedToday);

public sealed record DashboardQuery(DateOnly? From, DateOnly? To, string? Period, DateOnly? Date, Guid? RoomTypeId, int? FromHour = null, int? ToHour = null);

public interface IDashboardService
{
    Task<Result<OwnerDashboardDto>> OwnerAsync(DashboardQuery query, CancellationToken ct);

    Task<OperationsDashboardDto> OperationsAsync(DateOnly? date, CancellationToken ct);
}

public interface IReportService
{
    Task<Result<ReportTable>> BuildAsync(string type, ReportQuery query, CancellationToken ct);
}

/// <summary>Выгрузка отчёта-таблицы в файл.</summary>
public interface IReportExporter
{
    byte[] ToExcel(ReportTable report, string hotelName);

    byte[] ToPdf(ReportTable report, string hotelName);
}

public static class ReportErrors
{
    public static readonly Error UnknownType = Error.NotFound("report.unknown_type", "Такого отчёта нет.");
    public static readonly Error UnknownFormat = Error.Validation("report.unknown_format", "Формат выгрузки: xlsx или pdf.");
    public static readonly Error PeriodInvalid = Error.Validation("report.period_invalid", "Дата начала позже даты окончания.");
    public static readonly Error PeriodTooLong = Error.Validation("report.period_too_long", "Период отчёта — не больше 366 дней.");
    public static readonly Error HoursInvalid = Error.Validation("report.hours_invalid", "Время: начало — с 00:00 до 23:00, окончание — с 01:00 до 24:00; в один день окончание позже начала.");
}

public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    public ReportQueryValidator()
    {
        RuleFor(x => x.Period).Must(p => p is null or "day" or "week" or "month" or "year").WithMessage("Период: day, week, month или year.");
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To).WithMessage("Дата начала позже даты окончания.");
    }
}
