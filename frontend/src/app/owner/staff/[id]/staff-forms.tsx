"use client";

import { useTranslations } from "next-intl";
import { useActionState, useMemo, useState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SubmitButton } from "@/components/forms/submit-button";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { Staff, StaffPermissions } from "@/lib/api/types";
import { computeOverrides, groupPermissions, LIMITED_PERMISSIONS } from "@/lib/staff/permissions";
import { resetStaffPasswordAction, setActiveAction, setPermissionsAction, updateStaffAction } from "../actions";

export function ProfileForm({ staff }: { staff: Staff }) {
  const t = useTranslations("Staff");
  const auth = useTranslations("Auth");
  const common = useTranslations("Common");
  const [state, action, pending] = useActionState<FormState, FormData>(updateStaffAction, {});
  const errors = state.fieldErrors ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="id" value={staff.id} />
      <FormAlert state={state} success={t("saved")} />
      <Field name="fullName" label={t("fullName")} defaultValue={state.values?.fullName ?? staff.fullName} required error={errors.fullName} />
      <Field name="email" type="email" label={auth("email")} defaultValue={state.values?.email ?? staff.email ?? ""} error={errors.email} />
      <Field name="phone" type="tel" label={auth("phone")} defaultValue={state.values?.phone ?? staff.phone ?? ""} error={errors.phone} />
      <SubmitButton pending={pending}>{common("save")}</SubmitButton>
    </form>
  );
}

export function StatusForm({ id, isActive }: { id: string; isActive: boolean }) {
  const t = useTranslations("Staff");
  const [state, action, pending] = useActionState<FormState, FormData>(setActiveAction, {});
  return (
    <form action={action} className="grid gap-3">
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="activate" value={String(!isActive)} />
      <FormAlert state={state} />
      <SubmitButton pending={pending} variant={isActive ? "destructive" : "default"}>
        {isActive ? t("deactivate") : t("activate")}
      </SubmitButton>
    </form>
  );
}

export function ResetPasswordForm({ id }: { id: string }) {
  const t = useTranslations("Staff");
  const auth = useTranslations("Auth");
  const [state, action, pending] = useActionState<FormState, FormData>(resetStaffPasswordAction, {});
  return (
    <form action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="id" value={id} />
      <FormAlert state={state} success={t("resetDone")} />
      <Field
        name="newPassword"
        type="text"
        autoComplete="off"
        label={t("initialPassword")}
        hint={auth("passwordHint")}
        required
        error={state.fieldErrors?.newPassword}
      />
      <SubmitButton pending={pending} variant="outline">
        {t("resetPassword")}
      </SubmitButton>
    </form>
  );
}

export function PermissionsEditor({ id, permissions }: { id: string; permissions: StaffPermissions }) {
  const t = useTranslations("Staff");
  const [state, action, pending] = useActionState<FormState, FormData>(setPermissionsAction, {});
  const roleDefaults = useMemo(() => new Set(permissions.rolePermissions), [permissions.rolePermissions]);
  const [checked, setChecked] = useState(() => new Set(permissions.effective));
  const [limits, setLimits] = useState<Record<string, number | null>>(() =>
    Object.fromEntries(permissions.overrides.map((o) => [o.code, o.limitAmount])),
  );

  const overrides = computeOverrides(permissions.configurable, roleDefaults, checked, limits);
  const toggle = (code: string, on: boolean) =>
    setChecked((prev) => {
      const next = new Set(prev);
      if (on) next.add(code);
      else next.delete(code);
      return next;
    });

  return (
    <form action={action} className="grid gap-6">
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="overrides" value={JSON.stringify(overrides)} />
      <FormAlert state={state} success={t("saved")} />
      {groupPermissions(permissions.configurable).map(([group, items]) => (
        <fieldset key={group} className="grid gap-2">
          <legend className="mb-1 text-sm font-medium">{group}</legend>
          {items.map((p) => {
            const isChecked = checked.has(p.code);
            const inRole = roleDefaults.has(p.code);
            const status = inRole && isChecked ? "byRole" : !inRole && isChecked ? "granted" : inRole ? "revoked" : null;
            return (
              <div key={p.code} className="flex flex-wrap items-center gap-2">
                <Checkbox id={`perm-${p.code}`} checked={isChecked} onCheckedChange={(v) => toggle(p.code, v === true)} />
                <Label htmlFor={`perm-${p.code}`} className="font-normal">
                  {p.description}
                </Label>
                {status && (
                  <Badge variant={status === "revoked" ? "destructive" : status === "granted" ? "default" : "outline"}>
                    {t(status)}
                  </Badge>
                )}
                {LIMITED_PERMISSIONS.has(p.code) && isChecked && (
                  <span className="flex items-center gap-1 text-sm">
                    <Label htmlFor={`limit-${p.code}`} className="font-normal text-muted-foreground">
                      {t("limit")}
                    </Label>
                    <Input
                      id={`limit-${p.code}`}
                      type="number"
                      min={1}
                      step="0.01"
                      inputMode="decimal"
                      className="h-7 w-32"
                      value={limits[p.code] ?? ""}
                      onChange={(e) =>
                        setLimits((prev) => ({ ...prev, [p.code]: e.target.value === "" ? null : Number(e.target.value) }))
                      }
                    />
                  </span>
                )}
              </div>
            );
          })}
        </fieldset>
      ))}
      <div>
        <SubmitButton pending={pending}>{t("savePermissions")}</SubmitButton>
      </div>
    </form>
  );
}
