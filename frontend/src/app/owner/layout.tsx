import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";

export default async function Layout({ children }: LayoutProps<"/owner">) {
  const t = await getTranslations("Nav");
  return (
    <Suspense fallback={<AreaSkeleton />}>
      <AreaShell roles={["Owner"]} title={t("areaOwner")} nav={[{ href: "/owner", label: t("ownerHome") }, { href: "/owner/staff", label: t("staff") }]}>
        {children}
      </AreaShell>
    </Suspense>
  );
}
