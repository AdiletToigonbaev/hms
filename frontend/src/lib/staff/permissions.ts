import type { PermissionInfo, PermissionOverride, RoleGrant } from "@/lib/api/types";

/** Разрешение, для которого задаётся лимит суммы. */
export const LIMITED_PERMISSIONS = new Set(["refund.approve"]);

/**
 * Отмеченные права → индивидуальные настройки относительно прав роли:
 * отмечено, но нет у роли → «выдано»; не отмечено, но есть у роли → «отозвано».
 * Для прав с лимитом лимит сохраняется, даже если право есть у роли.
 */
export function computeOverrides(
  configurable: readonly PermissionInfo[],
  roleDefaults: ReadonlySet<string>,
  checked: ReadonlySet<string>,
  limits: Readonly<Record<string, number | null | undefined>>,
): PermissionOverride[] {
  const result: PermissionOverride[] = [];
  for (const { code } of configurable) {
    const isChecked = checked.has(code);
    const inRole = roleDefaults.has(code);
    const limit = LIMITED_PERMISSIONS.has(code) ? (limits[code] ?? null) : null;

    if (isChecked && (!inRole || limit !== null)) {
      result.push({ code, isGranted: true, limitAmount: limit });
    } else if (!isChecked && inRole) {
      result.push({ code, isGranted: false, limitAmount: null });
    }
  }
  return result;
}

/** Отмеченные права роли → список прав роли с лимитами (лимит — только у прав с лимитом). */
export function computeRoleGrants(
  configurable: readonly PermissionInfo[],
  checked: ReadonlySet<string>,
  limits: Readonly<Record<string, number | null | undefined>>,
): RoleGrant[] {
  return configurable
    .filter((p) => checked.has(p.code))
    .map((p) => ({ code: p.code, limitAmount: LIMITED_PERMISSIONS.has(p.code) ? (limits[p.code] ?? null) : null }));
}

/** Отличие права роли от стандарта: «добавлено» (нет в стандарте), «снято» (есть в стандарте), иначе null. */
export function roleChange(code: string, checked: ReadonlySet<string>, defaults: ReadonlySet<string>): "added" | "removed" | null {
  const on = checked.has(code);
  const standard = defaults.has(code);
  return on && !standard ? "added" : !on && standard ? "removed" : null;
}

/** Группирует разрешения по разделам, сохраняя порядок каталога. */
export function groupPermissions(list: readonly PermissionInfo[]): [string, PermissionInfo[]][] {
  const groups = new Map<string, PermissionInfo[]>();
  for (const p of list) {
    const items = groups.get(p.group) ?? [];
    items.push(p);
    groups.set(p.group, items);
  }
  return [...groups.entries()];
}
