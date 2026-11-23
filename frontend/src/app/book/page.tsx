import { ArrowLeft, BedDouble, CircleAlert, ImageOff, LogIn, UserPlus, Users } from "lucide-react";
import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PriceBreakdown } from "@/components/price-breakdown";
import { SiteShell } from "@/components/site/site-shell";
import { StaySearchForm } from "@/components/site/stay-search";
import { LoadingState } from "@/components/states/states";
import { buttonVariants } from "@/components/ui/button";
import { hotelTimeZone } from "@/i18n/config";
import { mediaUrl } from "@/lib/api/media";
import { errorKey } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { Photo, PriceQuote, PublicAvailability, PublicAvailableRoom, PublicRoom, PublicRoomType } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { hotelToday, nightsLabel } from "@/lib/bookings/status";
import { formatMoney, formatStayDate } from "@/lib/format";
import { getHotelInfo } from "@/lib/hotel";
import { bedTypeLabel } from "@/lib/rooms/status";
import { hotelHour, readStay, stayApiBody, stayApiQuery, stayQuery, staySummary, validateStay, type StaySearch } from "@/lib/site/stay";
import { WebBookingForm } from "./book-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Site"))("bookTitle"), robots: { index: false } };
}

/**
 * Бронирование на сайте: свободные номера по категориям на выбранное время → гость выбирает номер → цена (считает API)
 * → подтверждение после входа. Номер выбирает гость (решение 2026-11-23).
 */
export default function BookPage({ searchParams }: PageProps<"/book">) {
  return (
    <SiteShell wide>
      <Suspense fallback={<LoadingState rows={4} />}>
        <BookContent searchParams={searchParams} />
      </Suspense>
    </SiteShell>
  );
}

/** Свободный номер вместе с данными каталога (фото, кровати, описание) — каталог может не загрузиться, тогда без них. */
interface RoomChoice {
  group: PublicAvailability;
  room: PublicAvailableRoom;
  type: PublicRoomType | undefined;
  info: PublicRoom | undefined;
}

async function BookContent({ searchParams }: Pick<PageProps<"/book">, "searchParams">) {
  const params = await searchParams;
  const [t, errors, hotel] = await Promise.all([getTranslations("Site"), getTranslations("Errors"), getHotelInfo()]);
  const today = hotelToday(hotelTimeZone);
  const nowHour = hotelHour(hotelTimeZone);
  const stay = readStay(params, today, nowHour, hotel.hourlyMinHours, hotel.hourlyMaxHours);
  const summary = staySummary(stay, formatStayDate, nightsLabel);
  const apiError = (code: string | undefined, title: string | undefined) => {
    const key = errorKey(code ?? "unknown") as Parameters<typeof errors>[0];
    return errors.has(key) ? errors(key) : (title ?? errors("unknown"));
  };

  const invalid = validateStay(stay);
  const [available, catalog] = invalid
    ? [null, null]
    : await Promise.all([
        api<PublicAvailability[]>(`/public/availability?${stayApiQuery(stay)}`, { anonymous: true }),
        api<PublicRoomType[]>("/public/room-types", { anonymous: true }),
      ]);
  const types = new Map((catalog?.ok ? catalog.data : []).map((type) => [type.id, type]));
  const choiceOf = (group: PublicAvailability, room: PublicAvailableRoom): RoomChoice => {
    const type = types.get(group.roomTypeId);
    return { group, room, type, info: type?.rooms.find((r) => r.id === room.roomId) };
  };

  const groups = available?.ok ? available.data : [];
  const chosenGroup = stay.roomId ? groups.find((g) => g.rooms.some((r) => r.roomId === stay.roomId)) : undefined;
  const chosen = chosenGroup ? choiceOf(chosenGroup, chosenGroup.rooms.find((r) => r.roomId === stay.roomId)!) : undefined;

  // Пришли со страницы категории — показываем её номера; если в ней всё занято — все свободные.
  const ofType = stay.roomTypeId ? groups.filter((g) => g.roomTypeId === stay.roomTypeId) : [];
  const shown = ofType.length > 0 ? ofType : groups;
  const list = { ...stay, roomId: null };

  return (
    <div className="grid gap-8">
      <div className="grid gap-2">
        <Link href={`/?${stayQuery({ ...list, roomTypeId: null })}`} className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("toHome")}
        </Link>
        <h1 className="font-heading text-3xl sm:text-4xl">{chosen ? t("confirmTitle") : t("bookTitle")}</h1>
        <p className="tabular text-muted-foreground">
          {summary} · {t("guestsN", { n: stay.adults + stay.children })}
        </p>
      </div>

      {chosen ? (
        <Suspense fallback={<LoadingState rows={3} />}>
          <ConfirmStep stay={stay} choice={chosen} autoConfirm={hotel.autoConfirmWebBookings} />
        </Suspense>
      ) : (
        <>
          <StaySearchForm stay={list} today={today} nowHour={nowHour} minHours={hotel.hourlyMinHours} maxHours={hotel.hourlyMaxHours} basePath="/book" />
          <section aria-labelledby="results-title" className="grid gap-6">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 id="results-title" className="font-heading text-2xl">
                {t("resultsTitle")}
              </h2>
              {ofType.length > 0 && groups.length > ofType.length && (
                <Link href={`/book?${stayQuery({ ...list, roomTypeId: null })}`} className="text-sm text-primary underline-offset-4 hover:underline">
                  {t("allRooms")}
                </Link>
              )}
            </div>
            {invalid ? (
              <Notice text={invalid} />
            ) : !available?.ok ? (
              <Notice text={apiError(available?.problem.code, available?.problem.title)} />
            ) : groups.length === 0 ? (
              <Notice text={stay.roomId ? t("roomBusy") : stay.roomTypeId ? t("typeBusy") : t("noneFree")} />
            ) : (
              <>
                {stay.roomId ? <Notice text={t("roomBusy")} /> : stay.roomTypeId && ofType.length === 0 && <Notice text={t("typeBusy")} />}
                {shown.map((group) => (
                  <CategoryRooms key={group.roomTypeId} group={group} type={types.get(group.roomTypeId)} stay={list} choiceOf={choiceOf} />
                ))}
              </>
            )}
          </section>
        </>
      )}
    </div>
  );
}

/** Категория с её свободными номерами: заголовок (вместимость, удобства) и карточки номеров. */
async function CategoryRooms({
  group,
  type,
  stay,
  choiceOf,
}: {
  group: PublicAvailability;
  type: PublicRoomType | undefined;
  stay: StaySearch;
  choiceOf: (group: PublicAvailability, room: PublicAvailableRoom) => RoomChoice;
}) {
  const t = await getTranslations("Site");
  const headingId = `type-${group.roomTypeId}`;
  return (
    <section aria-labelledby={headingId} className="grid gap-3">
      <div className="grid gap-1">
        <h3 id={headingId} className="font-heading text-xl">
          <Link href={`/rooms/${group.roomTypeId}?${stayQuery({ ...stay, roomTypeId: null })}`} className="hover:underline">
            {group.roomTypeName}
          </Link>
        </h3>
        <p className="text-sm text-muted-foreground">
          {t("guestsUpTo", { n: group.capacity })} · {t("leftN", { n: group.availableCount })}
          {type && type.amenities.length > 0 && ` · ${type.amenities.slice(0, 5).map((a) => a.name).join(" · ")}`}
        </p>
      </div>
      <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {group.rooms.map((room) => (
          <li key={room.roomId}>
            <RoomCard choice={choiceOf(group, room)} stay={stay} />
          </li>
        ))}
      </ul>
    </section>
  );
}

async function RoomCard({ choice, stay }: { choice: RoomChoice; stay: StaySearch }) {
  const t = await getTranslations("Site");
  const { group, room, type, info } = choice;
  const href = `/book?${stayQuery({ ...stay, roomId: room.roomId })}`;
  return (
    <article className="flex h-full flex-col overflow-hidden rounded-2xl border bg-card shadow-sm" aria-labelledby={`room-${room.roomId}`}>
      <Cover photo={info?.photos[0] ?? type?.photos[0]} sizes="(min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw" />
      <div className="grid flex-1 content-start gap-2 p-4">
        <h4 id={`room-${room.roomId}`} className="font-heading text-lg">
          {t("roomN", { n: room.number })}
          {room.floor !== null && <span className="text-sm font-normal text-muted-foreground"> · {t("floorN", { n: room.floor })}</span>}
        </h4>
        <RoomFacts capacity={room.capacity} info={info} />
        {info?.description && <p className="line-clamp-2 text-sm text-muted-foreground">{info.description}</p>}
      </div>
      <div className="flex items-end justify-between gap-3 border-t p-4">
        <p>
          <span className="block text-sm text-muted-foreground">{group.kind === "Hourly" ? t("forHours", { n: group.units }) : t("forNights", { n: nightsLabel(group.units) })}</span>
          <span className="tabular text-xl font-semibold">{formatMoney(room.total)}</span>
        </p>
        <Link href={href} className={buttonVariants({ className: "h-11 px-5" })} aria-describedby={`room-${room.roomId}`}>
          {t("choose")}
        </Link>
      </div>
    </article>
  );
}

function Cover({ photo, sizes }: { photo: Photo | undefined; sizes: string }) {
  return (
    <div className="relative aspect-[4/3] bg-secondary">
      {photo ? (
        <Image src={mediaUrl(photo.fileId)} alt="" fill unoptimized sizes={sizes} className="object-cover" />
      ) : (
        <div className="grid h-full place-items-center text-muted-foreground">
          <ImageOff className="size-8" aria-hidden />
        </div>
      )}
    </div>
  );
}

async function RoomFacts({ capacity, info }: { capacity: number; info: PublicRoom | undefined }) {
  const t = await getTranslations("Site");
  return (
    <p className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
      <span className="inline-flex items-center gap-1.5">
        <Users className="size-4" aria-hidden />
        {t("guestsUpTo", { n: capacity })}
      </span>
      {info && (
        <span className="inline-flex items-center gap-1.5">
          <BedDouble className="size-4" aria-hidden />
          {t("beds", { n: info.bedsCount })} · {bedTypeLabel[info.bedType as keyof typeof bedTypeLabel] ?? info.bedType}
        </span>
      )}
    </p>
  );
}

function Notice({ text }: { text: string }) {
  return (
    <p role="status" className="flex items-start gap-2 rounded-2xl border bg-secondary/40 p-4 text-sm">
      <CircleAlert className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
      {text}
    </p>
  );
}

/** Шаг 2: цена выбранного номера (считает API) и подтверждение — только вошедшему гостю. */
async function ConfirmStep({ stay, choice, autoConfirm }: { stay: StaySearch; choice: RoomChoice; autoConfirm: boolean }) {
  const [t, user, quote] = await Promise.all([
    getTranslations("Site"),
    getCurrentUser(),
    api<PriceQuote>("/public/bookings/quote", { method: "POST", anonymous: true, body: { ...stayApiBody(stay), roomId: choice.room.roomId } }),
  ]);
  const back = `/book?${stayQuery({ ...stay, roomId: null })}`;
  const here = `/book?${stayQuery(stay)}`;
  const isGuest = user?.roles.includes("Guest") ?? false;
  const { group, room, type, info } = choice;

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <section aria-labelledby="choice-title" className="grid content-start overflow-hidden rounded-2xl border bg-card">
        <Cover photo={info?.photos[0] ?? type?.photos[0]} sizes="(min-width: 1024px) 60vw, 100vw" />
        <div className="grid gap-4 p-5">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <div className="grid gap-0.5">
              <h2 id="choice-title" className="font-heading text-2xl">
                {t("roomN", { n: room.number })}
                {room.floor !== null && <span className="text-base font-normal text-muted-foreground"> · {t("floorN", { n: room.floor })}</span>}
              </h2>
              <p className="text-sm text-muted-foreground">{group.roomTypeName}</p>
            </div>
            <Link href={back} className="text-sm text-primary underline-offset-4 hover:underline">
              {t("changeChoice")}
            </Link>
          </div>
          <RoomFacts capacity={room.capacity} info={info} />
          {info?.description && <p className="text-sm text-muted-foreground">{info.description}</p>}
          {quote.ok ? <PriceBreakdown quote={quote.data} /> : <p className="text-sm text-muted-foreground">{t("roomBusy")}</p>}
          <p className="text-xs text-muted-foreground">{t("payAtHotel")}</p>
        </div>
      </section>

      <section aria-labelledby="who-title" className="grid content-start gap-4 rounded-2xl border bg-card p-5">
        <h2 id="who-title" className="font-heading text-xl">
          {t("yourBooking")}
        </h2>
        {!quote.ok ? (
          <Link href={back} className={buttonVariants({ variant: "outline" })}>
            {t("changeChoice")}
          </Link>
        ) : isGuest ? (
          <>
            <p className="text-sm text-muted-foreground">{t("bookingAs", { name: user!.fullName })}</p>
            <WebBookingForm stay={stay} autoConfirm={autoConfirm} />
          </>
        ) : user ? (
          <p className="text-sm text-muted-foreground">{t("staffCannotBook")}</p>
        ) : (
          <>
            <p className="text-sm text-muted-foreground">{t("loginToBook")}</p>
            <div className="grid gap-2 sm:grid-cols-2">
              <Link href={`/login?next=${encodeURIComponent(here)}`} className={buttonVariants({ className: "h-11" })}>
                <LogIn aria-hidden />
                {t("loginAndBook")}
              </Link>
              <Link href={`/register?next=${encodeURIComponent(here)}`} className={buttonVariants({ variant: "outline", className: "h-11" })}>
                <UserPlus aria-hidden />
                {t("register")}
              </Link>
            </div>
            <p className="text-xs text-muted-foreground">
              {t("total")}: <span className="tabular font-medium">{formatMoney(quote.data.total)}</span>
            </p>
          </>
        )}
      </section>
    </div>
  );
}
