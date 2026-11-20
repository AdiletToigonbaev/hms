import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { cookies } from "next/headers";
import { Suspense } from "react";
import { ApiStatus } from "@/components/api-status";
import { buttonVariants } from "@/components/ui/button";
import { ACCESS_COOKIE, REFRESH_COOKIE } from "@/lib/auth/cookies";
import { readAccessToken } from "@/lib/auth/jwt";
import { homeFor } from "@/lib/auth/routes";

/** Ссылки входа или личного кабинета — зависят от cookie, поэтому рендерятся внутри Suspense. */
async function SessionLinks() {
  const [jar, t] = await Promise.all([cookies(), getTranslations("Home")]);
  const claims = readAccessToken(jar.get(ACCESS_COOKIE)?.value);
  if (claims || jar.has(REFRESH_COOKIE)) {
    return (
      <Link href={claims ? homeFor(claims.roles) : "/account"} className={buttonVariants()}>
        {t("cabinet")}
      </Link>
    );
  }
  return (
    <>
      <Link href="/login" className={buttonVariants()}>
        {t("login")}
      </Link>
      <Link href="/register" className={buttonVariants({ variant: "outline" })}>
        {t("register")}
      </Link>
    </>
  );
}

export default async function Home() {
  const [t, common] = await Promise.all([getTranslations("Home"), getTranslations("Common")]);
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col justify-center gap-6 px-4 py-12">
      <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
      <p className="text-muted-foreground">{t("status")}</p>
      <div className="flex flex-wrap gap-2">
        <Suspense fallback={<span className="h-8" />}>
          <SessionLinks />
        </Suspense>
      </div>
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
