import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";

export default async function Layout({ children }: LayoutProps<"/admin">) {
  const t = await getTranslations("Nav");
  return (
    <Suspense fallback={<AreaSkeleton />}>
      <AreaShell roles={["Administrator", "Owner"]} title={t("areaAdmin")} nav={[{ href: "/admin", label: t("adminHome") }]}>
        {children}
      </AreaShell>
    </Suspense>
  );
}
