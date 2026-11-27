import { ArrowLeft, BedDouble, Check, Users } from "lucide-react";
import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { SiteShell } from "@/components/site/site-shell";
import { StaySearchForm } from "@/components/site/stay-search";
import { LoadingState } from "@/components/states/states";
import { hotelTimeZone } from "@/i18n/config";
import { mediaUrl } from "@/lib/api/media";
import { api } from "@/lib/api/server";
import type { PublicRoomType } from "@/lib/api/types";
import { hotelToday } from "@/lib/bookings/status";
import { OG_IMAGE } from "@/lib/brand-image";
import { formatMoney } from "@/lib/format";
import { getHotelInfo } from "@/lib/hotel";
import { bedTypeLabel } from "@/lib/rooms/status";
import { hotelHour, readStay } from "@/lib/site/stay";
import { requestNow } from "@/lib/time/request-now";

const uuid = /^[0-9a-f-]{36}$/i;

async function loadType(id: string) {
  if (!uuid.test(id)) return null;
  const result = await api<PublicRoomType>(`/public/room-types/${id}`, { anonymous: true });
  return result.ok ? result.data : null;
}

export async function generateMetadata({ params }: PageProps<"/rooms/[id]">): Promise<Metadata> {
  const { id } = await params;
  const [type, hotel] = await Promise.all([loadType(id), getHotelInfo()]);
  if (!type) return {};
  const description = type.description?.slice(0, 160) ?? `${type.name} — ${hotel.name}`;
  return {
    title: type.name,
    description,
    alternates: { canonical: `/rooms/${type.id}` },
    openGraph: {
      title: `${type.name} · ${hotel.name}`,
      description,
      siteName: hotel.name,
      // Фото категории; без фото — общая картинка гостиницы.
      images: type.photos[0] ? [mediaUrl(type.photos[0].fileId)] : [{ ...OG_IMAGE, alt: hotel.name }],
    },
  };
}

export default function RoomTypePage({ params, searchParams }: PageProps<"/rooms/[id]">) {
  return (
    <SiteShell wide>
      <Suspense fallback={<LoadingState rows={4} />}>
        <RoomTypeContent params={params} searchParams={searchParams} />
      </Suspense>
    </SiteShell>
  );
}

async function RoomTypeContent({ params, searchParams }: PageProps<"/rooms/[id]">) {
  const now = await requestNow();
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const [t, type, hotel] = await Promise.all([getTranslations("Site"), loadType(id), getHotelInfo()]);
  if (!type) notFound();
  const today = hotelToday(hotelTimeZone, now);
  const nowHour = hotelHour(hotelTimeZone, now);
  const stay = { ...readStay(query, today, nowHour, hotel.hourlyMinHours, hotel.hourlyMaxHours), roomTypeId: type.id };
  // Только почасовая продажа недоступна — сразу посуточный поиск.
  if (type.hourlyPriceFrom === null) stay.hourly = false;
  const [cover, ...rest] = type.photos;

  return (
    <div className="grid gap-8">
      <Link href="/#rooms" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden />
        {t("allRooms")}
      </Link>

      <div className="grid gap-2">
        <h1 className="font-heading text-4xl">{type.name}</h1>
        <p className="flex flex-wrap gap-x-5 gap-y-1 text-muted-foreground">
          <span className="inline-flex items-center gap-1.5">
            <Users className="size-4" aria-hidden />
            {t("guestsUpTo", { n: type.capacity })}
          </span>
          <span className="inline-flex items-center gap-1.5">
            <BedDouble className="size-4" aria-hidden />
            {t("beds", { n: type.bedsCount })} · {bedTypeLabel[type.bedType as keyof typeof bedTypeLabel] ?? type.bedType}
          </span>
        </p>
      </div>

      {cover && (
        <section aria-label={t("gallery")} className="grid gap-3 sm:grid-cols-[2fr_1fr]">
          <div className="relative aspect-[4/3] overflow-hidden rounded-2xl bg-secondary sm:row-span-2">
            <Image src={mediaUrl(cover.fileId)} alt={t("photoOf", { name: type.name })} fill unoptimized priority sizes="(min-width: 640px) 66vw, 100vw" className="object-cover" />
          </div>
          {rest.slice(0, 4).map((photo, i) => (
            <div key={photo.id} className="relative aspect-[4/3] overflow-hidden rounded-2xl bg-secondary">
              <Image src={mediaUrl(photo.fileId)} alt={t("photoN", { name: type.name, n: i + 2 })} fill unoptimized sizes="(min-width: 640px) 33vw, 100vw" className="object-cover" />
            </div>
          ))}
        </section>
      )}

      <div className="grid gap-8 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          {type.description && <p className="whitespace-pre-line text-lg leading-relaxed">{type.description}</p>}
          {type.amenities.length > 0 && (
            <section aria-labelledby="amenities-title" className="grid gap-3">
              <h2 id="amenities-title" className="font-heading text-2xl">
                {t("amenities")}
              </h2>
              <ul className="grid gap-2 sm:grid-cols-2">
                {type.amenities.map((a) => (
                  <li key={a.id} className="flex items-center gap-2">
                    <Check className="size-4 text-primary" aria-hidden />
                    {a.name}
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>
        <aside className="grid content-start gap-3 rounded-2xl border bg-secondary/40 p-5" aria-label={t("prices")}>
          {type.hourlyPriceFrom !== null && (
            <p>
              <span className="text-muted-foreground">{t("from")} </span>
              <span className="tabular text-2xl font-semibold">{formatMoney(type.hourlyPriceFrom)}</span>
              <span className="text-muted-foreground"> {t("perHour")}</span>
              <span className="block text-sm text-muted-foreground">{t("minHours", { n: hotel.hourlyMinHours })}</span>
            </p>
          )}
          <p>
            <span className="text-muted-foreground">{t("from")} </span>
            <span className="tabular text-2xl font-semibold">{formatMoney(type.priceFrom)}</span>
            <span className="text-muted-foreground"> {t("perNight")}</span>
            <span className="block text-sm text-muted-foreground">{t("ruleNights", { checkIn: hotel.checkInTime, checkOut: hotel.checkOutTime })}</span>
          </p>
          <p className="text-xs text-muted-foreground">{t("priceNote")}</p>
        </aside>
      </div>

      <section aria-labelledby="book-title" className="grid scroll-mt-24 gap-3" id="book">
        <h2 id="book-title" className="font-heading text-2xl">
          {t("bookThis")}
        </h2>
        <StaySearchForm stay={stay} today={today} nowHour={nowHour} minHours={hotel.hourlyMinHours} maxHours={hotel.hourlyMaxHours} basePath={`/rooms/${type.id}`} />
      </section>
    </div>
  );
}
