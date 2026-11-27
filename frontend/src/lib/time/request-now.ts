import "server-only";
import { connection } from "next/server";

/**
 * Текущее время для серверных компонентов. Cache Components запрещает читать часы (new Date())
 * при пререндеринге — в том числе при предзагрузке страниц (partialPrefetching): значение «устарело бы»
 * в сохранённой оболочке. connection() откладывает этот кусок до реального запроса — как требует Next.js
 * (docs: getting-started/caching «Random values and timestamps»). Вызывать до hotelToday/hotelHour/…
 * и передавать результат им явно.
 */
export async function requestNow(): Promise<Date> {
  await connection();
  return new Date();
}
