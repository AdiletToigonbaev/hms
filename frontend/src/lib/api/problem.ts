import type { Problem } from "@/lib/api/types";

/** Состояние формы после серверного действия. */
export interface FormState {
  ok?: boolean;
  /** Ключ сообщения из messages/ru.json → Errors.* */
  error?: string;
  /** Ошибки полей: поле → текст (валидация API уже на русском). */
  fieldErrors?: Record<string, string>;
  message?: string;
}

/** Коды ошибок API, для которых есть переведённые сообщения (Errors.<code>). */
const knownCodes = new Set([
  "auth.invalid_credentials",
  "auth.locked_out",
  "auth.email_not_confirmed",
  "auth.invalid_token",
  "auth.email_taken",
  "auth.phone_taken",
  "auth.wrong_password",
  "auth.password_rejected",
  "auth.forbidden",
  "auth.unauthorized",
  "staff.cannot_modify_owner",
  "staff.permission_not_configurable",
  "staff.permissions_not_configurable",
  "staff.not_found",
  "rate_limited",
  "api.unavailable",
  "validation.failed",
]);

/** ProblemDetails → состояние формы: ошибки полей и общий код сообщения. */
export function toFormState(problem: Problem): FormState {
  const fieldErrors: Record<string, string> = {};
  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    if (messages.length > 0) fieldErrors[field] = messages[0];
  }
  const code = problem.code && knownCodes.has(problem.code) ? problem.code : "unknown";
  return {
    ok: false,
    error: code,
    // Для кодов вроде auth.password_rejected API присылает конкретный текст причины.
    message: code === "auth.password_rejected" || code === "unknown" ? problem.title : undefined,
    fieldErrors: Object.keys(fieldErrors).length ? fieldErrors : undefined,
  };
}

/** Ключ i18n для кода ошибки: точки в ключах next-intl означают вложенность — заменяем на «_». */
export function errorKey(code: string): string {
  return code.replace(/\./g, "_");
}
