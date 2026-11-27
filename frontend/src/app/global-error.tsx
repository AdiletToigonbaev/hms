"use client";

import { Lora, Manrope } from "next/font/google";
import { CrashScreen, errorDigest } from "@/components/states/crash-screen";
import { cn } from "@/lib/utils";
import messages from "../../messages/ru.json";
import "./globals.css";

// Сбой корневого layout: этот файл заменяет его целиком — свои <html>/<body>, стили и шрифты,
// без провайдера next-intl (тексты — напрямую из messages/ru.json).
const lora = Lora({ subsets: ["latin", "cyrillic"], variable: "--font-lora", display: "swap" });
const manrope = Manrope({ subsets: ["latin", "cyrillic"], variable: "--font-manrope", display: "swap" });

export default function GlobalError({ error, retry }: { error: unknown; retry: () => void }) {
  const t = messages.Common;
  const digest = errorDigest(error);
  return (
    <html lang="ru" className={cn(lora.variable, manrope.variable)}>
      <body className="min-h-dvh bg-background text-foreground antialiased">
        <title>{t.error}</title>
        <CrashScreen
          onRetry={retry}
          text={{
            title: t.error,
            hint: t.errorHint,
            retry: t.retry,
            home: t.backHome,
            code: digest ? t.errorCode.replace("{code}", digest) : undefined,
          }}
        />
      </body>
    </html>
  );
}
