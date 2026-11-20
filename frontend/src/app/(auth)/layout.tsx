import Link from "next/link";
import { getTranslations } from "next-intl/server";

export default async function AuthLayout({ children }: LayoutProps<"/">) {
  const t = await getTranslations("App");
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-6 bg-muted/40 px-4 py-10">
      <Link href="/" className="text-lg font-semibold tracking-tight">
        {t("name")} · {t("fullName")}
      </Link>
      <main className="w-full max-w-sm">{children}</main>
    </div>
  );
}
