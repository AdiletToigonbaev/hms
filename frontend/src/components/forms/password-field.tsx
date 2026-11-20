"use client";

import { Eye, EyeOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState, type ComponentProps } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

interface PasswordFieldProps extends Omit<ComponentProps<typeof Input>, "type"> {
  name: string;
  label: string;
  hint?: string;
  error?: string;
}

/**
 * Поле пароля с кнопкой «показать / скрыть» (глазик). Кнопка не отправляет форму и не крадёт фокус у Tab-порядка:
 * она идёт сразу после поля. Состояние сбрасывается при перезагрузке — по умолчанию пароль скрыт.
 */
export function PasswordField({ name, label, hint, error, id, className, ...input }: PasswordFieldProps) {
  const t = useTranslations("Common");
  const [visible, setVisible] = useState(false);
  const inputId = id ?? name;
  const describedBy = [hint && `${inputId}-hint`, error && `${inputId}-error`].filter(Boolean).join(" ") || undefined;
  return (
    <div className="grid content-start gap-1.5">
      <Label htmlFor={inputId}>{label}</Label>
      <div className="relative">
        <Input
          id={inputId}
          name={name}
          type={visible ? "text" : "password"}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          className={cn("pr-10", className)}
          // Показанный пароль не должен попадать в автозамену и проверку орфографии.
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          {...input}
        />
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? t("hidePassword") : t("showPassword")}
          aria-pressed={visible}
          aria-controls={inputId}
          title={visible ? t("hidePassword") : t("showPassword")}
          className="absolute top-1/2 right-1 grid size-8 -translate-y-1/2 place-items-center rounded-md text-muted-foreground transition hover:bg-accent hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        >
          {visible ? <EyeOff className="size-4" aria-hidden /> : <Eye className="size-4" aria-hidden />}
        </button>
      </div>
      {hint && !error && (
        <p id={`${inputId}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${inputId}-error`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}
