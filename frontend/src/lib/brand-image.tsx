import "server-only";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { brandLetter } from "@/components/brand";

/**
 * Картинки бренда (иконка сайта, картинка для ссылок) генерируются из названия гостиницы в настройках —
 * та же монограмма, что в шапке (`Brand`). Цвета — токены светлой темы из globals.css
 * (ImageResponse не читает CSS-переменные): при смене палитры — поменять и здесь.
 */
export const brandColors = {
  primary: "#b5562f",
  onPrimary: "#fffaf5",
  sand: "#f3ece2",
  text: "#2b2420",
  muted: "#6e625a",
  border: "#e6dccf",
} as const;

/** Картинка для ссылок (маршрут app/og-image) — для openGraph.images в metadata. */
export const OG_IMAGE = { url: "/og-image", width: 1200, height: 630 } as const;

// Lora 400 (OFL, assets/fonts/Lora-OFL.txt): латиница, кириллица и расширенная кириллица (Ң, Ө, Ү).
// ImageResponse понимает ttf/otf/woff, но не woff2 — поэтому отдельные файлы, а не next/font.
// У каждого набора символов своё имя: с одинаковым именем ImageResponse берёт только первый файл,
// и кириллица уходит в шрифт по умолчанию. Порядок поиска глифа — в BRAND_FONT_FAMILY.
const fontFiles = [
  { name: "Lora", file: "lora-latin-400-normal.woff" },
  { name: "Lora Cyrillic", file: "lora-cyrillic-400-normal.woff" },
  { name: "Lora Cyrillic Ext", file: "lora-cyrillic-ext-400-normal.woff" },
];
const BRAND_FONT_FAMILY = fontFiles.map((f) => `"${f.name}"`).join(", ");

type BrandFont = { name: string; data: Buffer; weight: 400; style: "normal" };
let fonts: Promise<BrandFont[]> | undefined;

/** Шрифты читаются один раз на процесс. */
export function brandFonts(): Promise<BrandFont[]> {
  fonts ??= Promise.all(
    fontFiles.map(async ({ name, file }) => ({
      name,
      data: await readFile(join(process.cwd(), "assets", "fonts", file)),
      weight: 400 as const,
      style: "normal" as const,
    })),
  );
  return fonts;
}

/** fontFamily для разметки ImageResponse: Lora со всеми наборами символов. */
export const brandFontFamily = BRAND_FONT_FAMILY;

/** Монограмма: первая буква названия на терракотовом фоне (как в шапке: rounded-xl ≈ треть стороны). */
export function Monogram({ name, size, rounded = true }: { name: string; size: number; rounded?: boolean }) {
  return (
    <div
      style={{
        width: size,
        height: size,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: brandColors.primary,
        color: brandColors.onPrimary,
        borderRadius: rounded ? Math.round(size / 3.6) : 0,
        fontFamily: brandFontFamily,
        fontSize: Math.round(size * 0.6),
        lineHeight: 1,
      }}
    >
      {brandLetter(name)}
    </div>
  );
}
