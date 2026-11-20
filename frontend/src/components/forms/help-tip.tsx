"use client";

import { Popover } from "@base-ui/react/popover";
import { CircleHelp, Lightbulb } from "lucide-react";
import { useTranslations } from "next-intl";

interface HelpTipProps {
  /** Название настройки — заголовок окошка. */
  title: string;
  /** Что делает настройка. */
  text: string;
  /** Пример ситуации с числами. */
  example?: string;
  /** Если настройка пока не применяется — когда заработает (честно, чтобы не путать). */
  pending?: string;
}

/**
 * Значок «?» рядом с названием настройки: по нажатию — пояснение и пример сценария.
 * Открывается нажатием (не наведением) — удобно на телефоне и с клавиатуры.
 */
export function HelpTip({ title, text, example, pending }: HelpTipProps) {
  const t = useTranslations("Common");
  return (
    <Popover.Root>
      <Popover.Trigger
        type="button"
        aria-label={`${t("help")}: ${title}`}
        className="inline-grid size-5 shrink-0 place-items-center rounded-full text-muted-foreground transition outline-none hover:text-primary focus-visible:ring-3 focus-visible:ring-ring/50 data-[popup-open]:text-primary"
      >
        <CircleHelp className="size-4" aria-hidden />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Positioner sideOffset={6} align="start" className="z-50">
          <Popover.Popup className="grid w-80 max-w-[calc(100vw-1.5rem)] origin-[var(--transform-origin)] gap-2.5 rounded-xl border bg-card p-4 text-sm shadow-lg outline-none transition-[transform,opacity] duration-150 data-[ending-style]:scale-95 data-[ending-style]:opacity-0 data-[starting-style]:scale-95 data-[starting-style]:opacity-0">
            <Popover.Title className="font-heading text-base leading-snug">{title}</Popover.Title>
            <Popover.Description className="text-muted-foreground">{text}</Popover.Description>
            {example && (
              <div className="flex gap-2 rounded-lg bg-secondary/70 p-2.5 text-secondary-foreground">
                <Lightbulb className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
                <p>
                  <span className="font-medium">{t("example")}: </span>
                  {example}
                </p>
              </div>
            )}
            {pending && <p className="text-xs text-status-cleaning">{pending}</p>}
          </Popover.Popup>
        </Popover.Positioner>
      </Popover.Portal>
    </Popover.Root>
  );
}
