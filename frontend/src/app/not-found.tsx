import Link from "next/link";
import { getTranslations } from "next-intl/server";

export default async function NotFound() {
  const t = await getTranslations("Common");
  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col items-center justify-center gap-4 px-4 text-center">
      <h1 className="text-2xl font-semibold">{t("notFound")}</h1>
      <Link href="/" className="text-primary underline underline-offset-4">
        {t("backHome")}
      </Link>
    </main>
  );
}
