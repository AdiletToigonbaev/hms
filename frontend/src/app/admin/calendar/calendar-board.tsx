"use client";

import { AlarmClock, CircleAlert, Clock3, GripVertical, LogIn, LogOut, MoveRight, UserRound, X } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useMemo, useRef, useState, useTransition, type PointerEvent as ReactPointerEvent } from "react";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormAlert } from "@/components/forms/form-alert";
import { buttonVariants } from "@/components/ui/button";
import type { FormState } from "@/lib/api/problem";
import type { CalendarBooking, CalendarData, CalendarRoom, ExtensionQuote, RescheduleQuote } from "@/lib/api/types";
import { nightsLabel } from "@/lib/bookings/status";
import {
  conflictsWith,
  dayMovements,
  fromMinutes,
  MINUTES_PER_DAY,
  moveStep,
  rescheduleTarget,
  resizeStep,
  snap,
  span,
  targetPeriod,
  toMinutes,
  type CalendarView,
} from "@/lib/bookings/timeline";
import { formatDayShort, formatWeekdayShort, shiftDays, type IsoDate } from "@/lib/calendar";
import { formatHotelLocal, formatHotelTime, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";
import type { RescheduleInput } from "@/lib/validation/bookings";
import { extendFromCalendarAction, quoteExtensionAction, quoteRescheduleAction, rescheduleBookingAction } from "../bookings/actions";

const ROW = 60;
const LABEL = 132;
/** Ширина часа и дня подстраивается под экран, но не мельче этих значений (иначе — прокрутка). */
const MIN_HOUR_PX = 44;
const MIN_DAY_PX = 140;
/** До первого измерения ширины (серверная отрисовка). */
const DEFAULT_HOUR_PX = 64;
const DEFAULT_DAY_PX = 168;

/** Цвет блока по статусу; «опаздывает» — поверх. Цвет всегда дублируется текстом в панели и подсказке. */
const tone: Record<string, string> = {
  Pending: "border-status-cleaning/60 bg-status-cleaning/15 text-status-cleaning border-dashed",
  AwaitingPayment: "border-status-cleaning/60 bg-status-cleaning/15 text-status-cleaning border-dashed",
  Confirmed: "border-status-reserved/60 bg-status-reserved/18 text-foreground",
  CheckedIn: "border-status-occupied/60 bg-status-occupied/18 text-foreground",
  CheckedOut: "border-status-available/40 bg-status-available/10 text-muted-foreground",
};

interface Drag {
  booking: CalendarBooking;
  mode: "move" | "resize";
  pointerId: number;
  x0: number;
  y0: number;
  dx: number;
  dy: number;
  moved: boolean;
}

interface Pending {
  booking: CalendarBooking;
  target: RescheduleInput;
  quote: RescheduleQuote;
}

/** Продление правым краем (бронь заселённого гостя или та, что не переносится): на сколько и за сколько. */
interface PendingExtension {
  booking: CalendarBooking;
  units: number;
  quote: ExtensionQuote;
}

export function CalendarBoard({
  data,
  view,
  from,
  days,
  today,
  canEdit,
  canCreate,
}: {
  data: CalendarData;
  view: CalendarView;
  from: IsoDate;
  days: number;
  today: IsoDate;
  canEdit: boolean;
  canCreate: boolean;
}) {
  const t = useTranslations("Calendar");
  const router = useRouter();
  const scroller = useRef<HTMLDivElement>(null);
  // Шкала на всю ширину: сутки (или неделя) занимают всё место справа от столбца номеров.
  const [boxWidth, setBoxWidth] = useState(0);
  useEffect(() => {
    const el = scroller.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setBoxWidth(el.clientWidth));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const free = Math.max(0, boxWidth - LABEL);
  const HOUR_PX = boxWidth ? Math.max(MIN_HOUR_PX, Math.floor((free / 24) * 100) / 100) : DEFAULT_HOUR_PX;
  const DAY_PX = boxWidth ? Math.max(MIN_DAY_PX, Math.floor((free / days) * 100) / 100) : DEFAULT_DAY_PX;
  const ppm = view === "day" ? HOUR_PX / 60 : DAY_PX / MINUTES_PER_DAY;
  const width = days * MINUTES_PER_DAY * ppm;
  const rooms = data.rooms;
  const rowOf = useMemo(() => new Map(rooms.map((r, i) => [r.id, i])), [rooms]);
  const roomById = useMemo(() => new Map(rooms.map((r) => [r.id, r])), [rooms]);
  const limits = { minHours: data.hourlyMinHours, maxHours: data.hourlyMaxHours };

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = data.bookings.find((b) => b.id === selectedId) ?? null;
  const [drag, setDrag] = useState<Drag | null>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const [pendingExtension, setPendingExtension] = useState<PendingExtension | null>(null);
  const [notice, setNotice] = useState<FormState>({});
  const [success, setSuccess] = useState<string | null>(null);
  const [busy, startBusy] = useTransition();

  const nowSpan = span(data.now, data.now, from, days, ppm);
  const inRange = today >= from && today <= shiftDays(from, days - 1);
  const movements = inRange ? dayMovements(data.bookings, today) : null;

  useEffect(() => {
    if (!selectedId) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setSelectedId(null);
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [selectedId]);

  // Сразу показываем «сейчас» (или утро), а не полночь.
  useEffect(() => {
    if (!scroller.current) return;
    // День — к текущему часу (или к утру); неделя — к сегодняшнему дню.
    const anchor = nowSpan ? nowSpan.left : view === "day" ? 8 * HOUR_PX : 0;
    scroller.current.scrollLeft = Math.max(0, anchor - (view === "day" ? 2 * HOUR_PX : DAY_PX / 2));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view, from, HOUR_PX, DAY_PX]);

  /** Перетащить (перенос): бронь до заселения. */
  const canMove = (b: CalendarBooking) => canEdit && b.canReschedule;
  /** Правый край: у переносимой брони — длительность; у заселённой — продление. */
  const canResize = (b: CalendarBooking) => canEdit && resizeStep(b.kind, view) !== null && (b.canReschedule || b.canExtend);
  /** Тянут край брони, которую нельзя перенести, — это продление. */
  const isExtension = (d: Drag) => d.mode === "resize" && !d.booking.canReschedule;

  /** Что получится, если отпустить блок сейчас. */
  function preview(d: Drag) {
    const b = d.booking;
    const step = d.mode === "move" ? moveStep(b.kind, view) : resizeStep(b.kind, view);
    const delta = step ? snap(d.dx / ppm, step) : 0;
    const shift = d.mode === "move" ? delta : 0;
    const stretch = d.mode === "resize" ? delta : 0;
    const fromRow = rowOf.get(b.roomId) ?? 0;
    const row = d.mode === "move" ? Math.min(rooms.length - 1, Math.max(0, fromRow + Math.round(d.dy / ROW))) : fromRow;
    const room = rooms[row];
    const roomOk = room.isSellable && (b.kind === "Nightly" || room.sellsHourly);
    if (isExtension(d)) {
      // Продление: только вперёд, конец (с уборкой) не должен задеть следующую бронь.
      const units = Math.round(stretch / (b.kind === "Hourly" ? 60 : MINUTES_PER_DAY));
      const tooLong = b.kind === "Hourly" && (b.hours ?? 0) + units > limits.maxHours;
      const busy = units > 0 && conflictsWith(data.bookings, b.id, b.roomId, { start: toMinutes(b.startsAt), blockedUntil: toMinutes(b.blockedUntil) + stretch });
      return { shift: 0, stretch, rowDelta: 0, room, roomOk: true, target: null, busy, extendUnits: units, tooLong };
    }
    const target = rescheduleTarget(b, { roomId: room.id, shift, stretch }, limits);
    const busy = target !== null && conflictsWith(data.bookings, b.id, room.id, targetPeriod(target, b, data));
    return { shift, stretch, rowDelta: row - fromRow, room, roomOk, target, busy, extendUnits: 0, tooLong: false };
  }

  function onPointerDown(e: ReactPointerEvent<HTMLElement>, b: CalendarBooking, mode: Drag["mode"]) {
    if (e.button !== 0) return;
    e.stopPropagation();
    if (mode === "move" ? canMove(b) : canResize(b)) e.currentTarget.setPointerCapture(e.pointerId);
    setDrag({ booking: b, mode, pointerId: e.pointerId, x0: e.clientX, y0: e.clientY, dx: 0, dy: 0, moved: false });
  }

  function onPointerMove(e: ReactPointerEvent<HTMLElement>) {
    if (!drag || drag.pointerId !== e.pointerId || !(drag.mode === "move" ? canMove(drag.booking) : canResize(drag.booking))) return;
    const dx = e.clientX - drag.x0;
    const dy = e.clientY - drag.y0;
    setDrag({ ...drag, dx, dy, moved: drag.moved || Math.abs(dx) > 5 || Math.abs(dy) > 5 });
  }

  function onPointerUp(e: ReactPointerEvent<HTMLElement>) {
    if (!drag || drag.pointerId !== e.pointerId) return;
    const d = drag;
    setDrag(null);
    if (!d.moved) {
      setSelectedId(d.booking.id);
      return;
    }
    const p = preview(d);
    if (isExtension(d)) {
      extendByDrag(d.booking, p.extendUnits, p.busy, p.tooLong);
      return;
    }
    if (!p.roomOk) {
      setNotice({ ok: false, error: "calendar.room_not_suitable" });
      return;
    }
    if (!p.target) {
      if (p.shift !== 0 || p.stretch !== 0 || p.rowDelta !== 0) setNotice({ ok: false, error: "calendar.duration_out_of_rules" });
      return;
    }
    if (p.busy) {
      setNotice({ ok: false, error: "booking.room_unavailable" });
      return;
    }
    const target = p.target;
    setNotice({});
    setSuccess(null);
    startBusy(async () => {
      const quote = await quoteRescheduleAction(d.booking.id, target);
      if (quote.ok) setPending({ booking: d.booking, target, quote: quote.data });
      else setNotice(quote.state);
    });
  }

  /** Продление правым краем: расчёт API (новый выезд, доплата, свободен ли номер) → окно подтверждения. */
  function extendByDrag(b: CalendarBooking, units: number, busy: boolean, tooLong: boolean) {
    if (units < 0) {
      setNotice({ ok: false, error: "calendar.shorten_not_available" });
      return;
    }
    if (units === 0) return;
    if (tooLong) {
      setNotice({ ok: false, error: "calendar.duration_out_of_rules" });
      return;
    }
    if (busy) {
      setNotice({ ok: false, error: "booking.room_unavailable" });
      return;
    }
    setNotice({});
    setSuccess(null);
    startBusy(async () => {
      const quote = await quoteExtensionAction(b.id, units);
      if (quote.ok) setPendingExtension({ booking: b, units, quote: quote.quote });
      else setNotice(quote.state);
    });
  }

  function confirmExtension() {
    if (!pendingExtension) return;
    const { booking, units, quote } = pendingExtension;
    setPendingExtension(null);
    startBusy(async () => {
      const result = await extendFromCalendarAction(booking.id, units, booking.version);
      if (result.ok) {
        setNotice({});
        setSuccess(t("extendedTo", { number: booking.number, end: formatHotelLocal(quote.newEndsAt), amount: formatMoney(quote.amount) }));
        setSelectedId(booking.id);
        router.refresh();
      } else {
        setNotice(result.state);
      }
    });
  }

  function confirmMove() {
    if (!pending) return;
    const { booking, target, quote } = pending;
    setPending(null);
    startBusy(async () => {
      const result = await rescheduleBookingAction(booking.id, target, booking.version);
      if (result.ok) {
        setNotice({});
        setSuccess(t("moved", { number: booking.number, when: rangeLabel(quote.startsAt, quote.endsAt, booking.kind), room: quote.roomNumber }));
        setSelectedId(booking.id);
        router.refresh();
      } else {
        setNotice(result.state);
      }
    });
  }

  /** Клик по свободному месту — новая бронь на это время и номер. */
  function onTrackClick(e: React.MouseEvent<HTMLDivElement>, room: CalendarRoom) {
    if (!canCreate || !room.isSellable || e.target !== e.currentTarget) return;
    const x = e.nativeEvent.offsetX;
    const minute = Math.floor(x / ppm);
    const day = shiftDays(from, Math.floor(minute / MINUTES_PER_DAY));
    if (view === "day" && room.sellsHourly) {
      const hour = Math.floor((minute % MINUTES_PER_DAY) / 60);
      router.push(`/admin/bookings/new?date=${day}&startHour=${hour}&hours=${data.hourlyMinHours}&roomId=${room.id}`);
    } else {
      router.push(`/admin/bookings/new?kind=nightly&checkIn=${day}&checkOut=${shiftDays(day, 1)}&roomId=${room.id}`);
    }
  }

  const rangeLabel = (start: string, end: string, kind: CalendarBooking["kind"]) =>
    kind === "Hourly" || start.slice(0, 10) === end.slice(0, 10)
      ? `${formatDayShort(start.slice(0, 10))}, ${formatHotelTime(start)}–${formatHotelTime(end)}`
      : `${formatDayShort(start.slice(0, 10))} — ${formatDayShort(end.slice(0, 10))}`;

  const length = (b: Pick<CalendarBooking, "kind" | "hours" | "nights">) => (b.kind === "Hourly" ? t("hoursShort", { n: b.hours ?? 0 }) : nightsLabel(b.nights));

  const pendingText = pending
    ? [
        t("moveFrom", { when: rangeLabel(pending.booking.startsAt, pending.booking.endsAt, pending.booking.kind), room: roomById.get(pending.booking.roomId)?.number ?? "—" }),
        t("moveTo", { when: rangeLabel(pending.quote.startsAt, pending.quote.endsAt, pending.booking.kind), room: pending.quote.roomNumber }),
        pending.quote.newTotal === pending.quote.oldTotal
          ? t("priceSame", { amount: formatMoney(pending.quote.newTotal) })
          : t("priceChange", { from: formatMoney(pending.quote.oldTotal), to: formatMoney(pending.quote.newTotal) }),
      ].join(" ")
    : "";

  return (
    <div className="grid min-w-0 gap-4">
      {movements && <TodayStrip movements={movements} roomById={roomById} />}

      {/* Подробности брони — панель поверх календаря справа (на телефоне — снизу); место под неё не резервируется. */}
      {selected && (
        <aside
          aria-label={t("details")}
          className="fixed inset-x-2 bottom-2 z-40 max-h-[70dvh] overflow-auto sm:inset-x-auto sm:right-4 sm:bottom-4 sm:w-[22rem]"
        >
          <SelectedBooking
            booking={selected}
            room={roomById.get(selected.roomId)}
            when={rangeLabel(selected.startsAt, selected.endsAt, selected.kind)}
            length={length(selected)}
            canEdit={canEdit}
            onClose={() => setSelectedId(null)}
          />
        </aside>
      )}

      <div className="grid min-w-0 content-start gap-4">

        <div className="grid gap-2">
          <Legend />
          {canEdit && <p className="text-xs text-muted-foreground">{view === "day" ? t("dragHintDay") : t("dragHintWeek")}</p>}
        </div>

        <div aria-live="polite" className="grid gap-2">
          <FormAlert state={notice} />
          {success && <FormAlert state={{ ok: true }} success={success} />}
          {busy && <p className="text-sm text-muted-foreground">{t("working")}</p>}
        </div>

        {rooms.length === 0 ? (
          <p className="rounded-2xl border bg-card p-6 text-sm text-muted-foreground">{t("noRooms")}</p>
        ) : (
          <div ref={scroller} className="relative max-h-[calc(100dvh-12rem)] min-h-80 overflow-auto overscroll-contain rounded-2xl border bg-card shadow-xs" data-testid="calendar-board">
            <div style={{ width: LABEL + width }} className="relative">
              {/* Шкала: часы или дни */}
              <div className="sticky top-0 z-20 flex border-b bg-card/95 backdrop-blur">
                <div style={{ width: LABEL }} className="sticky left-0 z-10 shrink-0 border-r bg-card px-3 py-2 text-xs font-medium text-muted-foreground">
                  {t("roomCol")}
                </div>
                {view === "day"
                  ? Array.from({ length: 24 }, (_, h) => (
                      <div key={h} data-hour={h} style={{ width: HOUR_PX }} className="tabular shrink-0 border-l border-border/60 px-1.5 py-2 text-xs text-muted-foreground">
                        {String(h).padStart(2, "0")}:00
                      </div>
                    ))
                  : Array.from({ length: days }, (_, i) => {
                      const day = shiftDays(from, i);
                      return (
                        <div
                          key={day}
                          style={{ width: DAY_PX }}
                          className={cn("shrink-0 border-l border-border/60 px-2 py-2 text-xs", day === today ? "bg-primary/8 font-semibold text-primary" : "text-muted-foreground")}
                        >
                          <span className="capitalize">{formatWeekdayShort(day)}</span>, {formatDayShort(day)}
                        </div>
                      );
                    })}
              </div>

              {/* Строки номеров */}
              <div className="relative">
                {rooms.map((room) => (
                  <div key={room.id} className="flex border-b last:border-b-0" style={{ height: ROW }}>
                    <div style={{ width: LABEL }} className="sticky left-0 z-10 grid shrink-0 content-center border-r bg-card px-3">
                      <span className="tabular text-sm font-semibold">№ {room.number}</span>
                      <span className="truncate text-xs text-muted-foreground" title={room.roomTypeName}>
                        {room.isSellable ? room.roomTypeName : t("notSellable")}
                      </span>
                    </div>
                    <div
                      role="presentation"
                      onClick={(e) => onTrackClick(e, room)}
                      data-room={room.number}
                      className={cn("relative shrink-0", canCreate && room.isSellable && "cursor-cell")}
                      style={{
                        width,
                        backgroundImage: `repeating-linear-gradient(to right, transparent 0, transparent ${(view === "day" ? HOUR_PX : DAY_PX) - 1}px, var(--color-border) ${(view === "day" ? HOUR_PX : DAY_PX) - 1}px, var(--color-border) ${view === "day" ? HOUR_PX : DAY_PX}px)`,
                      }}
                    >
                      {!room.isSellable && <div aria-hidden className="bg-hatch pointer-events-none absolute inset-0" />}
                      {data.bookings
                        .filter((b) => b.roomId === room.id)
                        .map((b) => {
                          const block = span(b.startsAt, b.endsAt, from, days, ppm);
                          const cleaning = span(b.endsAt, b.blockedUntil, from, days, ppm);
                          const dragging = drag?.booking.id === b.id && drag.moved ? preview(drag) : null;
                          const draggable = canMove(b);
                          const resizable = canResize(b);
                          const why = !canEdit
                            ? null
                            : b.canReschedule
                              ? null
                              : b.canExtend
                                ? resizable
                                  ? t("whyExtendOnly")
                                  : t("whyExtendInCard")
                                : t("whyClosed");
                          return (
                            <div key={b.id}>
                              {cleaning && (
                                <div
                                  aria-hidden
                                  title={t("cleaning", { n: toMinutes(b.blockedUntil) - toMinutes(b.endsAt) })}
                                  className="bg-hatch pointer-events-none absolute top-2 bottom-2 rounded-r-md border border-l-0 border-border/70"
                                  style={{ left: cleaning.left, width: cleaning.width }}
                                />
                              )}
                              {block && (
                                <button
                                  type="button"
                                  data-booking={b.number}
                                  aria-label={t("blockLabel", { number: b.number, guest: b.guestName, when: rangeLabel(b.startsAt, b.endsAt, b.kind) })}
                                  aria-pressed={selectedId === b.id}
                                  title={`${b.guestName} · ${rangeLabel(b.startsAt, b.endsAt, b.kind)} · № ${room.number}${why ? `\n${why}` : ""}`}
                                  onPointerDown={(e) => onPointerDown(e, b, "move")}
                                  onPointerMove={onPointerMove}
                                  onPointerUp={onPointerUp}
                                  onPointerCancel={() => setDrag(null)}
                                  onKeyDown={(e) => (e.key === "Enter" || e.key === " ") && setSelectedId(b.id)}
                                  className={cn(
                                    "absolute top-1.5 bottom-1.5 flex items-stretch overflow-hidden rounded-lg border text-left text-xs shadow-xs transition-[box-shadow,opacity] select-none focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
                                    tone[b.status] ?? tone.Confirmed,
                                    b.noShowDue && "border-destructive/70 ring-1 ring-destructive/40",
                                    selectedId === b.id && "ring-2 ring-primary",
                                    draggable ? "cursor-grab touch-none active:cursor-grabbing" : "cursor-pointer",
                                    dragging && "opacity-40",
                                    block.clippedStart && "rounded-l-none border-l-0",
                                    block.clippedEnd && "rounded-r-none border-r-0",
                                  )}
                                  style={{ left: block.left, width: block.width }}
                                >
                                  <span className="grid min-w-0 flex-1 content-center gap-0.5 px-2">
                                    <span className="flex items-center gap-1 truncate font-semibold">
                                      {b.noShowDue && <CircleAlert className="size-3.5 shrink-0 text-destructive" aria-hidden />}
                                      {b.holdUntil && <Clock3 className="size-3.5 shrink-0" aria-hidden />}
                                      <span className="truncate">{b.guestName}</span>
                                    </span>
                                    <span className="tabular truncate opacity-80">
                                      {b.kind === "Hourly" ? `${formatHotelTime(b.startsAt)}–${formatHotelTime(b.endsAt)}` : nightsLabel(b.nights)}
                                    </span>
                                  </span>
                                  {resizable && (
                                    <span
                                      role="presentation"
                                      title={t("resizeHint")}
                                      onPointerDown={(e) => onPointerDown(e, b, "resize")}
                                      onPointerMove={onPointerMove}
                                      onPointerUp={onPointerUp}
                                      className="flex w-3 shrink-0 cursor-ew-resize touch-none items-center justify-center border-l border-current/20 opacity-60 hover:opacity-100"
                                    >
                                      <GripVertical className="size-3" aria-hidden />
                                    </span>
                                  )}
                                </button>
                              )}
                              {dragging && block && (
                                <div
                                  aria-hidden
                                  className={cn(
                                    "pointer-events-none absolute top-1.5 bottom-1.5 z-30 grid content-center rounded-lg border-2 px-2 text-xs font-semibold shadow-lg",
                                    dragging.roomOk && !dragging.busy && !dragging.tooLong && (dragging.target || dragging.extendUnits > 0 || (dragging.shift === 0 && dragging.stretch === 0 && dragging.rowDelta === 0))
                                      ? "border-status-occupied bg-status-occupied/15 text-status-occupied"
                                      : "border-destructive bg-destructive/10 text-destructive",
                                  )}
                                  style={{
                                    left: block.left + dragging.shift * ppm,
                                    width: Math.max(16, block.width + dragging.stretch * ppm),
                                    transform: `translateY(${dragging.rowDelta * ROW}px)`,
                                  }}
                                >
                                  <span className="tabular truncate">
                                    {isExtension(drag!)
                                      ? dragging.extendUnits > 0
                                        ? `${b.kind === "Hourly" ? `+${t("hoursShort", { n: dragging.extendUnits })}` : `+${nightsLabel(dragging.extendUnits)}`} · ${t("until", { time: formatHotelTime(fromMinutes(toMinutes(b.endsAt) + dragging.stretch)) })}`
                                        : t("shortenLater")
                                      : dragging.target
                                      ? dragging.target.kind === "Hourly"
                                        ? `№ ${dragging.room.number} · ${String(dragging.target.startHour).padStart(2, "0")}:00 · ${t("hoursShort", { n: dragging.target.hours })}`
                                        : `№ ${dragging.room.number} · ${formatDayShort(dragging.target.checkIn)} — ${formatDayShort(dragging.target.checkOut)}`
                                      : `№ ${dragging.room.number}`}
                                    {dragging.busy && ` · ${t("busy")}`}
                                    {!dragging.roomOk && ` · ${t("notSuitable")}`}
                                  </span>
                                </div>
                              )}
                            </div>
                          );
                        })}
                    </div>
                  </div>
                ))}

                {nowSpan && (
                  <div aria-hidden className="pointer-events-none absolute top-0 bottom-0 z-10 w-0.5 bg-destructive/70" style={{ left: LABEL + nowSpan.left }}>
                    <span className="absolute -top-0.5 -left-[3px] size-2 rounded-full bg-destructive" />
                  </div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>

      <ConfirmDialog
        open={pendingExtension !== null}
        onOpenChange={(open) => !open && setPendingExtension(null)}
        title={pendingExtension ? t("extendTitle", { number: pendingExtension.booking.number }) : ""}
        description={
          pendingExtension
            ? t("extendText", { end: formatHotelLocal(pendingExtension.quote.newEndsAt), amount: formatMoney(pendingExtension.quote.amount) })
            : ""
        }
        confirmLabel={t("extendYes")}
        cancelLabel={t("moveNo")}
        onConfirm={confirmExtension}
      />

      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(open) => !open && setPending(null)}
        title={pending ? t("moveTitle", { number: pending.booking.number }) : ""}
        description={pendingText}
        confirmLabel={t("moveYes")}
        cancelLabel={t("moveNo")}
        onConfirm={confirmMove}
      />
    </div>
  );
}

function SelectedBooking({
  booking: b,
  room,
  when,
  length,
  canEdit,
  onClose,
}: {
  booking: CalendarBooking;
  room: CalendarRoom | undefined;
  when: string;
  length: string;
  canEdit: boolean;
  onClose: () => void;
}) {
  const t = useTranslations("Calendar");
  return (
    <section className="grid gap-3 rounded-2xl border bg-card p-5 shadow-xl" aria-labelledby="selected-title">
      <div className="flex items-start justify-between gap-2">
        <div className="grid gap-1.5">
          <h2 id="selected-title" className="tabular font-heading text-lg">
            {t("booking", { number: b.number })}
          </h2>
          <BookingStatusBadge status={b.status} className="w-fit" />
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label={t("close")}
          className="grid size-8 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-accent hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        >
          <X className="size-4" aria-hidden />
        </button>
      </div>
      <dl className="grid gap-2 text-sm">
        <div className="flex items-center gap-2">
          <UserRound className="size-4 text-muted-foreground" aria-hidden />
          <dd className="font-medium">{b.guestName}</dd>
        </div>
        {b.guestPhone && <dd className="tabular pl-6 text-muted-foreground">{b.guestPhone}</dd>}
        <div className="flex items-center gap-2">
          <Clock3 className="size-4 text-muted-foreground" aria-hidden />
          <dd className="tabular">
            {when} · {length}
          </dd>
        </div>
        <dd className="pl-6 text-muted-foreground">
          № {room?.number ?? "—"} · {room?.roomTypeName ?? ""} · {t("people", { n: b.adults + b.children })}
        </dd>
        <dd className="tabular pl-6 font-medium">{formatMoney(b.totalAmount)}</dd>
      </dl>
      {b.holdUntil && (
        <p className="flex gap-2 rounded-xl bg-status-cleaning/10 px-3 py-2 text-sm text-status-cleaning">
          <AlarmClock className="mt-0.5 size-4 shrink-0" aria-hidden />
          {t("holdUntil", { time: formatHotelLocal(b.holdUntil) })}
        </p>
      )}
      {b.noShowDue && (
        <p className="flex gap-2 rounded-xl bg-destructive/10 px-3 py-2 text-sm text-destructive">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          {t("lateGuest")}
        </p>
      )}
      <Link href={`/admin/bookings/${b.id}`} className={buttonVariants({ className: "w-full" })}>
        {t("open")}
        <MoveRight aria-hidden />
      </Link>
      {canEdit && (
        <p className="text-xs text-muted-foreground">{b.canReschedule ? t("canDrag") : b.canExtend ? t("canExtendOnly") : t("cannotDrag")}</p>
      )}
    </section>
  );
}

function TodayStrip({ movements, roomById }: { movements: ReturnType<typeof dayMovements>; roomById: Map<string, CalendarRoom> }) {
  const t = useTranslations("Calendar");
  const groups = [
    { key: "arrivals", title: t("arrivals"), icon: LogIn, items: movements.arrivals, time: (b: CalendarBooking) => formatHotelTime(b.startsAt), tone: "" },
    { key: "departures", title: t("departures"), icon: LogOut, items: movements.departures, time: (b: CalendarBooking) => formatHotelTime(b.endsAt), tone: "" },
    { key: "late", title: t("late"), icon: CircleAlert, items: movements.late, time: (b: CalendarBooking) => formatHotelTime(b.startsAt), tone: "text-destructive" },
    {
      key: "awaiting",
      title: t("awaiting"),
      icon: AlarmClock,
      items: movements.awaiting,
      time: (b: CalendarBooking) => t("until", { time: formatHotelTime(b.holdUntil ?? "") }),
      tone: "text-status-cleaning",
    },
  ] as const;
  return (
    <section aria-label={t("todayTitle")} className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {groups.map((g) => (
        <div key={g.key} className="grid content-start gap-2 rounded-2xl border bg-card p-4 shadow-xs">
          <h2 className={cn("flex items-center gap-2 text-sm font-semibold", g.tone)}>
            <g.icon className="size-4" aria-hidden />
            {g.title}
            <span className="tabular ml-auto rounded-full bg-secondary px-2 text-xs text-secondary-foreground">{g.items.length}</span>
          </h2>
          {g.items.length === 0 ? (
            <p className="text-xs text-muted-foreground">{t("none")}</p>
          ) : (
            <ul className="grid gap-1">
              {g.items.slice(0, 5).map((b) => (
                <li key={b.id}>
                  <Link href={`/admin/bookings/${b.id}`} className="flex gap-2 rounded-lg px-1.5 py-1 text-xs hover:bg-accent">
                    <span className="tabular w-16 shrink-0 font-medium">{g.time(b)}</span>
                    <span className="tabular shrink-0 text-muted-foreground">№ {roomById.get(b.roomId)?.number ?? "—"}</span>
                    <span className="truncate">{b.guestName}</span>
                  </Link>
                </li>
              ))}
              {g.items.length > 5 && <li className="px-1.5 text-xs text-muted-foreground">{t("more", { n: g.items.length - 5 })}</li>}
            </ul>
          )}
        </div>
      ))}
    </section>
  );
}

function Legend() {
  const t = useTranslations("Calendar");
  const items = [
    { label: t("legendConfirmed"), className: tone.Confirmed },
    { label: t("legendPending"), className: tone.Pending },
    { label: t("legendCheckedIn"), className: tone.CheckedIn },
    { label: t("legendCleaning"), className: "bg-hatch border-border/70" },
    { label: t("legendLate"), className: "border-destructive/70 bg-card" },
  ];
  return (
    <ul className="flex flex-wrap items-center gap-2" aria-label={t("legend")}>
      {items.map((i) => (
        <li key={i.label} className="flex items-center gap-2.5 rounded-full border bg-card px-3.5 py-1.5 text-sm shadow-xs">
          <span className={cn("inline-block h-4 w-9 rounded-md border", i.className)} aria-hidden />
          {i.label}
        </li>
      ))}
    </ul>
  );
}

