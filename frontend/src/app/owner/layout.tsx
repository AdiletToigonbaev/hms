import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";
import { staffNav } from "@/lib/nav";
import { staffAppMetadata } from "@/lib/app-manifest";
import { areaViewport } from "@/lib/theme-color";

// Панель браузера на телефоне — в тон песочной шапке кабинета.
export const viewport = areaViewport;

// «На экран Домой» — ярлык, который открывает кабинет владельца как приложение (lib/app-manifest.ts).
export const metadata = staffAppMetadata("owner");

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
