import "server-only";
import { api } from "@/lib/api/server";
import type { PublicHotelInfo } from "@/lib/api/types";

/** Публичные сведения о гостинице (название для бренда и т. п.). Без названия — нейтральное «Гостиница». */
export async function getHotelInfo(): Promise<PublicHotelInfo> {
  const result = await api<PublicHotelInfo>("/public/hotel-info", { anonymous: true });
  return result.ok
    ? result.data
    : {
        name: "Гостиница",
        address: null,
        phone: null,
        email: null,
        description: null,
        checkInTime: "14:00",
        checkOutTime: "12:00",
        rules: null,
        freeCancelHoursBeforeCheckIn: 24,
      };
}
