import { cn } from "@/lib/utils";

/**
 * Знак гостиницы. Логотипа пока нет — монограмма из первой буквы названия (docs/design/design-direction.md).
 * Когда появится логотип, он подставляется только здесь.
 */
/** Буква монограммы — одна и та же в шапке, иконке сайта и картинке для ссылок (lib/brand-image.tsx). */
export function brandLetter(name: string): string {
  return name.trim().charAt(0).toUpperCase() || "Г";
}

export function Brand({ name, className, compact }: { name: string; className?: string; compact?: boolean }) {
  const letter = brandLetter(name);
  return (
    <span className={cn("inline-flex items-center gap-2.5", className)}>
      <span
        aria-hidden
        className="grid size-9 shrink-0 place-items-center rounded-xl bg-primary font-heading text-lg text-primary-foreground shadow-sm"
      >
        {letter}
      </span>
      {!compact && <span className="truncate font-heading text-lg leading-tight">{name}</span>}
    </span>
  );
}
