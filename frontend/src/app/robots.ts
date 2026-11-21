import type { MetadataRoute } from "next";
import { siteUrl } from "@/lib/site/url";

/** Поисковикам — только сайт: кабинеты, бронирование и служебные адреса закрыты. */
export default function robots(): MetadataRoute.Robots {
  const base = siteUrl();
  return {
    rules: { userAgent: "*", allow: ["/", "/rooms/", "/media/"], disallow: ["/admin", "/owner", "/hk", "/account", "/book", "/api", "/login", "/register"] },
    sitemap: new URL("/sitemap.xml", base).toString(),
  };
}
