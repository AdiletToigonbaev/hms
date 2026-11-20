"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { api } from "@/lib/api/server";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import type { TokenResponse } from "@/lib/api/types";
import { REFRESH_COOKIE, allSessionCookies, sessionCookies } from "@/lib/auth/cookies";
import { homeFor, safeNextPath } from "@/lib/auth/routes";
import {
  changePasswordSchema,
  checkbox,
  emailSchema,
  fieldErrors,
  loginSchema,
  registerSchema,
  resetPasswordSchema,
} from "@/lib/validation/auth";

// Поля, которые возвращаются в форму при ошибке (пароли — никогда).
const LOGIN_FIELDS = ["login"] as const;
const REGISTER_FIELDS = ["fullName", "email", "phone", "acceptPersonalData", "acceptWhatsApp"] as const;
const EMAIL_FIELDS = ["email"] as const;

async function writeSession(tokens: TokenResponse) {
  const jar = await cookies();
  for (const cookie of sessionCookies(tokens)) jar.set(cookie);
}

export async function loginAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = loginSchema.safeParse({ login: form.get("login"), password: form.get("password") });
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, LOGIN_FIELDS);

  const result = await api<TokenResponse>("/auth/login", { method: "POST", body: parsed.data, anonymous: true });
  if (!result.ok) return withValues(toFormState(result.problem), form, LOGIN_FIELDS);

  await writeSession(result.data);
  const user = result.data.user;
  redirect(user.mustChangePassword ? "/change-password" : (safeNextPath(form.get("next") as string | null) ?? homeFor(user.roles)));
}

export async function registerAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = registerSchema.safeParse({
    fullName: form.get("fullName"),
    email: form.get("email"),
    phone: form.get("phone") ?? "",
    password: form.get("password"),
    acceptPersonalData: checkbox(form, "acceptPersonalData"),
    acceptWhatsApp: checkbox(form, "acceptWhatsApp"),
  });
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, REGISTER_FIELDS);

  const result = await api("/auth/register", { method: "POST", body: parsed.data, anonymous: true });
  if (!result.ok) return withValues(toFormState(result.problem), form, REGISTER_FIELDS);
  return { ok: true };
}

export async function confirmEmailAction(_: FormState, form: FormData): Promise<FormState> {
  const result = await api("/auth/confirm-email", {
    method: "POST",
    body: { userId: form.get("userId"), token: form.get("token") },
    anonymous: true,
  });
  return result.ok ? { ok: true } : toFormState(result.problem);
}

export async function resendConfirmationAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = emailSchema.safeParse({ email: form.get("email") });
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, EMAIL_FIELDS);
  const result = await api("/auth/resend-confirmation", { method: "POST", body: parsed.data, anonymous: true });
  return result.ok ? { ok: true } : withValues(toFormState(result.problem), form, EMAIL_FIELDS);
}

export async function forgotPasswordAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = emailSchema.safeParse({ email: form.get("email") });
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, EMAIL_FIELDS);
  const result = await api("/auth/forgot-password", { method: "POST", body: parsed.data, anonymous: true });
  // Ответ одинаков для существующих и несуществующих адресов.
  return result.ok || result.status === 400 ? { ok: true } : withValues(toFormState(result.problem), form, EMAIL_FIELDS);
}

export async function resetPasswordAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = resetPasswordSchema.safeParse({
    userId: form.get("userId"),
    token: form.get("token"),
    password: form.get("password"),
  });
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };

  const result = await api("/auth/reset-password", {
    method: "POST",
    body: { userId: parsed.data.userId, token: parsed.data.token, newPassword: parsed.data.password },
    anonymous: true,
  });
  return result.ok ? { ok: true } : toFormState(result.problem);
}

export async function changePasswordAction(_: FormState, form: FormData): Promise<FormState> {
  const parsed = changePasswordSchema.safeParse({
    currentPassword: form.get("currentPassword"),
    newPassword: form.get("newPassword"),
  });
  if (!parsed.success) return { ok: false, fieldErrors: fieldErrors(parsed.error) };

  const result = await api<TokenResponse>("/me/change-password", { method: "POST", body: parsed.data });
  if (!result.ok) {
    if (result.status === 401) redirect("/login");
    return toFormState(result.problem);
  }

  await writeSession(result.data);
  redirect(homeFor(result.data.user.roles));
}

export async function logoutAction(): Promise<void> {
  const jar = await cookies();
  const refreshToken = jar.get(REFRESH_COOKIE)?.value;
  if (refreshToken) {
    await api("/auth/logout", { method: "POST", body: { refreshToken }, anonymous: true });
  }
  for (const name of allSessionCookies) jar.delete(name);
  redirect("/login");
}
