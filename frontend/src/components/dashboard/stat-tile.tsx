import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/** Показатель-«плитка»: крупное значение, подпись и основа расчёта (подсказка под значением, не только при наведении). */
export function StatTile({
  label,
  value,
  hint,
  help,
  tone,
  className,
}: {
  label: string;
  value: string;
  hint?: string;
  /** Значок «?» с пояснением (как в настройках) — рядом с названием. */
  help?: ReactNode;
  tone?: "bad" | "good";
  className?: string;
}) {
  return (
    <div className={cn("grid content-start gap-1 rounded-2xl border bg-card p-4", className)}>
      <span className="flex items-center gap-1 text-sm text-muted-foreground">
        {label}
        {help}
      </span>
      <span className={cn("tabular font-heading text-2xl leading-tight", tone === "bad" && "text-destructive")}>{value}</span>
      {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
    </div>
  );
}
