import type { Metadata } from "next";
import { Lora, Manrope } from "next/font/google";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getTranslations } from "next-intl/server";
import { QueryProvider } from "@/components/providers/query-provider";
import { siteUrl } from "@/lib/site/url";
import { cn } from "@/lib/utils";
import "./globals.css";

// Шрифты с полной кириллицей: заголовки — Lora, интерфейс — Manrope (docs/design/design-direction.md)
const lora = Lora({ subsets: ["latin", "cyrillic"], variable: "--font-lora", display: "swap" });
const manrope = Manrope({ subsets: ["latin", "cyrillic"], variable: "--font-manrope", display: "swap" });

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("App");
  return {
    metadataBase: siteUrl(),
    title: { default: t("fullName"), template: `%s · ${t("name")}` },
    description: t("description"),
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
