import type { BedType, RoomStatus } from "@/lib/api/types";

export type DisplayStatus = RoomStatus | "Reserved";

/** Подписи статусов номера. Цвет всегда дублируется текстом (docs/design/design-direction.md). */
export const roomStatusLabel: Record<DisplayStatus, string> = {
  Available: "Свободен",
  Reserved: "Бронь",
  Occupied: "Занят",
  Cleaning: "Уборка",
  Maintenance: "Ремонт",
  OutOfService: "Вне продажи",
};

/** Классы Tailwind для статусной шкалы (токены --status-* в globals.css). */
export const roomStatusTone: Record<DisplayStatus, string> = {
  Available: "bg-status-available/12 text-status-available border-status-available/30",
  Reserved: "bg-status-reserved/12 text-status-reserved border-status-reserved/30",
  Occupied: "bg-status-occupied/12 text-status-occupied border-status-occupied/30",
  Cleaning: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  Maintenance: "bg-status-maintenance/12 text-status-maintenance border-status-maintenance/30",
  OutOfService: "bg-status-out/12 text-status-out border-status-out/30",
};

export const bedTypeLabel: Record<BedType, string> = {
  Single: "Односпальная",
  Double: "Двуспальная",
  Twin: "Две раздельные",
  Queen: "Queen-size",
  King: "King-size",
  Bunk: "Двухъярусная",
};

export const bedTypes = Object.keys(bedTypeLabel) as BedType[];

/** Склонение: 1 гость, 2 гостя, 5 гостей. */
export function plural(n: number, one: string, few: string, many: string): string {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return one;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
  return many;
}

/** Группировка номеров по этажам (без этажа — в конце). */
export function groupByFloor<T extends { floor: number | null }>(rooms: readonly T[]): [number | null, T[]][] {
  const groups = new Map<number | null, T[]>();
  for (const room of rooms) {
    const list = groups.get(room.floor) ?? [];
    list.push(room);
    groups.set(room.floor, list);
  }
  return [...groups.entries()].sort(([a], [b]) => (a === null ? 1 : b === null ? -1 : a - b));
}

/** Подсказка к статусу «Бронь»: «до 14:30» (сейчас по брони) или «в 15:00» (ближайшая сегодня); другой день — с датой. */
export function roomBookingHint(room: { occupiedUntil: string | null; nextBookingAt: string | null }, today: string): string | null {
  const short = (local: string) => {
    const time = local.slice(11, 16);
    if (local.slice(0, 10) === today) return time;
    const [, m, d] = local.slice(0, 10).split("-");
    return `${d}.${m} ${time}`;
  };
  if (room.occupiedUntil) return `по брони до ${short(room.occupiedUntil)}`;
  if (room.nextBookingAt) return `бронь в ${short(room.nextBookingAt)}`;
  return null;
}
