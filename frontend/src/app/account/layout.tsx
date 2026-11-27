import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";
import { guestNav } from "@/lib/nav";
import { areaViewport } from "@/lib/theme-color";

// Панель браузера на телефоне — в тон песочной шапке кабинета.
export const viewport = areaViewport;

export default async function Layout({ children }: LayoutProps<"/account">) {
  const t = await getTranslations("Nav");
  return (
    <Suspense fallback={<AreaSkeleton />}>
      <AreaShell roles={["Guest", "Owner", "Administrator", "Housekeeper"]} title={t("areaAccount")} nav={guestNav(t)}>
        {children}
      </AreaShell>
    </Suspense>
  );
}
