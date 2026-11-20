import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";

export default async function Layout({ children }: LayoutProps<"/hk">) {
  const t = await getTranslations("Nav");
  return (
    <Suspense fallback={<AreaSkeleton />}>
      <AreaShell roles={["Housekeeper"]} title={t("areaHk")} nav={[{ href: "/hk", label: t("hkHome") }]}>
        {children}
      </AreaShell>
    </Suspense>
  );
}
