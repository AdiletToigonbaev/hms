import type { Metadata } from "next";
import { Lora, Manrope } from "next/font/google";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getTranslations } from "next-intl/server";
import { QueryProvider } from "@/components/providers/query-provider";
import { OG_IMAGE } from "@/lib/brand-image";
import { getCachedHotelName } from "@/lib/hotel";
import { siteUrl } from "@/lib/site/url";
import { siteViewport } from "@/lib/theme-color";
import { cn } from "@/lib/utils";
import "./globals.css";

// Шрифты с полной кириллицей: заголовки — Lora, интерфейс — Manrope (docs/design/design-direction.md)
const lora = Lora({ subsets: ["latin", "cyrillic"], variable: "--font-lora", display: "swap" });
const manrope = Manrope({ subsets: ["latin", "cyrillic"], variable: "--font-manrope", display: "swap" });

// Панель браузера на телефоне — в тон шапке сайта (кабинеты задают свой цвет).
export const viewport = siteViewport;

export async function generateMetadata(): Promise<Metadata> {
  const [t, name] = await Promise.all([getTranslations("App"), getCachedHotelName()]);
  return {
    metadataBase: siteUrl(),
    // Во вкладке и в поиске — название гостиницы из настроек («Вход · <название>»), а не название системы.
    title: { default: name, template: `%s · ${name}` },
    description: t("description"),
    openGraph: { siteName: name, images: [{ ...OG_IMAGE, alt: name }], locale: "ru_RU" },
    twitter: { card: "summary_large_image" },
  };
}

export default async function RootLayout({ children }: LayoutProps<"/">) {
  const locale = await getLocale();
  return (
    <html lang={locale} className={cn(lora.variable, manrope.variable)}>
      <body className="min-h-dvh bg-background text-foreground antialiased">
        <NextIntlClientProvider>
          <QueryProvider>{children}</QueryProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
