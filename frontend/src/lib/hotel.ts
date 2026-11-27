import "server-only";
import { cacheLife, cacheTag } from "next/cache";
import { apiInternalUrl } from "@/lib/api/config";
import { api } from "@/lib/api/server";
import type { PublicHotelInfo } from "@/lib/api/types";

const FALLBACK_NAME = "Гостиница";

/** Тег кэша названия: сбрасывается при сохранении сведений о гостинице (owner/settings/actions.ts). */
export const HOTEL_NAME_TAG = "hotel-name";

/**
 * Название гостиницы для метаданных всех страниц (заголовок вкладки, Open Graph).
 * Кэшируется, чтобы статические страницы оставались статическими (Cache Components): getHotelInfo читает
 * заголовки запроса. При сборке образа API недоступен — тогда «Гостиница», и через минуту (cacheLife «minutes»)
 * страница перегенерируется с настоящим названием; после сохранения настроек — сразу (updateTag).
 */
export async function getCachedHotelName(): Promise<string> {
  "use cache";
  cacheLife("minutes");
  cacheTag(HOTEL_NAME_TAG);
  try {
    const response = await fetch(`${apiInternalUrl()}/api/v1/public/hotel-info`, { signal: AbortSignal.timeout(3000) });
    if (response.ok) {
      const info = (await response.json()) as Partial<PublicHotelInfo>;
      if (info.name?.trim()) return info.name;
    }
  } catch {
    // API недоступен (сборка, перезапуск) — нейтральное название до следующего обновления кэша.
  }
  return FALLBACK_NAME;
}

/** Публичные сведения о гостинице (название для бренда и т. п.). Без названия — нейтральное «Гостиница». */
export async function getHotelInfo(): Promise<PublicHotelInfo> {
  const result = await api<PublicHotelInfo>("/public/hotel-info", { anonymous: true });
  return result.ok
    ? result.data
    : {
        name: FALLBACK_NAME,
        address: null,
        phone: null,
        email: null,
        description: null,
        checkInTime: "14:00",
        checkOutTime: "12:00",
        rules: null,
        freeCancelHoursBeforeCheckIn: 24,
        hourlyMinHours: 2,
        hourlyMaxHours: 12,
        hourlyFreeCancelMinutes: 120,
        autoConfirmWebBookings: false,
        hasLatePenalties: false,
      };
}
