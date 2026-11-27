import { staffAppManifest } from "@/lib/app-manifest";

/** Ярлык «На экран Домой» для администратора: открывает рабочий стол (lib/app-manifest.ts). */
export function GET() {
  return staffAppManifest("admin");
}
