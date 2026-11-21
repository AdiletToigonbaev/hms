"use client";

import { Loader2, Search } from "lucide-react";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef, useTransition } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

/** Пауза после ввода, прежде чем искать: не спрашивать сервер на каждую букву. */
export const LIVE_SEARCH_DELAY_MS = 300;

/** Искать ли по введённому тексту: пусто — сбросить поиск; иначе — от `minChars` символов. */
export function shouldSearch(value: string, minChars: number): boolean {
  const v = value.trim();
  return v.length === 0 || v.length >= minChars;
}

/** Адрес страницы с поиском и текущими значениями формы (фильтры сохраняются, страница списка сбрасывается). */
export function searchUrl(pathname: string, form: FormData | null, name: string, value: string): string {
  const query = new URLSearchParams();
  for (const [key, v] of form?.entries() ?? []) {
    if (typeof v === "string" && v !== "" && key !== "page" && key !== name) query.append(key, v);
  }
  if (value.trim()) query.set(name, value.trim());
  const text = query.toString();
  return text ? `${pathname}?${text}` : pathname;
}

/**
 * Поле «живого» поиска: список обновляется по мере ввода (после короткой паузы), без кнопки.
 * Ищет сервер (страница перечитывает данные по адресу); остальные поля той же формы — фильтры — сохраняются.
 * Адрес меняется без новой записи в истории: «Назад» не листает каждую букву. Enter ищет сразу.
 */
export function LiveSearchInput({
  name = "search",
  defaultValue,
  placeholder,
  label,
  minChars = 2,
  className,
}: {
  name?: string;
  defaultValue: string;
  placeholder: string;
  label: string;
  minChars?: number;
  className?: string;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const [pending, startTransition] = useTransition();
  const input = useRef<HTMLInputElement>(null);
  const timer = useRef<number | undefined>(undefined);
  const searched = useRef(defaultValue.trim());

  useEffect(() => () => window.clearTimeout(timer.current), []);

  function search(value: string) {
    window.clearTimeout(timer.current);
    const v = value.trim();
    if (v === searched.current || !shouldSearch(v, minChars)) return;
    searched.current = v;
    const form = input.current?.form;
    const url = searchUrl(pathname, form ? new FormData(form) : null, name, v);
    startTransition(() => router.replace(url, { scroll: false }));
  }

  return (
    <div className={cn("relative", className)}>
      <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
      <Input
        ref={input}
        type="search"
        name={name}
        defaultValue={defaultValue}
        placeholder={placeholder}
        aria-label={label}
        autoComplete="off"
        className="pr-8 pl-8"
        onChange={(e) => {
          const value = e.target.value;
          window.clearTimeout(timer.current);
          timer.current = window.setTimeout(() => search(value), LIVE_SEARCH_DELAY_MS);
        }}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            e.preventDefault();
            search(e.currentTarget.value);
          }
        }}
      />
      {pending && <Loader2 className="absolute top-1/2 right-2.5 size-4 -translate-y-1/2 animate-spin text-muted-foreground" aria-label={label} />}
    </div>
  );
}
