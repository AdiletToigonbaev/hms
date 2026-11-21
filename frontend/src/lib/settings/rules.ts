import type { HotelSettings } from "@/lib/api/types";

/** Значения таблицы «Правила броней» (как в форме). */
export interface BookingRulesValues {
  hourlyMinHours: number;
  hourlyMaxHours: number;
  hourlyFreeCancelMinutes: number;
  hourlyLateCancelPenaltyHours: number;
  hourlyNoShowAfterMinutes: number;
  hourlyNoShowPenaltyHours: number;
  hourlyPendingHoldMinutes: number;
  hourlyRefundUnused: boolean;
  checkInTime: string;
  checkOutTime: string;
  noShowCutoffTime: string;
  freeCancelHoursBeforeCheckIn: number;
  lateCancelPenaltyNights: number;
  noShowPenaltyNights: number;
  pendingHoldHours: number;
  earlyDeparturePenaltyNights: number;
}

export function rulesFromSettings(s: HotelSettings): BookingRulesValues {
  return {
    hourlyMinHours: s.hourlyMinHours,
    hourlyMaxHours: s.hourlyMaxHours,
    hourlyFreeCancelMinutes: s.hourlyPolicy.freeCancelMinutesBeforeStart,
    hourlyLateCancelPenaltyHours: s.hourlyPolicy.lateCancelPenaltyHours,
    hourlyNoShowAfterMinutes: s.hourlyPolicy.noShowAfterMinutes,
    hourlyNoShowPenaltyHours: s.hourlyPolicy.noShowPenaltyHours,
    hourlyPendingHoldMinutes: s.hourlyPolicy.pendingHoldMinutes,
    hourlyRefundUnused: s.hourlyPolicy.refundUnusedOnEarlyDeparture,
    checkInTime: s.checkInTime,
    checkOutTime: s.checkOutTime,
    noShowCutoffTime: s.noShowCutoffTime,
    freeCancelHoursBeforeCheckIn: s.cancellationPolicy.freeCancelHoursBeforeCheckIn,
    lateCancelPenaltyNights: s.cancellationPolicy.lateCancelPenaltyNights,
    noShowPenaltyNights: s.cancellationPolicy.noShowPenaltyNights,
    pendingHoldHours: s.pendingHoldHours,
    earlyDeparturePenaltyNights: s.cancellationPolicy.earlyDeparturePenaltyNights,
  };
}

/** Тело PUT /hotel/settings из текущих настроек — разделы меняют только свои поля поверх него. */
export function settingsUpdateBody(s: HotelSettings) {
  return {
    checkInTime: s.checkInTime,
    checkOutTime: s.checkOutTime,
    prepaymentPercent: s.prepaymentPercent,
    paymentHoldMinutes: s.paymentHoldMinutes,
    pendingHoldHours: s.pendingHoldHours,
    autoConfirmWebBookings: s.autoConfirmWebBookings,
    autoReleaseRoomAfterCleaning: s.autoReleaseRoomAfterCleaning,
    requireEmailConfirmation: s.requireEmailConfirmation,
    noShowCutoffTime: s.noShowCutoffTime,
    hourlyMinHours: s.hourlyMinHours,
    hourlyMaxHours: s.hourlyMaxHours,
    cleaningBufferMinutes: s.cleaningBufferMinutes,
    cancellationPolicy: { ...s.cancellationPolicy },
    rules: s.rules,
    version: s.version,
    hourlyPolicy: { ...s.hourlyPolicy },
    autoNoShowEnabled: s.autoNoShowEnabled,
    cleaningBufferNightlyMinutes: s.cleaningBufferNightlyMinutes,
  };
}

type Translate = (key: string, values?: Record<string, string | number>) => string;

/**
 * Текущие правила обычными словами (пересобирается при каждом изменении формы).
 * t — переводчик пространства SettingsSummary.
 */
export function describeRules(v: BookingRulesValues, t: Translate): { hourly: string[]; nightly: string[] } {
  const minutes = (n: number) => (n > 0 && n % 60 === 0 ? t("hours", { n: n / 60 }) : t("minutes", { n }));
  const hours = (n: number) => t("hours", { n });
  const nights = (n: number) => t("nights", { n });
  /** «штраф 1 час» или «без штрафа». */
  const penalty = (n: number, unit: (n: number) => string) => (n > 0 ? t("penalty", { value: unit(n) }) : t("noPenalty"));

  const hourly = [
    t("hourlyDuration", { min: t("hoursGen", { n: v.hourlyMinHours }), max: t("hoursGen", { n: v.hourlyMaxHours }) }),
    v.hourlyLateCancelPenaltyHours > 0
      ? t("hourlyCancel", { free: minutes(v.hourlyFreeCancelMinutes), penalty: hours(v.hourlyLateCancelPenaltyHours) })
      : t("hourlyCancelNoPenalty"),
    t("hourlyNoShow", { after: minutes(v.hourlyNoShowAfterMinutes), penalty: penalty(v.hourlyNoShowPenaltyHours, hours) }),
    t("hourlyHold", { hold: minutes(v.hourlyPendingHoldMinutes) }),
    v.hourlyRefundUnused ? t("hourlyEarlyRefund") : t("hourlyEarlyNoRefund"),
  ];
  const nightly = [
    t("nightlyTimes", { in: v.checkInTime, out: v.checkOutTime }),
    v.lateCancelPenaltyNights > 0
      ? t("nightlyCancel", { free: t("hours", { n: v.freeCancelHoursBeforeCheckIn }), penalty: nights(v.lateCancelPenaltyNights) })
      : t("nightlyCancelNoPenalty"),
    t("nightlyNoShow", { cutoff: v.noShowCutoffTime, penalty: penalty(v.noShowPenaltyNights, nights) }),
    t("nightlyHold", { hold: t("hours", { n: v.pendingHoldHours }) }),
    v.earlyDeparturePenaltyNights > 0 ? t("nightlyEarlyPenalty", { penalty: nights(v.earlyDeparturePenaltyNights) }) : t("nightlyEarlyNoPenalty"),
  ];
  return { hourly, nightly };
}
