import { CalendarCheck, Clock3, ShieldCheck } from "lucide-react";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { RoomTypeCard } from "@/components/site/room-type-card";
import { SiteShell } from "@/components/site/site-shell";
import { StaySearchForm } from "@/components/site/stay-search";
import { LoadingState } from "@/components/states/states";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { PublicHotelInfo, PublicRoomType } from "@/lib/api/types";
import { hotelToday } from "@/lib/bookings/status";
import { OG_IMAGE } from "@/lib/brand-image";
import { getHotelInfo } from "@/lib/hotel";
import { hotelHour, readStay, stayQuery } from "@/lib/site/stay";
import { requestNow } from "@/lib/time/request-now";

export async function generateMetadata(): Promise<Metadata> {
  const [hotel, t] = await Promise.all([getHotelInfo(), getTranslations("Site")]);
  const description = hotel.description?.slice(0, 160) ?? t("metaDescription", { name: hotel.name });
  return {
    title: { absolute: t("metaTitle", { name: hotel.name }) },
    description,
    alternates: { canonical: "/" },
    openGraph: {
      title: hotel.name,
      description,
      type: "website",
      locale: "ru_RU",
      siteName: hotel.name,
      images: [{ ...OG_IMAGE, alt: hotel.name }],
    },
  };
}

/** Разметка schema.org для поисковиков: гостиница, адрес, контакты, время заезда/выезда. */
function HotelJsonLd({ hotel }: { hotel: PublicHotelInfo }) {
  const data = {
    "@context": "https://schema.org",
    "@type": "Hotel",
    name: hotel.name,
    description: hotel.description ?? undefined,
    address: hotel.address ?? undefined,
    telephone: hotel.phone ?? undefined,
    email: hotel.email ?? undefined,
    checkinTime: hotel.checkInTime,
    checkoutTime: hotel.checkOutTime,
    priceRange: "KGS",
  };
  // JSON.stringify экранирует кавычки; «<» заменяем, чтобы текст из настроек не закрыл тег script.
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(data).replace(/</g, "\\u003c") }} />;
}

export default function Home({ searchParams }: PageProps<"/">) {
  return (
    <SiteShell wide>
      <Suspense fallback={<LoadingState rows={4} />}>
        <HomeContent searchParams={searchParams} />
      </Suspense>
    </SiteShell>
  );
}

async function HomeContent({ searchParams }: Pick<PageProps<"/">, "searchParams">) {
  const now = await requestNow();
  const params = await searchParams;
  const [t, hotel, types] = await Promise.all([getTranslations("Site"), getHotelInfo(), api<PublicRoomType[]>("/public/room-types", { anonymous: true })]);
  const today = hotelToday(hotelTimeZone, now);
  const nowHour = hotelHour(hotelTimeZone, now);
  const stay = readStay(params, today, nowHour, hotel.hourlyMinHours, hotel.hourlyMaxHours);
  const cancelHint = hotel.hasLatePenalties
    ? t("ruleCancelHours", { n: Math.round(hotel.hourlyFreeCancelMinutes / 60) })
    : t("ruleCancelFree");

  return (
    <div className="grid gap-12 sm:gap-16">
      <HotelJsonLd hotel={hotel} />
      {params.accountDeleted === "1" && (
        <p role="status" className="rounded-2xl border bg-secondary/40 p-4 text-sm">
          {t("accountDeleted")}
        </p>
      )}
      <section className="grid gap-6" aria-labelledby="hero-title">
        <div className="grid max-w-3xl gap-3">
          <h1 id="hero-title" className="font-heading text-4xl leading-tight sm:text-5xl">
            {hotel.name}
          </h1>
          <p className="text-lg text-muted-foreground">{hotel.description ?? t("heroText")}</p>
        </div>
        <StaySearchForm stay={stay} today={today} nowHour={nowHour} minHours={hotel.hourlyMinHours} maxHours={hotel.hourlyMaxHours} basePath="/" />
        <ul className="grid gap-3 text-sm sm:grid-cols-3">
          <li className="flex items-start gap-2.5">
            <Clock3 className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
            <span>{t("ruleHours", { min: hotel.hourlyMinHours })}</span>
          </li>
          <li className="flex items-start gap-2.5">
            <CalendarCheck className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
            <span>{t("ruleNights", { checkIn: hotel.checkInTime, checkOut: hotel.checkOutTime })}</span>
          </li>
          <li className="flex items-start gap-2.5">
            <ShieldCheck className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
            <span>{cancelHint}</span>
          </li>
        </ul>
      </section>

      <section id="rooms" className="grid scroll-mt-24 gap-6" aria-labelledby="rooms-title">
        <div className="grid gap-1">
          <h2 id="rooms-title" className="font-heading text-3xl">
            {t("roomsTitle")}
          </h2>
          <p className="text-muted-foreground">{t("roomsText")}</p>
        </div>
        {!types.ok ? (
          <p className="text-muted-foreground">{t("roomsError")}</p>
        ) : types.data.length === 0 ? (
          <p className="text-muted-foreground">{t("roomsEmpty")}</p>
        ) : (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {types.data.map((type) => (
              <RoomTypeCard key={type.id} type={type} href={`/rooms/${type.id}?${stayQuery({ ...stay, roomTypeId: null })}`} />
            ))}
          </div>
        )}
      </section>

      {hotel.rules && (
        <section className="grid max-w-3xl gap-3" aria-labelledby="rules-title">
          <h2 id="rules-title" className="font-heading text-2xl">
            {t("rulesTitle")}
          </h2>
          <p className="whitespace-pre-line text-muted-foreground">{hotel.rules}</p>
        </section>
      )}
    </div>
  );
}
