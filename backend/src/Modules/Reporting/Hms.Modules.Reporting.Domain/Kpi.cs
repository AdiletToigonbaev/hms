namespace Hms.Modules.Reporting.Domain;

/// <summary>Статус брони глазами отчётов (без зависимости от модуля броней).</summary>
public enum StayStatus
{
    Pending,
    Confirmed,
    CheckedIn,
    CheckedOut,
    Cancelled,
    NoShow,
    Expired,
}

/// <summary>
/// Бронь для расчёта: время UTC [StartsAt, EndsAt), посуточные — даты заезда/выезда гостиницы.
/// <see cref="Value"/> — стоимость брони (проживание − скидка + услуги + продления − перерасчёт),
/// <see cref="Accommodation"/> — только проживание (без услуг) для ADR и цены часа.
/// </summary>
public sealed record StayInput(
    bool Hourly,
    StayStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly CheckIn,
    DateOnly CheckOut,
    decimal Value,
    decimal Accommodation,
    decimal Penalty,
    DateTimeOffset? PenaltyAt);

/// <summary>День периода: дата гостиницы и её границы в UTC.</summary>
public sealed record PeriodDay(DateOnly Date, DateTimeOffset Start, DateTimeOffset End);

public sealed record MoneyInput(DateTimeOffset At, decimal Amount);

public sealed record ExpenseInput(DateOnly Date, decimal Amount);

/// <summary>Показатели за период (docs/database/database-design.md §8).</summary>
/// <param name="AccruedRevenue">Начисленная выручка (Total Revenue): стоимость проживания и услуг, приходящаяся на период, + штрафы.</param>
/// <param name="CashIn">Поступления: платежи − аннулирования с датой оплаты в периоде.</param>
/// <param name="RefundsPaid">Выплаченные возвраты в периоде.</param>
/// <param name="NetRevenue">Чистые поступления = поступления − возвраты.</param>
/// <param name="NetProfit">Прибыль по поступлениям = чистые поступления − расходы.</param>
/// <param name="AccruedProfit">Прибыль по начислению = начисленная выручка − расходы.</param>
/// <param name="Occupancy">Загрузка по времени: занятые номеро-часы / (номера × часы периода), 0..1.</param>
/// <param name="RoomNightsSold">Проданные номеро-ночи (посуточные брони).</param>
/// <param name="Adr">Средняя цена ночи (посуточные): выручка за проживание / номеро-ночи. Null — посуточных не было.</param>
/// <param name="RevPar">Выручка посуточного проживания на доступный номер в сутки.</param>
/// <param name="HoursSold">Проданные номеро-часы (почасовые брони).</param>
/// <param name="AvgHourPrice">Средняя цена часа (почасовые): выручка за проживание / часы. Null — почасовых не было.</param>
/// <param name="RevenuePerRoomHour">Выручка за проживание (все брони) на доступный номер в час.</param>
/// <param name="Turnover">Оборот номера: брони с началом в периоде / (номера × дни).</param>
public sealed record KpiResult(
    decimal AccruedRevenue,
    decimal CashIn,
    decimal RefundsPaid,
    decimal NetRevenue,
    decimal Expenses,
    decimal NetProfit,
    decimal AccruedProfit,
    decimal Occupancy,
    decimal RoomNightsSold,
    decimal? Adr,
    decimal RevPar,
    decimal HoursSold,
    decimal? AvgHourPrice,
    decimal RevenuePerRoomHour,
    int TotalBookings,
    int CancelledBookings,
    int NoShowBookings,
    decimal CancellationRate,
    decimal? AvgStayNights,
    decimal? AvgStayHours,
    decimal Turnover);

public sealed record DayPoint(DateOnly Date, decimal AccruedRevenue, decimal CashIn, decimal Expenses, decimal Occupancy, int Arrivals);

/// <summary>
/// Расчёт показателей по фактическим данным (ТЗ §12: не демонстрационные значения).
/// Выручка брони распределяется по времени: на период приходится доля, пропорциональная пересечению проживания с периодом
/// (почасовые — по часам, посуточные — по ночам). Учитываются подтверждённые, заселённые и завершённые брони;
/// отменённые и неявки — только начисленным штрафом (на дату отмены/неявки). Неподтверждённые и истёкшие не учитываются.
/// </summary>
public static class Kpi
{
    private static readonly StayStatus[] Sold = [StayStatus.Confirmed, StayStatus.CheckedIn, StayStatus.CheckedOut];

    public static bool IsSold(StayStatus status) => Sold.Contains(status);

    /// <summary>Доля брони, приходящаяся на интервал [from, to): по ночам для посуточной, по времени — для почасовой.</summary>
    public static decimal Share(StayInput s, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<PeriodDay> days)
    {
        if (!s.Hourly)
        {
            var total = s.CheckOut.DayNumber - s.CheckIn.DayNumber;
            if (total <= 0)
            {
                return 0m;
            }

            var inside = days.Count(d => d.Start >= from && d.End <= to && d.Date >= s.CheckIn && d.Date < s.CheckOut);
            return (decimal)inside / total;
        }

        var length = (s.EndsAt - s.StartsAt).TotalSeconds;
        if (length <= 0)
        {
            return 0m;
        }

        var overlap = Overlap(s.StartsAt, s.EndsAt, from, to).TotalSeconds;
        return (decimal)(overlap / length);
    }

    public static TimeSpan Overlap(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? end - start : TimeSpan.Zero;
    }

    /// <summary>Начислено за интервал: доля стоимости проданных броней + штрафы, начисленные в интервале.</summary>
    public static decimal Accrued(IEnumerable<StayInput> stays, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<PeriodDay> days) =>
        stays.Sum(s => IsSold(s.Status)
            ? s.Value * Share(s, from, to, days)
            : s.Penalty > 0 && s.PenaltyAt is { } at && at >= from && at < to ? s.Penalty : 0m);

    public static KpiResult Calculate(
        IReadOnlyList<PeriodDay> days,
        int availableRooms,
        IReadOnlyList<StayInput> stays,
        IReadOnlyList<MoneyInput> payments,
        IReadOnlyList<MoneyInput> refundsPaid,
        IReadOnlyList<ExpenseInput> expenses)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            throw new ArgumentException("Период пуст.", nameof(days));
        }

        var from = days[0].Start;
        var to = days[^1].End;
        var periodHours = (decimal)(to - from).TotalHours;
        var rooms = Math.Max(availableRooms, 0);

        var accrued = Round(Accrued(stays, from, to, days));
        var cashIn = payments.Where(p => p.At >= from && p.At < to).Sum(p => p.Amount);
        var refunds = refundsPaid.Where(r => r.At >= from && r.At < to).Sum(r => r.Amount);
        var expenseSum = expenses.Where(e => e.Date >= days[0].Date && e.Date <= days[^1].Date).Sum(e => e.Amount);

        var sold = stays.Where(s => IsSold(s.Status)).ToList();
        var occupiedHours = sold.Sum(s => (decimal)Overlap(s.StartsAt, s.EndsAt, from, to).TotalHours);
        var occupancy = rooms == 0 || periodHours == 0 ? 0m : occupiedHours / (rooms * periodHours);

        var nightly = sold.Where(s => !s.Hourly).ToList();
        var roomNights = nightly.Sum(s => (decimal)days.Count(d => d.Date >= s.CheckIn && d.Date < s.CheckOut));
        var nightlyRevenue = nightly.Sum(s => s.Accommodation * Share(s, from, to, days));
        var hourly = sold.Where(s => s.Hourly).ToList();
        var hoursSold = hourly.Sum(s => (decimal)Overlap(s.StartsAt, s.EndsAt, from, to).TotalHours);
        var hourlyRevenue = hourly.Sum(s => s.Accommodation * Share(s, from, to, days));

        // Брони «за период» — с началом проживания в периоде (истёкшие не в счёт: брони не было).
        var started = stays.Where(s => s.StartsAt >= from && s.StartsAt < to && s.Status != StayStatus.Expired).ToList();
        var cancelled = started.Count(s => s.Status == StayStatus.Cancelled);
        var noShow = started.Count(s => s.Status == StayStatus.NoShow);
        var completed = stays.Where(s => s.Status == StayStatus.CheckedOut && s.EndsAt >= from && s.EndsAt < to).ToList();
        var completedNightly = completed.Where(s => !s.Hourly).ToList();
        var completedHourly = completed.Where(s => s.Hourly).ToList();

        return new KpiResult(
            AccruedRevenue: accrued,
            CashIn: cashIn,
            RefundsPaid: refunds,
            NetRevenue: cashIn - refunds,
            Expenses: expenseSum,
            NetProfit: cashIn - refunds - expenseSum,
            AccruedProfit: accrued - expenseSum,
            Occupancy: Round(occupancy, 4),
            RoomNightsSold: roomNights,
            Adr: roomNights > 0 ? Round(nightlyRevenue / roomNights) : null,
            RevPar: rooms == 0 ? 0m : Round(nightlyRevenue / (rooms * days.Count)),
            HoursSold: Round(hoursSold),
            AvgHourPrice: hoursSold > 0 ? Round(hourlyRevenue / hoursSold) : null,
            RevenuePerRoomHour: rooms == 0 || periodHours == 0 ? 0m : Round((nightlyRevenue + hourlyRevenue) / (rooms * periodHours)),
            TotalBookings: started.Count,
            CancelledBookings: cancelled,
            NoShowBookings: noShow,
            CancellationRate: started.Count == 0 ? 0m : Round((decimal)cancelled / started.Count, 4),
            AvgStayNights: completedNightly.Count == 0 ? null : Round((decimal)completedNightly.Average(s => s.CheckOut.DayNumber - s.CheckIn.DayNumber), 1),
            AvgStayHours: completedHourly.Count == 0 ? null : Round((decimal)completedHourly.Average(s => (s.EndsAt - s.StartsAt).TotalHours), 1),
            Turnover: rooms == 0 ? 0m : Round((decimal)started.Count(s => s.Status != StayStatus.Cancelled) / (rooms * days.Count), 2));
    }

    /// <summary>Показатели по дням периода (графики): начислено, поступило, расходы, загрузка, заезды.</summary>
    public static IReadOnlyList<DayPoint> Daily(
        IReadOnlyList<PeriodDay> days, int availableRooms, IReadOnlyList<StayInput> stays, IReadOnlyList<MoneyInput> payments, IReadOnlyList<ExpenseInput> expenses)
    {
        var sold = stays.Where(s => IsSold(s.Status)).ToList();
        return days.Select(d =>
        {
            var hours = (decimal)(d.End - d.Start).TotalHours;
            var occupied = sold.Sum(s => (decimal)Overlap(s.StartsAt, s.EndsAt, d.Start, d.End).TotalHours);
            return new DayPoint(
                d.Date,
                Round(Accrued(stays, d.Start, d.End, days)),
                payments.Where(p => p.At >= d.Start && p.At < d.End).Sum(p => p.Amount),
                expenses.Where(e => e.Date == d.Date).Sum(e => e.Amount),
                availableRooms == 0 || hours == 0 ? 0m : Round(occupied / (availableRooms * hours), 4),
                stays.Count(s => s.StartsAt >= d.Start && s.StartsAt < d.End && s.Status is not (StayStatus.Expired or StayStatus.Cancelled)));
        }).ToList();
    }

    private static decimal Round(decimal value, int digits = 2) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
