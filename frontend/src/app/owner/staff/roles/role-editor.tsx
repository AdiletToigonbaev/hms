"use client";

import { useTranslations } from "next-intl";
import { useActionState, useMemo, useState, useTransition } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { RolePermissions } from "@/lib/api/types";
import { computeRoleGrants, groupPermissions, LIMITED_PERMISSIONS, roleChange } from "@/lib/staff/permissions";
import { resetRolePermissionsAction, setRolePermissionsAction } from "../actions";

/** Права роли: галочки по разделам, лимит у прав с лимитом, отличия от стандарта, возврат к стандарту. */
export function RolePermissionsEditor({ settings }: { settings: RolePermissions }) {
  const t = useTranslations("Staff");
  const [state, action, pending] = useActionState<FormState, FormData>(setRolePermissionsAction, {});
  const [resetState, setResetState] = useState<FormState>({});
  const [resetting, startReset] = useTransition();
  const [asking, setAsking] = useState(false);
  const defaults = useMemo(() => new Set(settings.defaults), [settings.defaults]);
  const [checked, setChecked] = useState(() => new Set(settings.grants.map((g) => g.code)));
  const [limits, setLimits] = useState<Record<string, number | null>>(() => Object.fromEntries(settings.grants.map((g) => [g.code, g.limitAmount])));
  const grants = computeRoleGrants(settings.configurable, checked, limits);
  const toggle = (code: string, on: boolean) =>
    setChecked((prev) => {
      const next = new Set(prev);
      if (on) next.add(code);
      else next.delete(code);
      return next;
    });

  return (
    <form action={action} className="grid gap-6">
      <input type="hidden" name="role" value={settings.role} />
      <input type="hidden" name="grants" value={JSON.stringify(grants)} />
      <FormAlert state={state} success={t("roleSaved")} />
      <FormAlert state={resetState} success={t("roleResetDone")} />
      {groupPermissions(settings.configurable).map(([group, items]) => (
        <fieldset key={group} className="grid gap-2">
          <legend className="mb-1 text-sm font-medium">{group}</legend>
          {items.map((p) => {
            const isChecked = checked.has(p.code);
            const change = roleChange(p.code, checked, defaults);
            const id = `role-${settings.role}-${p.code}`;
            return (
              <div key={p.code} className="flex flex-wrap items-center gap-2">
                <Checkbox id={id} checked={isChecked} onCheckedChange={(v) => toggle(p.code, v === true)} />
                <Label htmlFor={id} className="font-normal">
                  {p.description}
                </Label>
                {change && <Badge variant={change === "removed" ? "destructive" : "default"}>{t(change === "added" ? "roleAdded" : "roleRemoved")}</Badge>}
                {LIMITED_PERMISSIONS.has(p.code) && isChecked && (
                  <span className="flex items-center gap-1 text-sm">
                    <Label htmlFor={`${id}-limit`} className="font-normal text-muted-foreground">
                      {t("limit")}
                    </Label>
                    <Input
                      id={`${id}-limit`}
                      type="number"
                      min={1}
                      step="0.01"
                      inputMode="decimal"
                      className="h-7 w-40"
                      placeholder={t("noLimit")}
                      value={limits[p.code] ?? ""}
                      onChange={(e) => setLimits((prev) => ({ ...prev, [p.code]: e.target.value === "" ? null : Number(e.target.value) }))}
                    />
                  </span>
                )}
              </div>
            );
          })}
        </fieldset>
      ))}
      <div className="flex flex-wrap items-center gap-2">
        <SubmitButton pending={pending}>{t("saveRolePermissions")}</SubmitButton>
        <Button type="button" variant="outline" disabled={pending || resetting} onClick={() => setAsking(true)}>
          {t("roleReset")}
        </Button>
      </div>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("roleResetTitle", { role: settings.displayName })}
        description={t("roleResetText")}
        confirmLabel={t("roleResetYes")}
        cancelLabel={t("cancel")}
        onConfirm={() =>
          startReset(async () => {
            const result = await resetRolePermissionsAction(settings.role);
            setResetState(result);
            if (result.ok) {
              setChecked(new Set(settings.defaults));
              setLimits({});
            }
          })
        }
      />
    </form>
  );
}
