import { ImageResponse } from "next/og";
import { Monogram, brandFonts } from "@/lib/brand-image";
import { getHotelInfo } from "@/lib/hotel";

// Значок «На экран Домой» (iOS/Android). Без скругления — система накладывает свою маску.
export const size = { width: 180, height: 180 };
export const contentType = "image/png";

export default async function AppleIcon() {
  const [hotel, fonts] = await Promise.all([getHotelInfo(), brandFonts()]);
  return new ImageResponse(<Monogram name={hotel.name} size={size.width} rounded={false} />, { ...size, fonts });
}
