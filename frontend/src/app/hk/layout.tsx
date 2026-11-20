import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";
import { housekeeperNav } from "@/lib/nav";

export default async function Layout({ children }: LayoutProps<"/hk">) {
  const t = await getTranslations("Nav");
  return (
    <Suspense fallback={<AreaSkeleton />}>
      <AreaShell roles={["Housekeeper"]} title={t("areaHk")} nav={housekeeperNav(t)}>
        {children}
      </AreaShell>
    </Suspense>
  );
}
