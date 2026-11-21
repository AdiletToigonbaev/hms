using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Platform.Time;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Правила брони из настроек гостиницы (roadmap §6.1, §6.1a): удержание неподтверждённой брони,
/// бесплатная отмена и штрафы, момент неявки. Посуточные и почасовые брони — свои значения.
/// </summary>
internal static class BookingRules
{
    public static PenaltyRules Penalties(HotelSettingsSnapshot s, BookingKind kind) => kind == BookingKind.Hourly
        ? new PenaltyRules(TimeSpan.FromMinutes(s.HourlyPolicy.FreeCancelMinutesBeforeStart), s.HourlyPolicy.LateCancelPenaltyHours, s.HourlyPolicy.NoShowPenaltyHours)
        : new PenaltyRules(TimeSpan.FromHours(s.CancellationPolicy.FreeCancelHoursBeforeCheckIn), s.CancellationPolicy.LateCancelPenaltyNights, s.CancellationPolicy.NoShowPenaltyNights);

    /// <summary>
    /// До какого момента неподтверждённая бронь держит номер: почасовая — PendingHoldMinutes, посуточная — PendingHoldHours,
    /// но не позже начала брони (если начало ещё впереди).
    /// </summary>
    public static DateTimeOffset HoldUntil(HotelSettingsSnapshot s, BookingKind kind, DateTimeOffset startsAt, DateTimeOffset now)
    {
        var hold = now + (kind == BookingKind.Hourly ? TimeSpan.FromMinutes(s.HourlyPolicy.PendingHoldMinutes) : TimeSpan.FromHours(s.PendingHoldHours));
        return startsAt > now && startsAt < hold ? startsAt : hold;
    }

    /// <summary>
    /// Когда гость считается не пришедшим: почасовая — через NoShowAfterMinutes после начала;
    /// посуточная — в NoShowCutoffTime следующего дня после даты заезда.
    /// </summary>
    public static DateTimeOffset NoShowAt(HotelSettingsSnapshot s, IHotelClock clock, BookingKind kind, DateTimeOffset startsAt, DateOnly checkIn) =>
        kind == BookingKind.Hourly
            ? startsAt.AddMinutes(s.HourlyPolicy.NoShowAfterMinutes)
            : clock.ToUtc(checkIn.AddDays(1), s.NoShowCutoffTime);
}
