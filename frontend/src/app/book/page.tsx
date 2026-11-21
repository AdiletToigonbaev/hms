import { ArrowLeft, CircleAlert, LogIn, UserPlus, Users } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PriceBreakdown } from "@/components/price-breakdown";
import { SiteShell } from "@/components/site/site-shell";
import { StaySearchForm } from "@/components/site/stay-search";
import { LoadingState } from "@/components/states/states";
import { buttonVariants } from "@/components/ui/button";
import { hotelTimeZone } from "@/i18n/config";
import { errorKey } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { PriceQuote, PublicAvailability } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { hotelToday, nightsLabel } from "@/lib/bookings/status";
import { formatMoney, formatStayDate } from "@/lib/format";
import { getHotelInfo } from "@/lib/hotel";
import { hotelHour, readStay, stayApiBody, stayApiQuery, stayQuery, staySummary, validateStay } from "@/lib/site/stay";
import { WebBookingForm } from "./book-form";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Site"))("bookTitle"), robots: { index: false } };
}

/** Бронирование на сайте: свободные категории на выбранное время → цена → подтверждение (после входа гостя). */
export default function BookPage({ searchParams }: PageProps<"/book">) {
  return (
    <SiteShell wide>
      <Suspense fallback={<LoadingState rows={4} />}>
        <BookContent searchParams={searchParams} />
      </Suspense>
    </SiteShell>
  );
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
  const available = invalid ? null : await api<PublicAvailability[]>(`/public/availability?${stayApiQuery(stay)}`, { anonymous: true });
  const chosen = stay.roomTypeId && available?.ok ? available.data.find((a) => a.roomTypeId === stay.roomTypeId) : undefined;

  return (
    <div className="grid gap-8">
      <div className="grid gap-2">
        <Link href={`/?${stayQuery({ ...stay, roomTypeId: null })}`} className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
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
          <ConfirmStep stay={{ ...stay, roomTypeId: chosen.roomTypeId }} choice={chosen} autoConfirm={hotel.autoConfirmWebBookings} />
        </Suspense>
      ) : (
        <>
          <StaySearchForm stay={stay} today={today} nowHour={nowHour} minHours={hotel.hourlyMinHours} maxHours={hotel.hourlyMaxHours} basePath="/book" />
          <section aria-labelledby="results-title" className="grid gap-4">
            <h2 id="results-title" className="font-heading text-2xl">
              {t("resultsTitle")}
            </h2>
            {invalid ? (
              <Notice text={invalid} />
            ) : !available?.ok ? (
              <Notice text={apiError(available?.problem.code, available?.problem.title)} />
            ) : available.data.length === 0 ? (
              <Notice text={stay.roomTypeId ? t("typeBusy") : t("noneFree")} />
            ) : (
              <>
              {stay.roomTypeId && <Notice text={t("typeBusy")} />}
              <ul className="grid gap-3">
                {available.data.map((a) => (
                  <li key={a.roomTypeId} className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border bg-card p-4 sm:p-5">
                    <div className="grid gap-1">
                      <Link href={`/rooms/${a.roomTypeId}?${stayQuery({ ...stay, roomTypeId: null })}`} className="font-heading text-xl hover:underline">
                        {a.roomTypeName}
                      </Link>
                      <p className="inline-flex items-center gap-1.5 text-sm text-muted-foreground">
                        <Users className="size-4" aria-hidden />
                        {t("guestsUpTo", { n: a.capacity })} · {t("leftN", { n: a.availableCount })}
                      </p>
                    </div>
                    <div className="flex items-center gap-4">
                      <p className="text-right">
                        <span className="block text-sm text-muted-foreground">{a.kind === "Hourly" ? t("forHours", { n: a.units }) : t("forNights", { n: nightsLabel(a.units) })}</span>
                        <span className="tabular text-xl font-semibold">
                          {t("from")} {formatMoney(a.totalFrom)}
                        </span>
                      </p>
                      <Link href={`/book?${stayQuery({ ...stay, roomTypeId: a.roomTypeId })}`} className={buttonVariants({ className: "h-11 px-5" })}>
                        {t("choose")}
                      </Link>
                    </div>
                  </li>
                ))}
              </ul>
              </>
            )}
          </section>
        </>
      )}
    </div>
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

/** Шаг 2: цена по выбранной категории (считает API) и подтверждение — только вошедшему гостю. */
async function ConfirmStep({ stay, choice, autoConfirm }: { stay: ReturnType<typeof readStay>; choice: PublicAvailability; autoConfirm: boolean }) {
  const [t, user, quote] = await Promise.all([
    getTranslations("Site"),
    getCurrentUser(),
    api<PriceQuote>("/public/bookings/quote", { method: "POST", anonymous: true, body: { ...stayApiBody(stay), roomTypeId: choice.roomTypeId } }),
  ]);
  const back = `/book?${stayQuery({ ...stay, roomTypeId: null })}`;
  const here = `/book?${stayQuery(stay)}`;
  const isGuest = user?.roles.includes("Guest") ?? false;

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <section aria-labelledby="choice-title" className="grid content-start gap-4 rounded-2xl border bg-card p-5">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 id="choice-title" className="font-heading text-2xl">
            {choice.roomTypeName}
          </h2>
          <Link href={back} className="text-sm text-primary underline-offset-4 hover:underline">
            {t("changeChoice")}
          </Link>
        </div>
        {quote.ok ? <PriceBreakdown quote={quote.data} /> : <p className="text-sm text-muted-foreground">{t("typeBusy")}</p>}
        <p className="text-xs text-muted-foreground">{t("payAtHotel")}</p>
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
