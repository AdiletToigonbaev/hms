import type { ComponentProps, ReactNode } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface FieldProps extends ComponentProps<typeof Input> {
  name: string;
  label: string;
  hint?: string;
  error?: string;
  /** Значок-подсказка рядом с названием (HelpTip). */
  help?: ReactNode;
}

/** Поле формы с подписью, подсказкой и ошибкой (связаны через aria-describedby). */
export function Field({ name, label, hint, error, help, id, ...input }: FieldProps) {
  const inputId = id ?? name;
  const describedBy = [hint && `${inputId}-hint`, error && `${inputId}-error`].filter(Boolean).join(" ") || undefined;
  return (
    // content-start: в сетке формы ячейка растягивается по высоте соседней — строки поля не должны раздуваться.
    <div className="grid content-start gap-1.5">
      {help ? (
        <div className="flex items-center gap-1">
          <Label htmlFor={inputId}>{label}</Label>
          {help}
        </div>
      ) : (
        <Label htmlFor={inputId}>{label}</Label>
      )}
      <Input id={inputId} name={name} aria-invalid={error ? true : undefined} aria-describedby={describedBy} {...input} />
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
