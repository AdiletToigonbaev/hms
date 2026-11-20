/**
 * Адрес backend для серверных вызовов Next.js (внутри docker-сети — http://api:8080).
 * Браузер обращается к API только через Next.js (BFF), см. docs/architecture/system-architecture.md §6.
 */
export function apiInternalUrl(): string {
  return process.env.API_INTERNAL_URL ?? "http://localhost:5080";
}
