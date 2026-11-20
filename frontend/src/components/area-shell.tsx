import { LogOut } from "lucide-react";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";
import { logoutAction } from "@/app/(auth)/actions";
import { Button } from "@/components/ui/button";
import type { Role } from "@/lib/api/types";
import { requireUser } from "@/lib/auth/session";

export interface NavItem {
  href: string;
  label: string;
}

interface AreaShellProps {
  roles: readonly Role[];
  title: string;
  nav: NavItem[];
  children: ReactNode;
}

/**
 * Каркас раздела: проверяет вход и роль (перенаправляет, если нет доступа), показывает навигацию.
 * Рендерится внутри <Suspense> — читает cookies запроса.
 */
export async function AreaShell({ roles, title, nav, children }: AreaShellProps) {
  const [user, t] = await Promise.all([requireUser(roles), getTranslations("Common")]);
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="border-b bg-background">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
          <span className="font-semibold">HMS · {title}</span>
          <nav aria-label={title} className="flex flex-wrap gap-4 text-sm">
            {nav.map((item) => (
              <Link key={item.href} href={item.href} className="text-muted-foreground hover:text-foreground">
                {item.label}
              </Link>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="text-muted-foreground">{user.fullName}</span>
            <form action={logoutAction}>
              <Button type="submit" variant="ghost" size="sm">
                <LogOut aria-hidden />
                {t("logout")}
              </Button>
            </form>
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6">{children}</main>
    </div>
  );
}

export function AreaSkeleton() {
  return (
    <div className="flex min-h-dvh flex-col" aria-busy="true">
      <div className="h-14 border-b bg-muted/40" />
      <div className="mx-auto w-full max-w-6xl flex-1 px-4 py-6">
        <div className="h-8 w-48 animate-pulse rounded bg-muted" />
      </div>
    </div>
  );
}
