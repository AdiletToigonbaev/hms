import { CircleCheck, CircleX } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { apiInternalUrl } from "@/lib/api/config";

async function isApiReady(): Promise<boolean> {
  try {
    const response = await fetch(`${apiInternalUrl()}/health/ready`, {
      cache: "no-store",
      signal: AbortSignal.timeout(3000),
    });
    return response.ok;
  } catch {
    return false;
  }
}

/** Серверный индикатор доступности API и БД (рендерится динамически внутри Suspense). */
export async function ApiStatus() {
  const [t, ready] = await Promise.all([getTranslations("Home"), isApiReady()]);
  return ready ? (
    <span className="inline-flex items-center gap-2 text-emerald-700 dark:text-emerald-400">
      <CircleCheck className="size-4" aria-hidden />
      {t("apiHealthy")}
    </span>
  ) : (
    <span className="inline-flex items-center gap-2 text-destructive">
      <CircleX className="size-4" aria-hidden />
      {t("apiUnavailable")}
    </span>
  );
}
