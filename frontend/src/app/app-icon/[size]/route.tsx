import { ImageResponse } from "next/og";
import { Monogram, brandFonts } from "@/lib/brand-image";
import { getHotelInfo } from "@/lib/hotel";

/** Размеры значка приложения для манифеста (Android требует 192 и 512). */
const SIZES = new Set([192, 512]);

/**
 * Значок ярлыка «На экран Домой» (манифест /hk). Монограмма во весь квадрат без скругления:
 * система сама накладывает маску (purpose «maskable»), буква — в безопасной зоне по центру.
 */
export async function GET(_: Request, { params }: RouteContext<"/app-icon/[size]">) {
  const size = Number((await params).size);
  if (!SIZES.has(size)) return new Response(null, { status: 404 });
  const [hotel, fonts] = await Promise.all([getHotelInfo(), brandFonts()]);
  return new ImageResponse(<Monogram name={hotel.name} size={size} rounded={false} />, { width: size, height: size, fonts });
}
