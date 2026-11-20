import { apiInternalUrl } from "@/lib/api/config";

const ID = /^[0-9a-f-]{36}$/i;

/** Проксирует публичные файлы API (фото номеров) — браузер к API напрямую не обращается. */
export async function GET(_: Request, { params }: RouteContext<"/media/[id]">) {
  const { id } = await params;
  if (!ID.test(id)) return new Response(null, { status: 404 });

  const upstream = await fetch(`${apiInternalUrl()}/api/v1/files/${id}`, { cache: "no-store" }).catch(() => null);
  if (!upstream?.ok || !upstream.body) return new Response(null, { status: upstream?.status === 404 ? 404 : 502 });

  const headers = new Headers();
  for (const name of ["content-type", "content-length", "cache-control", "etag", "last-modified"]) {
    const value = upstream.headers.get(name);
    if (value) headers.set(name, value);
  }
  headers.set("x-content-type-options", "nosniff");
  return new Response(upstream.body, { status: 200, headers });
}
