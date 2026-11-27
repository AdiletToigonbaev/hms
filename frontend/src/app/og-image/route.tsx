import { ImageResponse } from "next/og";
import { Monogram, OG_IMAGE, brandColors, brandFontFamily, brandFonts } from "@/lib/brand-image";
import { getHotelInfo } from "@/lib/hotel";
import { siteUrl } from "@/lib/site/url";

/**
 * Картинка для ссылок (Open Graph): превью, когда ссылку на сайт пересылают в WhatsApp, Telegram и т. п.
 * Отдельный маршрут, а не файл opengraph-image: файловые метаданные перекрыли бы фото номеров
 * на страницах /rooms/[id]. Подключается в metadata через OG_IMAGE (layout, главная, номер без фото).
 */
export async function GET() {
  const [hotel, fonts] = await Promise.all([getHotelInfo(), brandFonts()]);
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          padding: 80,
          background: brandColors.sand,
          color: brandColors.text,
          fontFamily: brandFontFamily,
          borderBottom: `16px solid ${brandColors.primary}`,
        }}
      >
        <Monogram name={hotel.name} size={140} />
        <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
          <div style={{ fontSize: 88, lineHeight: 1.1 }}>{hotel.name}</div>
          {hotel.address && <div style={{ fontSize: 36, color: brandColors.muted }}>{hotel.address}</div>}
        </div>
        <div style={{ fontSize: 32, color: brandColors.muted }}>{siteUrl().host}</div>
      </div>
    ),
    { width: OG_IMAGE.width, height: OG_IMAGE.height, fonts },
  );
}
