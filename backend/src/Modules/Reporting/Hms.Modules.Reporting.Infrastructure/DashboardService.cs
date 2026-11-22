using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Reporting.Application;
using Hms.Modules.Reporting.Domain;
using Hms.Modules.Rooms.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Reporting.Infrastructure;

/// <summary>Дашборды: владельца (показатели за период) и администратора (операционный, на день).</summary>
internal sealed class DashboardService(
    ReportData data,
    IBookingFacts bookingFacts,
    IBookingCharges charges,
    IFinanceFacts finance,
    IRoomDirectory rooms,
    IGuestDirectory guests,
    IHousekeepingFacts housekeeping) : IDashboardService
{
    private static readonly BookingStatus[] OwedStatuses =
        [BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.CheckedOut, BookingStatus.NoShow, BookingStatus.Cancelled];

    public async Task<Result<OwnerDashboardDto>> OwnerAsync(DashboardQuery q, CancellationToken ct)
    {
        var range = data.Resolve(q.From, q.To, q.Period, q.Date);
        if (range.IsFailure)
        {
            return range.Error;
        }

        if (!ReportData.HoursValid(range.Value.From, range.Value.To, q.FromHour, q.ToHour))
        {
            return ReportErrors.HoursInvalid;
        }

        var d = await data.LoadAsync(range.Value.From, range.Value.To, q.RoomTypeId, null, ct, q.FromHour, q.ToHour);
        var kpi = data.Kpi(d);
        var started = d.Bookings.Where(b => b.StartsAt >= d.Start && b.StartsAt < d.End && b.Status != BookingStatus.Expired).ToList();
        var typeNames = d.Rooms.GroupBy(r => r.RoomTypeId).ToDictionary(g => g.Key, g => g.First().RoomTypeName);

        // Долги на сейчас: начатые брони, по которым оплачено меньше начисленного (как в «Оплаты → Долги»).
        var now = data.UtcNow;
        var owed = (await charges.ListWithDueAsync(ct)).Where(c => OwedStatuses.Contains(c.Status) && c.StartsAt <= now).ToList();
        var received = owed.Count == 0 ? new Dictionary<Guid, decimal>() : await finance.NetReceivedAsync(owed.Select(c => c.BookingId).ToList(), ct);
        var debts = owed.Select(c => c.Due - received.GetValueOrDefault(c.BookingId)).Where(x => x > 0).ToList();

        // Возвраты, ждущие решения или выплаты (за последний год — шире не бывает).
        var pending = (await finance.RefundsAsync(now.AddYears(-1), now.AddDays(1), ct)).Where(r => r.Status is "Requested" or "Approved").ToList();

        return new OwnerDashboardDto(
            d.From, d.To, d.AvailableRooms, kpi,
            debts.Sum(), debts.Count,
            pending.Sum(r => r.Amount), pending.Count,
            data.Daily(d),
            d.Payments.GroupBy(p => p.Method.ToString()).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount)),
            started.GroupBy(b => b.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            d.Bookings.GroupBy(b => b.RoomTypeId)
                .Select(g => new NamedAmount(typeNames.GetValueOrDefault(g.Key) ?? "—", Kpi.Accrued(g.Select(data.ToStay), d.Start, d.End, d.Days)))
                .Where(x => x.Amount != 0)
                .Select(x => x with { Amount = Math.Round(x.Amount, 2) })
                .OrderByDescending(x => x.Amount).ToList(),
            d.Expenses.GroupBy(e => e.CategoryName).Select(g => new NamedAmount(g.Key, g.Sum(e => e.Amount))).OrderByDescending(x => x.Amount).ToList(),
            started.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count()),
            d.FromHour,
            d.ToHour);
    }

    public async Task<OperationsDashboardDto> OperationsAsync(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? data.Today;
        var days = data.Days(day, day);
        var (start, end) = (days[0].Start, days[0].End);
        var facts = await bookingFacts.ListAsync(start, end, ct);
        var allRooms = await rooms.ListActiveAsync(ct);
        var roomNumbers = allRooms.ToDictionary(r => r.Id, r => r.Number);
        var names = await guests.GetManyAsync(facts.Select(f => f.GuestId).OfType<Guid>().Distinct().ToList(), ct);

        TodayBookingDto Row(BookingFact f) => new(f.BookingId, f.Number, f.Kind, f.Status.ToString(), data.Format(f.StartsAt), data.Format(f.EndsAt),
            roomNumbers.GetValueOrDefault(f.RoomId) ?? "—", f.GuestId is { } g ? names.GetValueOrDefault(g)?.FullName : null);

        var arrivals = facts
            .Where(f => f.StartsAt >= start && f.StartsAt < end && f.Status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed or BookingStatus.CheckedIn)
            .OrderBy(f => f.StartsAt).Select(Row).ToList();
        var departures = facts
            .Where(f => f.EndsAt > start && f.EndsAt <= end && f.Status is BookingStatus.CheckedIn or BookingStatus.CheckedOut)
            .OrderBy(f => f.EndsAt).Select(Row).ToList();
        var occupancy = Kpi.Calculate(days, allRooms.Count(r => r.Status != RoomStatus.OutOfService), facts.Select(data.ToStay).ToList(), [], [], []).Occupancy;

        return new OperationsDashboardDto(
            day,
            arrivals,
            departures,
            facts.Count(f => f.Status == BookingStatus.CheckedIn),
            await bookingFacts.PendingCountAsync(data.UtcNow, ct),
            allRooms.GroupBy(r => r.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            await housekeeping.OpenCountAsync(ct),
            occupancy,
            facts.Count(f => f.CreatedAt >= start && f.CreatedAt < end));
    }
}
