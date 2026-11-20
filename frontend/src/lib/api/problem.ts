import type { Problem } from "@/lib/api/types";

/** Состояние формы после серверного действия. */
export interface FormState {
  ok?: boolean;
  /** Ключ сообщения из messages/ru.json → Errors.* */
  error?: string;
  /** Ошибки полей: поле → текст (валидация API уже на русском). */
  fieldErrors?: Record<string, string>;
  message?: string;
  /**
   * Введённые значения (без паролей): React 19 сбрасывает поля формы после серверного действия,
   * поэтому при ошибке форма восстанавливает их отсюда через defaultValue.
   */
  values?: Record<string, string>;
}

/** Значения полей формы для повторного показа (пароли и служебные поля не возвращаются). */
export function echoValues(form: FormData, names: readonly string[]): Record<string, string> {
  const values: Record<string, string> = {};
  for (const name of names) {
    const value = form.get(name);
    if (typeof value === "string") values[name] = value;
  }
  return values;
}

/** Ошибка с сохранением введённых значений. */
export function withValues(state: FormState, form: FormData, names: readonly string[]): FormState {
  return { ...state, values: echoValues(form, names) };
}

/** ProblemDetails → состояние формы: ошибки полей и общий код сообщения. */
export function toFormState(problem: Problem): FormState {
  const fieldErrors: Record<string, string> = {};
  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    if (messages.length > 0) fieldErrors[field] = messages[0];
  }
  return {
    ok: false,
    error: problem.code ?? "unknown",
    // Текст API (на русском) — запасной вариант, если перевода кода нет, и точная причина для auth.password_rejected.
    message: problem.title,
    fieldErrors: Object.keys(fieldErrors).length ? fieldErrors : undefined,
  };
}

/** Ключ i18n для кода ошибки: точки в ключах next-intl означают вложенность — заменяем на «_». */
export function errorKey(code: string): string {
  return code.replace(/\./g, "_");
}
