import { ArrowLeft, BedDouble, Check } from "lucide-react";
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
import { api } from "@/lib/api/server";
import type { AvailableRoom, ExtraService, RoomType } from "@/lib/api/types";
import { errorKey } from "@/lib/api/problem";
import { addDays, hotelToday, nightsLabel } from "@/lib/bookings/status";
import { formatMoney, formatStayDate } from "@/lib/format";
import { isIsoDate } from "@/lib/calendar";
import { cn } from "@/lib/utils";
import { MAX_NIGHTS, staySchema } from "@/lib/validation/bookings";
import { CreateBookingForm } from "../booking-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Bookings"))("newTitle") };
}

const uuid = /^[0-9a-f-]{36}$/i;
const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);

export default async function NewBookingPage({ searchParams }: PageProps<"/admin/bookings/new">) {
  const params = await searchParams;
  const today = hotelToday(hotelTimeZone);
  const raw = {
    checkIn: str(params.checkIn) ?? today,
    checkOut: str(params.checkOut) ?? addDays(str(params.checkIn) ?? today, 1),
    adults: str(params.adults) ?? "2",
    children: str(params.children) ?? "0",
  };
  const typeId = str(params.type) && uuid.test(str(params.type)!) ? str(params.type)! : "";
  const roomId = str(params.roomId) && uuid.test(str(params.roomId)!) ? str(params.roomId)! : "";
  const searched = params.checkIn !== undefined;
  const stay = staySchema.safeParse(raw);

  const [t, errors, types, services] = await Promise.all([
    getTranslations("Bookings"),
    getTranslations("Errors"),
    api<RoomType[]>("/room-types"),
    api<ExtraService[]>("/services"),
  ]);

  let rooms: AvailableRoom[] | null = null;
  let searchError: string | null = null;
  if (searched && stay.success) {
    const query = new URLSearchParams({
      checkIn: stay.data.checkIn,
      checkOut: stay.data.checkOut,
      adults: String(stay.data.adults),
      children: String(stay.data.children),
    });
    if (typeId) query.set("roomTypeId", typeId);
    const result = await api<AvailableRoom[]>(`/availability?${query}`);
    if (result.ok) rooms = result.data;
    else {
      const key = errorKey(result.problem.code ?? "unknown") as Parameters<typeof errors>[0];
      searchError = errors.has(key) ? errors(key) : (result.problem.title ?? errors("unknown"));
    }
  } else if (searched && !stay.success) {
    searchError = stay.error.issues[0]?.message ?? errors("validation_failed");
  }

  const selected = rooms?.find((r) => r.roomId === roomId) ?? null;
  const base = new URLSearchParams({ checkIn: raw.checkIn, checkOut: raw.checkOut, adults: raw.adults, children: raw.children });
  if (typeId) base.set("type", typeId);
  const chooseLink = (id: string) => {
    const next = new URLSearchParams(base);
    next.set("roomId", id);
    return `/admin/bookings/new?${next}#guest`;
  };

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
        <CardHeader>
          <CardTitle>
            <h2 className="text-xl">1. {t("step1")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <form
            method="get"
            className="grid gap-4 sm:grid-cols-2 lg:grid-cols-[minmax(0,2.4fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,1.4fr)_auto] lg:items-end"
          >
            <div className="grid gap-1.5 sm:col-span-2 lg:col-span-1">
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
            <div className="grid gap-1.5">
              <Label htmlFor="adults">{t("adults")}</Label>
              <Input id="adults" name="adults" type="number" min={1} max={20} defaultValue={raw.adults} required className="h-14 rounded-xl" />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="children">{t("children")}</Label>
              <Input id="children" name="children" type="number" min={0} max={20} defaultValue={raw.children} className="h-14 rounded-xl" />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="type">{t("roomType")}</Label>
              <NativeSelect id="type" name="type" defaultValue={typeId} className="h-14 rounded-xl">
                <option value="">{t("anyType")}</option>
                {types.ok &&
                  types.data
                    .filter((type) => !type.isArchived)
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
          {searchError && (
            <p role="alert" className="mt-3 text-sm text-destructive">
              {searchError}
            </p>
          )}
        </CardContent>
      </Card>

      {rooms && stay.success && (
        <section className="grid gap-3" aria-labelledby="rooms-title">
          <h2 id="rooms-title" className="text-xl">
            2. {t("step2")}{" "}
            <span className="text-base text-muted-foreground">
              · {formatStayDate(stay.data.checkIn)} — {formatStayDate(stay.data.checkOut)}, {nightsLabel(rooms[0]?.quote.nights.length ?? 0)}
            </span>
          </h2>
          {rooms.length === 0 ? (
            <EmptyState icon={BedDouble} title={t("noRooms")} description={t("noRoomsHint")} />
          ) : (
            <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {rooms.map((r) => {
                const isSelected = r.roomId === roomId;
                return (
                  <li key={r.roomId}>
                    <div
                      className={cn(
                        "grid h-full gap-3 rounded-2xl border bg-card p-4 shadow-xs",
                        isSelected && "border-primary ring-2 ring-primary/30",
                      )}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <div className="grid gap-0.5">
                          <span className="font-heading text-2xl leading-none">
                            {t("room")} {r.number}
                          </span>
                          <span className="text-sm text-muted-foreground">
                            {r.roomTypeName}
                            {r.floor !== null ? ` · ${r.floor} ${t("floor")}` : ""} · {t("upTo", { n: r.capacity })}
                          </span>
                        </div>
                      </div>
                      <div className="flex items-end justify-between gap-2">
                        <span className="tabular font-heading text-xl">{formatMoney(r.quote.total)}</span>
                        {isSelected ? (
                          <span className="inline-flex items-center gap-1 text-sm font-medium text-primary">
                            <Check className="size-4" aria-hidden />
                            {t("chosen")}
                          </span>
                        ) : (
                          <Link href={chooseLink(r.roomId)} className={buttonVariants({ variant: "outline", size: "sm" })} aria-label={`${t("choose")}: ${t("room")} ${r.number}`}>
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

      {selected && stay.success && (
        <div id="guest" className="grid scroll-mt-6 gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">3. {t("step3")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <CreateBookingForm
                key={selected.roomId}
                stay={{ roomId: selected.roomId, checkIn: stay.data.checkIn, checkOut: stay.data.checkOut, adults: stay.data.adults, children: stay.data.children }}
                services={services.ok ? services.data.filter((s) => s.isActive) : []}
              />
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
