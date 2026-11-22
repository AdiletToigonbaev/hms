import Link from "next/link";
import type { ReactNode } from "react";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

const pad = (n: number) => String(n).padStart(2, "0");

/** Час из адресной строки (1–23). 00:00 в начале и 24:00 в конце — это «сутки целиком», поэтому null. */
export function readHour(value: string | string[] | undefined): number | null {
  const n = Number(typeof value === "string" ? value : NaN);
  return Number.isInteger(n) && n >= 1 && n <= 23 ? n : null;
}

export const periods = [
  { key: "day", label: "Сегодня" },
  { key: "week", label: "Неделя" },
  { key: "month", label: "Месяц" },
  { key: "year", label: "Год" },
] as const;

/**
 * Фильтры в одну строку над графиками: быстрый период (день/неделя/месяц/год) и свой период, категория номера
 * и дополнительные поля (отчёты). Обычная форма GET — ссылку с фильтрами можно сохранить или отправить.
 */
export function PeriodFilter({
  basePath,
  activePeriod,
  from,
  to,
  today,
  roomTypes,
  roomTypeId,
  fromHour = null,
  toHour = null,
  hidden = {},
  keep: extra = {},
  children,
}: {
  basePath: string;
  activePeriod: string | null;
  from: string;
  to: string;
  today: string;
  roomTypes: { id: string; name: string }[];
  roomTypeId: string | null;
  /** Час начала первого дня (0–23) и окончания последнего (1–24); null — сутки целиком. */
  fromHour?: number | null;
  toHour?: number | null;
  /** Скрытые параметры (вид отчёта). */
  hidden?: Record<string, string>;
  /** Фильтры, которые сохраняются при выборе быстрого периода (у них есть свои поля в форме). */
  keep?: Record<string, string>;
  children?: ReactNode;
}) {
  const keep = new URLSearchParams(hidden);
  if (roomTypeId) keep.set("roomTypeId", roomTypeId);
  if (fromHour !== null) keep.set("fromHour", String(fromHour));
  if (toHour !== null) keep.set("toHour", String(toHour));
  for (const [k, v] of Object.entries(extra)) keep.set(k, v);
  return (
    <section aria-label="Период и фильтры" className="grid gap-4 rounded-2xl border bg-card p-4">
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-sm font-medium text-muted-foreground">Быстрый период</span>
        <nav aria-label="Быстрый период" className="inline-flex rounded-xl border bg-secondary/60 p-1">
          {periods.map((p) => {
            const q = new URLSearchParams(keep);
            q.set("period", p.key);
            return (
              <Link
                key={p.key}
                href={`${basePath}?${q}`}
                aria-current={activePeriod === p.key ? "page" : undefined}
                className={cn(
                  "inline-flex min-h-9 items-center rounded-lg px-3 text-sm font-medium transition",
                  activePeriod === p.key ? "bg-card text-primary shadow-sm" : "text-muted-foreground hover:text-foreground",
                )}
              >
                {p.label}
              </Link>
            );
          })}
        </nav>
      </div>
      <form method="get" action={basePath} className="flex flex-wrap items-end gap-3">
        {Object.entries(hidden).map(([k, v]) => (
          <input key={k} type="hidden" name={k} value={v} />
        ))}
        <div className="grid gap-1.5">
          <span className="text-sm font-medium">Период</span>
          <DateRangePicker
            key={`${from}-${to}`}
            mode="period"
            startName="from"
            endName="to"
            defaultStart={from}
            defaultEnd={to}
            today={today}
            startLabel="С"
            endLabel="По"
            size="compact"
          />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="fromHour">С</Label>
          <NativeSelect id="fromHour" name="fromHour" defaultValue={fromHour === null ? "" : String(fromHour)} className="w-28" aria-describedby="hours-hint">
            <option value="">00:00</option>
            {Array.from({ length: 23 }, (_, i) => i + 1).map((h) => (
              <option key={h} value={h}>
                {pad(h)}:00
              </option>
            ))}
          </NativeSelect>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="toHour">До</Label>
          <NativeSelect id="toHour" name="toHour" defaultValue={toHour === null ? "" : String(toHour)} className="w-28" aria-describedby="hours-hint">
            {Array.from({ length: 23 }, (_, i) => i + 1).map((h) => (
              <option key={h} value={h}>
                {pad(h)}:00
              </option>
            ))}
            <option value="">24:00</option>
          </NativeSelect>
          <span id="hours-hint" className="sr-only">
            Час начала первого дня и окончания последнего дня периода
          </span>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="roomTypeId">Категория номера</Label>
          <NativeSelect id="roomTypeId" name="roomTypeId" defaultValue={roomTypeId ?? ""} className="w-48">
            <option value="">Все категории</option>
            {roomTypes.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </NativeSelect>
        </div>
        {children}
        <Button type="submit">
          Показать
        </Button>
      </form>
    </section>
  );
}
