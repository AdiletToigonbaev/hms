import { BedDouble, ImageOff, Users } from "lucide-react";
import Image from "next/image";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { mediaUrl } from "@/lib/api/media";
import type { PublicRoomType } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { bedTypeLabel } from "@/lib/rooms/status";

/** Карточка категории в каталоге сайта: фото на первом плане, вместимость, удобства, цена «от» за час и за ночь. */
export async function RoomTypeCard({ type, href }: { type: PublicRoomType; href: string }) {
  const t = await getTranslations("Site");
  const cover = type.photos[0];
  return (
    <article className="group relative flex flex-col overflow-hidden rounded-2xl border bg-card shadow-sm transition hover:shadow-md" aria-labelledby={`type-${type.id}`}>
      <div className="relative aspect-[4/3] bg-secondary">
        {cover ? (
          <Image src={mediaUrl(cover.fileId)} alt={t("photoOf", { name: type.name })} fill unoptimized sizes="(min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw" className="object-cover transition duration-500 group-hover:scale-[1.02]" />
        ) : (
          <div className="grid h-full place-items-center text-muted-foreground">
            <ImageOff className="size-8" aria-hidden />
          </div>
        )}
      </div>
      <div className="grid flex-1 content-start gap-3 p-4">
        <h3 id={`type-${type.id}`} className="font-heading text-xl">
          <Link href={href} className="after:absolute after:inset-0 focus-visible:outline-none">
            {type.name}
          </Link>
        </h3>
        <p className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
          <span className="inline-flex items-center gap-1.5">
            <Users className="size-4" aria-hidden />
            {t("guestsUpTo", { n: type.capacity })}
          </span>
          <span className="inline-flex items-center gap-1.5">
            <BedDouble className="size-4" aria-hidden />
            {bedTypeLabel[type.bedType as keyof typeof bedTypeLabel] ?? type.bedType}
          </span>
        </p>
        {type.amenities.length > 0 && (
          <p className="line-clamp-1 text-sm text-muted-foreground">{type.amenities.slice(0, 5).map((a) => a.name).join(" · ")}</p>
        )}
        <p className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          {type.hourlyPriceFrom !== null && (
            <span>
              <span className="text-sm text-muted-foreground">{t("from")} </span>
              <span className="tabular text-lg font-semibold">{formatMoney(type.hourlyPriceFrom)}</span>
              <span className="text-sm text-muted-foreground"> {t("perHour")}</span>
            </span>
          )}
          <span>
            <span className="text-sm text-muted-foreground">{t("from")} </span>
            <span className="tabular text-lg font-semibold">{formatMoney(type.priceFrom)}</span>
            <span className="text-sm text-muted-foreground"> {t("perNight")}</span>
          </span>
        </p>
      </div>
    </article>
  );
}
