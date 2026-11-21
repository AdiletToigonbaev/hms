import "server-only";

/** Публичный адрес сайта (sitemap, canonical, Open Graph). Задаётся PUBLIC_WEB_URL — тем же, что в ссылках из писем. */
export function siteUrl(): URL {
  const raw = process.env.PUBLIC_WEB_URL ?? "http://localhost:3000";
  try {
    return new URL(raw);
  } catch {
    return new URL("http://localhost:3000");
  }
}
