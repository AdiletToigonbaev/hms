import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";
import { staffNav } from "@/lib/nav";

export default async function Layout({ children }: LayoutProps<"/owner">) {
  const t = await getTranslations("Nav");
  return (
    <Suspense fallback={<AreaSkeleton />}>
      <AreaShell roles={["Owner"]} title={t("areaOwner")} nav={(roles) => staffNav(roles, t)}>
        {children}
      </AreaShell>
    </Suspense>
  );
}
