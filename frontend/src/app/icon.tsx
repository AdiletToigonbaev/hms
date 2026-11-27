import { ImageResponse } from "next/og";
import { Monogram, brandFonts } from "@/lib/brand-image";
import { getHotelInfo } from "@/lib/hotel";

// Иконка во вкладке браузера и в закладках — монограмма из названия гостиницы (настройки).
export const size = { width: 32, height: 32 };
export const contentType = "image/png";

export default async function Icon() {
  const [hotel, fonts] = await Promise.all([getHotelInfo(), brandFonts()]);
  return new ImageResponse(<Monogram name={hotel.name} size={size.width} />, { ...size, fonts });
}
