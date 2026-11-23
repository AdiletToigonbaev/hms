"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { Label } from "@/components/ui/label";

const pad = (n: number) => String(n).padStart(2, "0");

/** Сегодня — только часы после текущего; другой день — любые. */
export function firstStartHour(date: string, today: string, nowHour: number): number {
  return date === today ? Math.min(nowHour + 1, 23) : 0;
}

/**
 * Дата и час начала почасовой брони на сайте. Доступные часы пересчитываются сразу при выборе даты
 * (раньше — только после отправки формы, и «завтра» оставляло сегодняшние прошедшие часы недоступными).
 */
export function HourlyStartFields({
  defaultDate,
  defaultHour,
  today,
  nowHour,
  className,
}: {
  defaultDate: string;
  defaultHour: number;
  today: string;
  nowHour: number;
  className: string;
}) {
  const t = useTranslations("Site");
  const [date, setDate] = useState(defaultDate);
  const [hour, setHour] = useState(defaultHour);
  const first = firstStartHour(date, today, nowHour);

  return (
    <>
      <div className="grid gap-1.5">
        <span className="text-sm font-medium">{t("date")}</span>
        <DateRangePicker
          mode="single"
          startName="date"
          defaultStart={defaultDate}
          today={today}
          min={today}
          startLabel={t("date")}
          onChange={(range) => {
            if (!range.start) return;
            setDate(range.start);
            // Вернулись на сегодня с уже прошедшим часом — ближайший доступный.
            setHour((h) => Math.max(h, firstStartHour(range.start!, today, nowHour)));
          }}
        />
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="startHour">{t("startTime")}</Label>
        <NativeSelect id="startHour" name="startHour" value={String(hour)} onChange={(e) => setHour(Number(e.target.value))} className={className}>
          {Array.from({ length: 24 }, (_, h) => h).map((h) => (
            <option key={h} value={h} disabled={h < first}>
              {pad(h)}:00
            </option>
          ))}
        </NativeSelect>
      </div>
    </>
  );
}
