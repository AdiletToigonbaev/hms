import type { ComponentProps } from "react";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

/** Нативный select в стиле полей ввода: надёжен в формах (отправляется без JS) и удобен на телефоне. */
export function NativeSelect({ className, ...props }: ComponentProps<"select">) {
  return (
    <select
      className={cn(
        "h-9 w-full min-w-0 rounded-lg border border-input bg-card px-2.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive",
        className,
      )}
      {...props}
    />
  );
}

export function SelectField({
  name,
  label,
  error,
  hint,
  children,
  ...props
}: ComponentProps<"select"> & { name: string; label: string; error?: string; hint?: string }) {
  const id = props.id ?? name;
  return (
    <div className="grid gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <NativeSelect id={id} name={name} aria-invalid={error ? true : undefined} {...props}>
        {children}
      </NativeSelect>
      {hint && !error && <p className="text-xs text-muted-foreground">{hint}</p>}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
