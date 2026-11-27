import "server-only";
import type { Metadata, MetadataRoute } from "next";
import { getTranslations } from "next-intl/server";
import { getCachedHotelName } from "@/lib/hotel";

/** Кабинеты персонала, которые ставятся на телефон ярлыком-приложением («На экран Домой»). */
export type StaffApp = "owner" | "admin" | "hk";

const AREA_TITLE = { owner: "areaOwner", admin: "areaAdmin", hk: "areaHk" } as const;

/**
 * Манифест кабинета: ярлык открывает свой кабинет на весь экран, без адресной строки.
 * Область (scope) — весь сайт: владелец работает и на страницах /admin, а при истечении сессии
 * все попадают на /login — иначе при таких переходах появлялась бы адресная строка.
 * У каждого кабинета свой id и стартовая страница — это разные приложения.
 * Гостям манифест не отдаётся: ссылка на него — только в layout кабинетов персонала.
 * Цвета — песочная шапка кабинета (lib/theme-color.ts).
 */
export async function staffAppManifest(app: StaffApp): Promise<Response> {
  const [name, t] = await Promise.all([getCachedHotelName(), getTranslations({ locale: "ru", namespace: "Nav" })]);
  const icons: MetadataRoute.Manifest["icons"] = [192, 512].flatMap((size) =>
    (["any", "maskable"] as const).map((purpose) => ({
      src: `/app-icon/${size}`,
      sizes: `${size}x${size}`,
      type: "image/png",
      purpose,
    })),
  );
  const manifest: MetadataRoute.Manifest = {
    id: `/${app}`,
    name: `${name} · ${t(AREA_TITLE[app])}`,
    short_name: name,
    lang: "ru",
    start_url: `/${app}`,
    scope: "/",
    display: "standalone",
    background_color: "#f6f1ea",
    theme_color: "#f6f1ea",
    icons,
  };
  return Response.json(manifest, { headers: { "Content-Type": "application/manifest+json; charset=utf-8" } });
}

/** metadata для layout кабинета: ссылка на манифест и режим приложения на iPhone. */
export function staffAppMetadata(app: StaffApp): Metadata {
  return {
    manifest: `/${app}/manifest.webmanifest`,
    appleWebApp: { capable: true, statusBarStyle: "default" },
  };
}
