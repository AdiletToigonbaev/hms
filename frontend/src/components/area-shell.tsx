import { Menu } from "lucide-react";
import Link from "next/link";
import type { ReactNode } from "react";
import { Brand } from "@/components/brand";
import { LogoutButton } from "@/components/logout-button";
import { NavLinks, type NavItem } from "@/components/layout/nav-links";
import type { Role } from "@/lib/api/types";
import { requireUser } from "@/lib/auth/session";
import { getHotelInfo } from "@/lib/hotel";

export type { NavItem };

interface AreaShellProps {
  roles: readonly Role[];
  title: string;
  /** Меню: список или функция от ролей пользователя (меню персонала зависит от роли). */
  nav: NavItem[] | ((roles: readonly Role[]) => NavItem[]);
  children: ReactNode;
}

const roleLabel: Record<Role, string> = {
  Owner: "Владелец",
  Administrator: "Администратор",
  Housekeeper: "Уборщик",
  Guest: "Гость",
};

/**
 * Каркас кабинета: боковое меню на ПК, верхняя панель с меню на телефоне.
 * Проверяет вход и роль (перенаправляет без доступа). Рендерится внутри <Suspense> — читает cookies.
 */
export async function AreaShell({ roles, title, nav, children }: AreaShellProps) {
  const [user, hotel] = await Promise.all([requireUser(roles), getHotelInfo()]);
  const role = user.roles[0] ?? "Guest";
  const items = typeof nav === "function" ? nav(user.roles) : nav;

  const userBlock = (
    <div className="flex items-center justify-between gap-2 rounded-xl bg-sidebar-accent/50 p-3">
      <div className="min-w-0">
        <p className="truncate text-sm font-medium">{user.fullName}</p>
        <p className="text-xs text-muted-foreground">{roleLabel[role]}</p>
      </div>
      <LogoutButton />
    </div>
  );

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[17rem_1fr]">
      {/* ПК: боковое меню */}
      <aside className="sticky top-0 hidden h-dvh flex-col gap-6 border-r border-sidebar-border bg-sidebar p-4 lg:flex">
        <Link href="/" className="rounded-xl px-1 py-1">
          <Brand name={hotel.name} />
        </Link>
        <nav aria-label={title} className="grid gap-2">
          <p className="px-3 text-xs font-semibold tracking-wider text-muted-foreground uppercase">{roleLabel[role]}</p>
          <NavLinks items={items} />
        </nav>
        <div className="mt-auto">{userBlock}</div>
      </aside>

      {/* Телефон и планшет: верхняя панель */}
      <header className="sticky top-0 z-20 border-b border-sidebar-border bg-sidebar/95 backdrop-blur lg:hidden">
        <details className="group">
          <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 px-4 [&::-webkit-details-marker]:hidden">
            <Brand name={hotel.name} />
            <span className="grid size-10 place-items-center rounded-xl border bg-card" aria-label="Меню">
              <Menu className="size-5" aria-hidden />
            </span>
          </summary>
          <nav aria-label={title} className="grid gap-3 border-t border-sidebar-border px-4 pt-3 pb-4">
            <NavLinks items={items} />
            {userBlock}
          </nav>
        </details>
      </header>

      <main className="mx-auto w-full max-w-6xl min-w-0 px-4 py-6 sm:px-6 lg:px-10 lg:py-10 has-[[data-wide]]:max-w-none">{children}</main>
    </div>
  );
}

export function AreaSkeleton() {
  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[17rem_1fr]" aria-busy="true">
      <div className="hidden border-r bg-sidebar lg:block" />
      <div className="mx-auto w-full max-w-6xl px-4 py-10 lg:px-10">
        <div className="h-9 w-64 animate-pulse rounded-lg bg-muted" />
        <div className="mt-6 grid gap-3">
          <div className="h-24 animate-pulse rounded-2xl bg-muted/70" />
          <div className="h-24 animate-pulse rounded-2xl bg-muted/70" />
        </div>
      </div>
    </div>
  );
}
