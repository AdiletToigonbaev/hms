"use client";

import { useTranslations } from "next-intl";
import { CrashScreen, errorDigest } from "@/components/states/crash-screen";

/** Сбой страницы (например, API недоступен при перезапуске): свой экран вместо стандартного Next.js. */
export default function Error({ error, retry }: { error: unknown; retry: () => void }) {
  const t = useTranslations("Common");
  const digest = errorDigest(error);
  return (
    <CrashScreen
      onRetry={retry}
      text={{
        title: t("error"),
        hint: t("errorHint"),
        retry: t("retry"),
        home: t("backHome"),
        code: digest ? t("errorCode", { code: digest }) : undefined,
      }}
    />
  );
}
