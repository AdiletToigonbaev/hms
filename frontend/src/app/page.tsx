import { Suspense } from "react";
import { getTranslations } from "next-intl/server";
import { ApiStatus } from "@/components/api-status";

export default async function Home() {
  const [t, common] = await Promise.all([getTranslations("Home"), getTranslations("Common")]);
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col justify-center gap-6 px-4 py-12">
      <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
      <p className="text-muted-foreground">{t("status")}</p>
      <section aria-labelledby="api-status" className="rounded-lg border p-4">
        <h2 id="api-status" className="mb-2 text-sm font-medium text-muted-foreground">
          {t("apiStatus")}
        </h2>
        <Suspense fallback={<span className="text-muted-foreground">{common("loading")}</span>}>
          <ApiStatus />
        </Suspense>
      </section>
    </main>
  );
}
