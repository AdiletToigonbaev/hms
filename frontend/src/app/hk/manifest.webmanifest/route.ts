import { staffAppManifest } from "@/lib/app-manifest";

/** Ярлык «На экран Домой» для уборщика: открывает задачи уборки (lib/app-manifest.ts). */
export function GET() {
  return staffAppManifest("hk");
}
