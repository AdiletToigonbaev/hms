import { Mail, MapPin, MessageCircle, Phone } from "lucide-react";
import { cookies } from "next/headers";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense, type ReactNode } from "react";
import { Brand } from "@/components/brand";
import { buttonVariants } from "@/components/ui/button";
import { ACCESS_COOKIE, REFRESH_COOKIE } from "@/lib/auth/cookies";
import { readAccessToken } from "@/lib/auth/jwt";
import { homeFor } from "@/lib/auth/routes";
import { getHotelInfo } from "@/lib/hotel";
import { twoGisLink, whatsAppLink } from "@/lib/site/contacts";
import { cn } from "@/lib/utils";

/** Вход или кабинет — по cookie сессии (поэтому внутри Suspense). */
async function SessionLink() {
  const [jar, t] = await Promise.all([cookies(), getTranslations("Site")]);
  const claims = readAccessToken(jar.get(ACCESS_COOKIE)?.value);
  if (claims || jar.has(REFRESH_COOKIE)) {
    return (
      <Link href={claims ? homeFor(claims.roles) : "/account"} className={buttonVariants({ size: "sm" })}>
        {t("cabinet")}
      </Link>
    );
  }
  return (
    <Link href="/login" className={buttonVariants({ size: "sm", variant: "outline" })}>
      {t("login")}
    </Link>
  );
}

/**
 * Каркас публичного сайта: шапка с названием гостиницы (из настроек), меню и входом; подвал с контактами.
 * Название не зашито в код — компонент Brand (docs/design/design-direction.md).
 * Шапка и подвал читают данные запроса — поэтому внутри Suspense (Cache Components).
 */
export function SiteShell({ children, wide }: { children: ReactNode; wide?: boolean }) {
  const container = cn("mx-auto w-full px-4 sm:px-6", wide ? "max-w-6xl" : "max-w-5xl");
  return (
    <div className="flex min-h-dvh flex-col bg-background">
      <Suspense fallback={<div className="h-16 border-b" />}>
        <SiteHeader container={container} />
      </Suspense>
      <main id="main" className={cn(container, "flex-1 py-8 sm:py-12")}>
        {children}
      </main>
      <Suspense fallback={<div className="h-40 border-t bg-secondary/40" />}>
        <SiteFooter container={container} />
      </Suspense>
    </div>
  );
}

async function SiteHeader({ container }: { container: string }) {
  const [t, hotel] = await Promise.all([getTranslations("Site"), getHotelInfo()]);
  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-lg focus:bg-card focus:px-3 focus:py-2"
      >
        {t("skip")}
      </a>
      <header className="sticky top-0 z-30 border-b bg-background/90 backdrop-blur">
        <div className={cn(container, "flex h-16 items-center justify-between gap-3")}>
          <Link href="/" className="min-w-0" aria-label={t("home", { name: hotel.name })}>
            <Brand name={hotel.name} />
          </Link>
          <nav aria-label={t("menu")} className="flex items-center gap-1 sm:gap-2">
            <Link href="/#rooms" className="hidden rounded-lg px-3 py-2 text-sm font-medium text-muted-foreground hover:text-foreground sm:inline">
              {t("rooms")}
            </Link>
            <Link href="/#contacts" className="hidden rounded-lg px-3 py-2 text-sm font-medium text-muted-foreground hover:text-foreground sm:inline">
              {t("contacts")}
            </Link>
            <SessionLink />
          </nav>
        </div>
      </header>
    </>
  );
}

async function SiteFooter({ container }: { container: string }) {
  const [t, hotel] = await Promise.all([getTranslations("Site"), getHotelInfo()]);
  // Кнопки — только если владелец указал WhatsApp и ссылку на 2ГИС в настройках.
  const whatsApp = whatsAppLink(hotel.whatsAppPhone, t("whatsAppGreeting", { name: hotel.name }));
  const twoGis = twoGisLink(hotel.twoGisUrl);
  const external = { target: "_blank", rel: "noopener noreferrer" } as const;
  return (
    <footer id="contacts" className="border-t bg-secondary/40">
      <div className={cn(container, "grid gap-6 py-10 sm:grid-cols-[minmax(0,1fr)_auto]")}>
        <div className="grid content-start gap-3">
          <Brand name={hotel.name} />
          <ul className="grid gap-2 text-sm text-muted-foreground">
            {hotel.address && (
              <li className="flex items-start gap-2">
                <MapPin className="mt-0.5 size-4 shrink-0" aria-hidden />
                {twoGis ? (
                  <a href={twoGis} {...external} className="hover:text-foreground">
                    {hotel.address}
                  </a>
                ) : (
                  hotel.address
                )}
              </li>
            )}
            {hotel.phone && (
              <li className="flex items-center gap-2">
                <Phone className="size-4 shrink-0" aria-hidden />
                <a href={`tel:${hotel.phone.replace(/[^\d+]/g, "")}`} className="tabular hover:text-foreground">
                  {hotel.phone}
                </a>
              </li>
            )}
            {hotel.email && (
              <li className="flex items-center gap-2">
                <Mail className="size-4 shrink-0" aria-hidden />
                <a href={`mailto:${hotel.email}`} className="hover:text-foreground">
                  {hotel.email}
                </a>
              </li>
            )}
          </ul>
          {(whatsApp || twoGis) && (
            <div className="flex flex-wrap gap-2 pt-1">
              {whatsApp && (
                <a href={whatsApp} {...external} className={cn(buttonVariants({ variant: "outline" }), "h-10 px-4")}>
                  <MessageCircle aria-hidden />
                  {t("writeWhatsApp")}
                </a>
              )}
              {twoGis && (
                <a href={twoGis} {...external} className={cn(buttonVariants({ variant: "outline" }), "h-10 px-4")}>
                  <MapPin aria-hidden />
                  {t("open2Gis")}
                </a>
              )}
            </div>
          )}
        </div>
        <dl className="grid content-start gap-1 text-sm text-muted-foreground sm:text-right">
          <div>
            <dt className="inline">{t("checkIn")}: </dt>
            <dd className="inline tabular">{hotel.checkInTime}</dd>
          </div>
          <div>
            <dt className="inline">{t("checkOut")}: </dt>
            <dd className="inline tabular">{hotel.checkOutTime}</dd>
          </div>
          <div>
            <dt className="inline">{t("hourlyFrom")}: </dt>
            <dd className="inline">{t("hoursN", { n: hotel.hourlyMinHours })}</dd>
          </div>
        </dl>
      </div>
    </footer>
  );
}
