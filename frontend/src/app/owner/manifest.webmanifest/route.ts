import { staffAppManifest } from "@/lib/app-manifest";

/** Ярлык «На экран Домой» для владельца: открывает обзор (lib/app-manifest.ts). */
export function GET() {
  return staffAppManifest("owner");
}
