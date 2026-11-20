import { CircleAlert, Inbox, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

/** Пустой список (ТЗ §16: empty states). */
export function EmptyState({
  title,
  description,
  action,
  icon: Icon = Inbox,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
  icon?: LucideIcon;
}) {
  return (
    <div className="grid justify-items-center gap-3 rounded-2xl border border-dashed bg-card/60 px-6 py-12 text-center">
      <span className="grid size-12 place-items-center rounded-full bg-secondary text-muted-foreground">
        <Icon className="size-6" aria-hidden />
      </span>
      <div className="grid gap-1">
        <p className="font-heading text-lg">{title}</p>
        {description && <p className="max-w-md text-sm text-muted-foreground">{description}</p>}
      </div>
      {action}
    </div>
  );
}

/** Ошибка загрузки данных (ТЗ §16: error states). */
export function ErrorState({ title, description }: { title: string; description?: string }) {
  return (
    <div role="alert" className="flex items-start gap-3 rounded-2xl border border-destructive/30 bg-destructive/5 p-4">
      <CircleAlert className="mt-0.5 size-5 shrink-0 text-destructive" aria-hidden />
      <div className="grid gap-1">
        <p className="font-medium text-destructive">{title}</p>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
    </div>
  );
}

/** Заглушка загрузки (ТЗ §16: loading states). */
export function LoadingState({ rows = 3 }: { rows?: number }) {
  return (
    <div className="grid gap-3" aria-busy="true" aria-live="polite">
      <div className="h-8 w-56 animate-pulse rounded-lg bg-muted" />
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="h-20 animate-pulse rounded-2xl bg-muted/70" />
      ))}
    </div>
  );
}
