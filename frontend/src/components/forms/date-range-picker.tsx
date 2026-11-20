"use client";

import { Popover } from "@base-ui/react/popover";
import { ArrowRight, CalendarDays, ChevronLeft, ChevronRight, X } from "lucide-react";
import { useTranslations } from "next-intl";
import { useRef, useState, type KeyboardEvent } from "react";
import { Button } from "@/components/ui/button";
import { nightsLabel } from "@/lib/bookings/status";
import {
  dayRole,
  daysBetween,
  formatDayFull,
  formatDayShort,
  formatMonthTitle,
  formatWeekdayShort,
  monthGrid,
  monthStart,
  nextWeekend,
  pickDay,
  shiftDays,
  weekdayHeaders,
  type IsoDate,
  type Range,
} from "@/lib/calendar";
import { cn } from "@/lib/utils";

interface DateRangePickerProps {
  /** Имена полей формы (значения — YYYY-MM-DD в скрытых input). */
  startName: string;
  endName: string;
  defaultStart?: IsoDate | null;
  defaultEnd?: IsoDate | null;
  /** Сегодня по часовому поясу гостиницы (подсветка и быстрые кнопки). */
  today: IsoDate;
  /** Раньше этой даты выбрать нельзя. */
  min?: IsoDate;
  /** `stay` — проживание: выезд позже заезда, ночи; `period` — фильтр: можно один день и пустое значение. */
  mode: "stay" | "period";
  startLabel: string;
  endLabel: string;
  maxNights?: number;
  size?: "default" | "compact";
  className?: string;
}

/**
 * Выбор периода в стиле приложения: поле «начало → конец», по клику — календарь на два месяца
 * (на телефоне — один). Клавиатура: стрелки — день/неделя, PageUp/PageDown — месяц, Enter — выбрать.
 */
export function DateRangePicker({
  startName,
  endName,
  defaultStart = null,
  defaultEnd = null,
  today,
  min,
  mode,
  startLabel,
  endLabel,
  maxNights,
  size = "default",
  className,
}: DateRangePickerProps) {
  const t = useTranslations("DatePicker");
  const allowSameDay = mode === "period";
  const [range, setRange] = useState<Range>({ start: defaultStart, end: defaultEnd });
  const [open, setOpen] = useState(false);
  const [hover, setHover] = useState<IsoDate | null>(null);
  const [view, setView] = useState<IsoDate>(monthStart(defaultStart ?? today));
  const [focus, setFocus] = useState<IsoDate>(defaultStart ?? today);
  const gridRef = useRef<HTMLDivElement>(null);

  const minMonth = min ? monthStart(min) : null;
  const nights = range.start && range.end ? daysBetween(range.start, range.end) : null;

  const disabled = (day: IsoDate) => {
    if (min && day < min) return true;
    // Пока выбирается выезд — не дальше максимума ночей.
    if (maxNights && range.start && !range.end && day > shiftDays(range.start, maxNights)) return true;
    return false;
  };

  function handleOpenChange(next: boolean) {
    if (next) {
      const anchor = range.start ?? (min && min > today ? min : today);
      setView(monthStart(anchor));
      setFocus(anchor);
    } else if (mode === "stay" && range.start && !range.end) {
      // Закрыли, выбрав только заезд — по умолчанию одна ночь.
      setRange({ start: range.start, end: shiftDays(range.start, 1) });
    }
    setHover(null);
    setOpen(next);
  }

  function choose(day: IsoDate) {
    if (disabled(day)) return;
    const next = pickDay(range, day, allowSameDay);
    setRange(next);
    setFocus(day);
  }

  function apply(next: Range) {
    setRange(next);
    if (next.start) {
      setView(monthStart(next.start));
      setFocus(next.start);
    }
  }

  function moveFocus(day: IsoDate) {
    setFocus(day);
    // Фокус ушёл за пределы видимых месяцев — листаем.
    if (day < view) setView(monthStart(day));
    else if (day >= monthStart(view, 2)) setView(monthStart(day, -1));
    requestAnimationFrame(() => gridRef.current?.querySelector<HTMLButtonElement>(`[data-day="${day}"]`)?.focus());
  }

  function onGridKey(e: KeyboardEvent) {
    const delta: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    if (e.key in delta) {
      e.preventDefault();
      moveFocus(shiftDays(focus, delta[e.key]));
    } else if (e.key === "PageUp" || e.key === "PageDown") {
      e.preventDefault();
      const d = new Date(`${focus}T00:00:00Z`);
      d.setUTCMonth(d.getUTCMonth() + (e.key === "PageUp" ? -1 : 1));
      moveFocus(d.toISOString().slice(0, 10));
    }
  }

  const quick: { label: string; range: Range }[] =
    mode === "stay"
      ? (() => {
          const base = range.start ?? (min && min > today ? min : today);
          return [
            { label: t("oneNight"), range: { start: base, end: shiftDays(base, 1) } },
            { label: t("weekend"), range: nextWeekend(min && min > today ? min : today) },
            { label: t("week"), range: { start: base, end: shiftDays(base, 7) } },
          ];
        })()
      : [
          { label: t("today"), range: { start: today, end: today } },
          { label: t("next7"), range: { start: today, end: shiftDays(today, 6) } },
          { label: t("next30"), range: { start: today, end: shiftDays(today, 29) } },
        ];

  const display = (day: IsoDate | null) => (day ? `${formatDayShort(day)}, ${formatWeekdayShort(day)}` : null);
  const compact = size === "compact";

  return (
    <div className={cn("relative", className)}>
      <input type="hidden" name={startName} value={range.start ?? ""} />
      <input type="hidden" name={endName} value={range.end ?? ""} />
      <Popover.Root open={open} onOpenChange={handleOpenChange}>
        <Popover.Trigger
          aria-label={`${startLabel} — ${endLabel}: ${range.start ? formatDayFull(range.start) : t("notSet")} — ${range.end ? formatDayFull(range.end) : t("notSet")}`}
          className={cn(
            "group flex w-full items-center gap-2.5 rounded-xl border border-input bg-card px-3 text-left text-sm shadow-xs transition outline-none",
            "hover:border-ring/60 focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 data-[popup-open]:border-ring data-[popup-open]:ring-3 data-[popup-open]:ring-ring/30",
            compact ? "h-9" : "min-h-14 py-2",
          )}
        >
          <CalendarDays className="size-4 shrink-0 text-primary" aria-hidden />
          {compact ? (
            <span className={cn("truncate", !range.start && "text-muted-foreground")}>
              {range.start
                ? range.end && range.end !== range.start
                  ? `${formatDayShort(range.start)} — ${formatDayShort(range.end)}`
                  : formatDayShort(range.start)
                : t("anyDates")}
            </span>
          ) : (
            <>
              <span className="grid min-w-0">
                <span className="text-[11px] font-medium tracking-wide text-muted-foreground uppercase">{startLabel}</span>
                <span className={cn("truncate font-medium", !range.start && "font-normal text-muted-foreground")}>{display(range.start) ?? t("choose")}</span>
              </span>
              <ArrowRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
              <span className="grid min-w-0">
                <span className="text-[11px] font-medium tracking-wide text-muted-foreground uppercase">{endLabel}</span>
                <span className={cn("truncate font-medium", !range.end && "font-normal text-muted-foreground")}>{display(range.end) ?? t("choose")}</span>
              </span>
              {mode === "stay" && nights !== null && (
                <span className="tabular ml-auto shrink-0 rounded-full bg-secondary px-2.5 py-0.5 text-xs font-medium text-secondary-foreground">
                  {nightsLabel(nights)}
                </span>
              )}
            </>
          )}
        </Popover.Trigger>
        {compact && range.start && (
          <button
            type="button"
            onClick={() => setRange({ start: null, end: null })}
            className="absolute top-1/2 right-2 grid size-6 -translate-y-1/2 place-items-center rounded-full text-muted-foreground hover:bg-accent hover:text-foreground"
            aria-label={t("clear")}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        )}
        <Popover.Portal>
          <Popover.Positioner sideOffset={8} align="start" className="z-50">
            <Popover.Popup
              className={cn(
                "grid max-w-[calc(100vw-1rem)] gap-4 rounded-2xl border bg-card p-4 shadow-xl outline-none",
                "origin-[var(--transform-origin)] transition-[transform,opacity] duration-150 data-[ending-style]:scale-95 data-[ending-style]:opacity-0 data-[starting-style]:scale-95 data-[starting-style]:opacity-0",
              )}
            >
              <Popover.Title className="sr-only">
                {startLabel} — {endLabel}
              </Popover.Title>

              <div className="flex flex-wrap gap-1.5">
                {quick.map((q) => (
                  <button
                    key={q.label}
                    type="button"
                    onClick={() => apply(q.range)}
                    className="rounded-full border border-border bg-secondary/60 px-3 py-1 text-xs font-medium text-secondary-foreground transition hover:border-primary/40 hover:bg-primary/10 hover:text-primary"
                  >
                    {q.label}
                  </button>
                ))}
              </div>

              <div className="relative" ref={gridRef} onKeyDown={onGridKey}>
                <div className="absolute top-0 right-0 left-0 flex justify-between">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8 rounded-full"
                    disabled={minMonth !== null && view <= minMonth}
                    onClick={() => setView(monthStart(view, -1))}
                    aria-label={t("prevMonth")}
                  >
                    <ChevronLeft aria-hidden />
                  </Button>
                  <Button type="button" variant="ghost" size="icon" className="size-8 rounded-full" onClick={() => setView(monthStart(view, 1))} aria-label={t("nextMonth")}>
                    <ChevronRight aria-hidden />
                  </Button>
                </div>
                <div className="grid gap-6 sm:grid-cols-2">
                  {[view, monthStart(view, 1)].map((month, index) => (
                    <div key={month} className={cn("grid gap-2", index === 1 && "hidden sm:grid")}>
                      <p className="h-8 text-center font-heading text-base leading-8" aria-live="polite">
                        {formatMonthTitle(month)}
                      </p>
                      <table className="border-separate border-spacing-y-1" role="grid" aria-label={formatMonthTitle(month)}>
                        <thead>
                          <tr>
                            {weekdayHeaders.map((w, i) => (
                              <th key={w} scope="col" className={cn("size-10 text-xs font-medium", i >= 5 ? "text-primary/80" : "text-muted-foreground")}>
                                {w}
                              </th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {monthGrid(month).map((week, w) => (
                            <tr key={w}>
                              {week.map((day, i) => {
                                if (!day) return <td key={i} className="size-10" />;
                                const role = dayRole(day, range, hover);
                                const isDisabled = disabled(day);
                                const selected = role === "start" || role === "end" || role === "single";
                                return (
                                  <td
                                    key={day}
                                    className={cn(
                                      "p-0",
                                      role === "inside" && "bg-primary/10",
                                      role === "start" && "rounded-l-full bg-primary/10",
                                      role === "end" && "rounded-r-full bg-primary/10",
                                      // Полоса периода скругляется на краях недели и месяца.
                                      role === "inside" && (i === 0 || day.endsWith("-01")) && "rounded-l-full",
                                      role === "inside" && (i === 6 || shiftDays(day, 1).endsWith("-01")) && "rounded-r-full",
                                    )}
                                  >
                                    <button
                                      type="button"
                                      data-day={day}
                                      tabIndex={day === focus ? 0 : -1}
                                      disabled={isDisabled}
                                      aria-pressed={selected}
                                      aria-label={formatDayFull(day)}
                                      onClick={() => choose(day)}
                                      onMouseEnter={() => setHover(day)}
                                      onMouseLeave={() => setHover(null)}
                                      onFocus={() => setFocus(day)}
                                      className={cn(
                                        "tabular relative grid size-10 place-items-center rounded-full text-sm transition outline-none",
                                        "focus-visible:ring-3 focus-visible:ring-ring/60",
                                        selected
                                          ? "bg-primary font-semibold text-primary-foreground shadow-sm"
                                          : role === "inside"
                                            ? "text-foreground hover:bg-primary/15"
                                            : "hover:bg-accent",
                                        !selected && i >= 5 && "text-primary/90",
                                        isDisabled && "cursor-not-allowed text-muted-foreground/40 hover:bg-transparent",
                                        day === today &&
                                          "after:absolute after:bottom-1.5 after:size-1 after:rounded-full after:bg-current after:content-['']",
                                      )}
                                    >
                                      {Number(day.slice(8))}
                                    </button>
                                  </td>
                                );
                              })}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ))}
                </div>
              </div>

              <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-3">
                <p className="text-sm" aria-live="polite">
                  {range.start ? (
                    <>
                      <span className="font-medium">{formatDayShort(range.start)}</span>
                      {range.end && range.end !== range.start && (
                        <>
                          {" — "}
                          <span className="font-medium">{formatDayShort(range.end)}</span>
                        </>
                      )}
                      {mode === "stay" && nights !== null && <span className="text-muted-foreground"> · {nightsLabel(nights)}</span>}
                      {!range.end && <span className="text-muted-foreground"> · {mode === "stay" ? t("pickCheckOut") : t("pickEnd")}</span>}
                    </>
                  ) : (
                    <span className="text-muted-foreground">{mode === "stay" ? t("pickCheckIn") : t("pickStart")}</span>
                  )}
                </p>
                <div className="flex gap-2">
                  {mode === "period" && range.start && (
                    <Button type="button" variant="ghost" size="sm" onClick={() => setRange({ start: null, end: null })}>
                      {t("clear")}
                    </Button>
                  )}
                  <Popover.Close render={<Button type="button" size="sm" />}>{t("done")}</Popover.Close>
                </div>
              </div>
            </Popover.Popup>
          </Popover.Positioner>
        </Popover.Portal>
      </Popover.Root>
    </div>
  );
}
