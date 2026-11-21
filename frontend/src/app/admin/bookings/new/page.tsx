import { ArrowLeft, BedDouble, Check, Clock3, MoonStar } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { PriceBreakdown } from "@/components/price-breakdown";
import { EmptyState } from "@/components/states/states";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { hotelTimeZone } from "@/i18n/config";
import { errorKey } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { AvailableRoom, ExtraService, Guest, GuestLookup, RoomType } from "@/lib/api/types";
import { addDays, hotelToday, nightsLabel } from "@/lib/bookings/status";
import { isIsoDate } from "@/lib/calendar";
import { formatMoney, formatStayDate } from "@/lib/format";
import { getHotelInfo } from "@/lib/hotel";
import { cn } from "@/lib/utils";
import { MAX_NIGHTS, hourlySchema, staySchema } from "@/lib/validation/bookings";
import { CreateBookingForm, type StayParams } from "../booking-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Bookings"))("newTitle") };
}

const uuid = /^[0-9a-f-]{36}$/i;
const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);
const pad = (n: number) => String(n).padStart(2, "0");

/** Текущий час по времени гостиницы — раньше начать почасовую бронь сегодня нельзя. */
function hotelHour(timeZone: string): number {
  return Number(new Intl.DateTimeFormat("en-GB", { timeZone, hour: "2-digit", hourCycle: "h23" }).format(new Date()));
}

export default async function NewBookingPage({ searchParams }: PageProps<"/admin/bookings/new">) {
  const params = await searchParams;
  const today = hotelToday(hotelTimeZone);
  // Почасовая бронь — основной поток гостиницы, поэтому вид по умолчанию; посуточная — ?kind=nightly.
  const hourly = str(params.kind) !== "nightly";
  const hotelInfo = await getHotelInfo();
  const minHours = hotelInfo.hourlyMinHours;
  const maxHours = hotelInfo.hourlyMaxHours;
  const nowHour = hotelHour(hotelTimeZone);

  const raw = {
    checkIn: str(params.checkIn) ?? today,
    checkOut: str(params.checkOut) ?? addDays(str(params.checkIn) ?? today, 1),
    date: str(params.date) ?? (nowHour >= 23 ? addDays(today, 1) : today),
    startHour: str(params.startHour) ?? String(nowHour >= 23 ? 10 : Math.max(nowHour + 1, 0)),
    hours: str(params.hours) ?? String(minHours),
    adults: str(params.adults) ?? "2",
    children: str(params.children) ?? "0",
  };
  const typeId = str(params.type) && uuid.test(str(params.type)!) ? str(params.type)! : "";
  const roomId = str(params.roomId) && uuid.test(str(params.roomId)!) ? str(params.roomId)! : "";
  // Бронь из карточки гостя: гость выбран заранее.
  const guestId = str(params.guestId) && uuid.test(str(params.guestId)!) ? str(params.guestId)! : "";
  const searched = hourly ? params.date !== undefined : params.checkIn !== undefined;
  const nightly = staySchema.safeParse(raw);
  const hourlyStay = hourlySchema.safeParse(raw);
  const valid = hourly ? hourlyStay.success : nightly.success;

  const [t, errors, types, services, preselected] = await Promise.all([
    getTranslations("Bookings"),
    getTranslations("Errors"),
    api<RoomType[]>("/room-types"),
    api<ExtraService[]>("/services"),
    guestId ? api<Guest>(`/guests/${guestId}`) : Promise.resolve(null),
  ]);
  const initialGuest: GuestLookup | null =
    preselected?.ok && !preselected.data.isAnonymized
      ? { id: preselected.data.id, fullName: preselected.data.fullName, phone: preselected.data.phone, email: preselected.data.email, bookingsCount: preselected.data.stats.bookingsCount }
      : null;

  let rooms: AvailableRoom[] | null = null;
  let searchError: string | null = null;
  if (searched && valid) {
    const query = new URLSearchParams({ adults: raw.adults, children: raw.children });
    if (hourly && hourlyStay.success) {
      query.set("kind", "Hourly");
      query.set("date", hourlyStay.data.date);
      query.set("startHour", String(hourlyStay.data.startHour));
      query.set("hours", String(hourlyStay.data.hours));
    } else if (nightly.success) {
      query.set("checkIn", nightly.data.checkIn);
      query.set("checkOut", nightly.data.checkOut);
    }
    if (typeId) query.set("roomTypeId", typeId);
    const result = await api<AvailableRoom[]>(`/availability?${query}`);
    if (result.ok) rooms = result.data;
    else {
      const key = errorKey(result.problem.code ?? "unknown") as Parameters<typeof errors>[0];
      searchError = errors.has(key) ? errors(key) : (result.problem.title ?? errors("unknown"));
    }
  } else if (searched) {
    const issues = hourly ? hourlyStay.error?.issues : nightly.error?.issues;
    searchError = issues?.[0]?.message ?? errors("validation_failed");
  }

  const selected = rooms?.find((r) => r.roomId === roomId) ?? null;
  const base = new URLSearchParams(
    hourly
      ? { kind: "hourly", date: raw.date, startHour: raw.startHour, hours: raw.hours, adults: raw.adults, children: raw.children }
      : { kind: "nightly", checkIn: raw.checkIn, checkOut: raw.checkOut, adults: raw.adults, children: raw.children },
  );
  if (typeId) base.set("type", typeId);
  if (initialGuest) base.set("guestId", initialGuest.id);
  const chooseLink = (id: string) => {
    const next = new URLSearchParams(base);
    next.set("roomId", id);
    return `/admin/bookings/new?${next}#guest`;
  };

  const stayParams: StayParams | null = !selected
    ? null
    : hourly && hourlyStay.success
      ? { kind: "Hourly", roomId: selected.roomId, date: hourlyStay.data.date, startHour: hourlyStay.data.startHour, hours: hourlyStay.data.hours, adults: hourlyStay.data.adults, children: hourlyStay.data.children }
      : nightly.success
        ? { kind: "Nightly", roomId: selected.roomId, checkIn: nightly.data.checkIn, checkOut: nightly.data.checkOut, adults: nightly.data.adults, children: nightly.data.children }
        : null;

  const startHour = Number(raw.startHour);
  const hoursCount = Number(raw.hours);
  const summary = hourly
    ? `${formatStayDate(raw.date)}, ${pad(startHour)}:00 — ${pad((startHour + hoursCount) % 24)}:00 · ${hoursCount} ч`
    : nightly.success
      ? `${formatStayDate(nightly.data.checkIn)} — ${formatStayDate(nightly.data.checkOut)}, ${nightsLabel(rooms?.[0]?.quote.nights.length ?? 0)}`
      : "";
  const tall = "h-14 rounded-xl";

  return (
    <div className="grid gap-6">
      <div className="grid gap-3">
        <Link href="/admin/bookings" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("back")}
        </Link>
        <PageHeader title={t("newTitle")} description={t("newDescription")} />
      </div>

      <Card>
        <CardHeader className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle>
            <h2 className="text-xl">1. {t("step1")}</h2>
          </CardTitle>
          <nav aria-label={t("kind")} className="inline-flex rounded-xl border bg-secondary/60 p-1">
            {[
              { href: "/admin/bookings/new", active: hourly, label: t("kindHourly"), icon: Clock3 },
              { href: "/admin/bookings/new?kind=nightly", active: !hourly, label: t("kindNightly"), icon: MoonStar },
            ].map((tab) => (
              <Link
                key={tab.href}
                href={tab.href}
                aria-current={tab.active ? "page" : undefined}
                className={cn(
                  "inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition",
                  tab.active ? "bg-card text-primary shadow-sm" : "text-muted-foreground hover:text-foreground",
                )}
              >
                <tab.icon className="size-4" aria-hidden />
                {tab.label}
              </Link>
            ))}
          </nav>
        </CardHeader>
        <CardContent>
          <form
            method="get"
            className={cn(
              "grid gap-4 sm:grid-cols-2 lg:items-end",
              hourly
                ? "lg:grid-cols-[minmax(0,1.6fr)_minmax(0,0.9fr)_minmax(0,0.9fr)_minmax(0,0.7fr)_minmax(0,0.7fr)_minmax(0,1.3fr)_auto]"
                : "lg:grid-cols-[minmax(0,2.4fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,1.4fr)_auto]",
            )}
          >
            {initialGuest && <input type="hidden" name="guestId" value={initialGuest.id} />}
            {hourly ? (
              <>
                <input type="hidden" name="kind" value="hourly" />
                <div className="grid gap-1.5">
                  <span className="text-sm font-medium">{t("date")}</span>
                  <DateRangePicker
                    key={raw.date}
                    mode="single"
                    startName="date"
                    defaultStart={isIsoDate(raw.date) ? raw.date : today}
                    today={today}
                    min={today}
                    startLabel={t("date")}
                  />
                </div>
                <div className="grid gap-1.5">
                  <Label htmlFor="startHour">{t("startTime")}</Label>
                  <NativeSelect id="startHour" name="startHour" defaultValue={raw.startHour} className={tall}>
                    {Array.from({ length: 24 }, (_, h) => (
                      <option key={h} value={h}>
                        {pad(h)}:00
                      </option>
                    ))}
                  </NativeSelect>
                </div>
                <div className="grid gap-1.5">
                  <Label htmlFor="hours">{t("hours")}</Label>
                  <NativeSelect id="hours" name="hours" defaultValue={raw.hours} className={tall}>
                    {Array.from({ length: maxHours - minHours + 1 }, (_, i) => minHours + i).map((h) => (
                      <option key={h} value={h}>
                        {h} ч
                      </option>
                    ))}
                  </NativeSelect>
                </div>
              </>
            ) : (
              <div className="grid gap-1.5 sm:col-span-2 lg:col-span-1">
                <input type="hidden" name="kind" value="nightly" />
                <span className="text-sm font-medium">{t("dates")}</span>
                <DateRangePicker
                  key={`${raw.checkIn}-${raw.checkOut}`}
                  mode="stay"
                  startName="checkIn"
                  endName="checkOut"
                  defaultStart={isIsoDate(raw.checkIn) ? raw.checkIn : today}
                  defaultEnd={isIsoDate(raw.checkOut) ? raw.checkOut : addDays(today, 1)}
                  today={today}
                  min={today}
                  maxNights={MAX_NIGHTS}
                  startLabel={t("checkIn")}
                  endLabel={t("checkOut")}
                />
              </div>
            )}
            <div className="grid gap-1.5">
              <Label htmlFor="adults">{t("adults")}</Label>
              <Input id="adults" name="adults" type="number" min={1} max={20} defaultValue={raw.adults} required className={tall} />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="children">{t("children")}</Label>
              <Input id="children" name="children" type="number" min={0} max={20} defaultValue={raw.children} className={tall} />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="type">{t("roomType")}</Label>
              <NativeSelect id="type" name="type" defaultValue={typeId} className={tall}>
                <option value="">{t("anyType")}</option>
                {types.ok &&
                  types.data
                    .filter((type) => !type.isArchived && (!hourly || type.hourlyPrice !== null))
                    .map((type) => (
                      <option key={type.id} value={type.id}>
                        {type.name}
                      </option>
                    ))}
              </NativeSelect>
            </div>
            <Button type="submit" className="h-14 rounded-xl px-6">
              {t("findRooms")}
            </Button>
          </form>
          {hourly && <p className="mt-3 text-xs text-muted-foreground">{t("hourlyHint", { min: minHours, max: maxHours })}</p>}
          {searchError && (
            <p role="alert" className="mt-3 text-sm text-destructive">
              {searchError}
            </p>
          )}
        </CardContent>
      </Card>

      {rooms && (
        <section className="grid gap-3" aria-labelledby="rooms-title">
          <h2 id="rooms-title" className="text-xl">
            2. {t("step2")} <span className="text-base text-muted-foreground">· {summary}</span>
          </h2>
          {rooms.length === 0 ? (
            <EmptyState icon={BedDouble} title={t("noRooms")} description={hourly ? t("noRoomsHourlyHint") : t("noRoomsHint")} />
          ) : (
            <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {rooms.map((r) => {
                const isSelected = r.roomId === roomId;
                return (
                  <li key={r.roomId}>
                    <div className={cn("grid h-full gap-3 rounded-2xl border bg-card p-4 shadow-xs", isSelected && "border-primary ring-2 ring-primary/30")}>
                      <div className="grid gap-0.5">
                        <span className="font-heading text-2xl leading-none">
                          {t("room")} {r.number}
                        </span>
                        <span className="text-sm text-muted-foreground">
                          {r.roomTypeName}
                          {r.floor !== null ? ` · ${r.floor} ${t("floor")}` : ""} · {t("upTo", { n: r.capacity })}
                        </span>
                      </div>
                      <div className="flex items-end justify-between gap-2">
                        <span className="tabular font-heading text-xl">{formatMoney(r.quote.total)}</span>
                        {isSelected ? (
                          <span className="inline-flex items-center gap-1 text-sm font-medium text-primary">
                            <Check className="size-4" aria-hidden />
                            {t("chosen")}
                          </span>
                        ) : (
                          <Link
                            href={chooseLink(r.roomId)}
                            className={buttonVariants({ variant: "outline", size: "sm" })}
                            aria-label={`${t("choose")}: ${t("room")} ${r.number}`}
                          >
                            {t("choose")}
                          </Link>
                        )}
                      </div>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      )}

      {selected && stayParams && (
        <div id="guest" className="grid scroll-mt-6 gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">3. {t("step3")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <CreateBookingForm key={selected.roomId} initialGuest={initialGuest} stay={stayParams} services={services.ok ? services.data.filter((s) => s.isActive) : []} />
            </CardContent>
          </Card>
          <Card className="content-start">
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">
                  {t("room")} {selected.number} · {t("price")}
                </h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <PriceBreakdown quote={selected.quote} />
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
