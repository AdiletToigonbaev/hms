import { CircleAlert, RotateCw } from "lucide-react";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export interface CrashScreenText {
  title: string;
  hint: string;
  retry: string;
  home: string;
  /** «Код ошибки: …» — идентификатор (digest) для поиска в логах сервера; без подробностей ошибки. */
  code?: string;
}

/**
 * Экран сбоя страницы (app/error.tsx, app/global-error.tsx): вместо стандартного «Application error»
 * Next.js — понятный текст в стиле сайта, «Повторить» и «На главную».
 */
export function CrashScreen({ text, onRetry }: { text: CrashScreenText; onRetry: () => void }) {
  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col items-center justify-center gap-5 px-4 text-center">
      <span className="grid size-14 place-items-center rounded-2xl bg-primary/10 text-primary" aria-hidden>
        <CircleAlert className="size-7" />
      </span>
      <div className="grid gap-2" role="alert">
        <h1 className="font-heading text-2xl">{text.title}</h1>
        <p className="text-muted-foreground">{text.hint}</p>
      </div>
      <div className="flex flex-wrap justify-center gap-3">
        <button type="button" onClick={onRetry} className={cn(buttonVariants(), "h-11 px-5")}>
          <RotateCw aria-hidden />
          {text.retry}
        </button>
        <Link href="/" prefetch={false} className={cn(buttonVariants({ variant: "outline" }), "h-11 px-5")}>
          {text.home}
        </Link>
      </div>
      {text.code && <p className="text-xs text-muted-foreground">{text.code}</p>}
    </main>
  );
}

/** digest ошибки из Server Component (Next.js скрывает подробности и даёт идентификатор для логов). */
export function errorDigest(error: unknown): string | undefined {
  if (error instanceof Error && "digest" in error && typeof error.digest === "string") return error.digest;
  return undefined;
}
