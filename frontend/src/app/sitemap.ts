import type { MetadataRoute } from "next";
import { api } from "@/lib/api/server";
import type { PublicRoomType } from "@/lib/api/types";
import { siteUrl } from "@/lib/site/url";

/** Главная и страницы категорий номеров (только продаваемые категории). */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = siteUrl();
  const types = await api<PublicRoomType[]>("/public/room-types", { anonymous: true });
  return [
    { url: new URL("/", base).toString(), changeFrequency: "weekly", priority: 1 },
    ...(types.ok ? types.data : []).map((t) => ({ url: new URL(`/rooms/${t.id}`, base).toString(), changeFrequency: "weekly" as const, priority: 0.8 })),
  ];
}
