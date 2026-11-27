import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { AreaShell, AreaSkeleton } from "@/components/area-shell";
import { housekeeperNav } from "@/lib/nav";
import { staffAppMetadata } from "@/lib/app-manifest";
import { areaViewport } from "@/lib/theme-color";

// Панель браузера на телефоне — в тон песочной шапке кабинета.
export const viewport = areaViewport;

// «На экран Домой» — ярлык, который открывает задачи уборки как приложение (lib/app-manifest.ts).
export const metadata = staffAppMetadata("hk");

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
