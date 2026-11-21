"use client";

import {
  BedDouble,
  CalendarClock,
  Building2,
  CalendarRange,
  ClipboardList,
  DoorOpen,
  LayoutDashboard,
  Tags,
  UserRound,
  PlugZap,
  Receipt,
  Undo2,
  Wallet,
  Settings,
  Sparkles,
  Users,
  type LucideIcon,
} from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

// Иконки по ключу: компоненты нельзя передать из серверного компонента в клиентский.
const icons = {
  dashboard: LayoutDashboard,
  rooms: DoorOpen,
  roomTypes: BedDouble,
  staff: Users,
  settings: Settings,
  integrations: PlugZap,
  housekeeping: Sparkles,
  bookings: CalendarRange,
  calendar: CalendarClock,
  payments: Wallet,
  refunds: Undo2,
  expenses: Receipt,
  pricing: Tags,
  guests: UserRound,
  account: ClipboardList,
  hotel: Building2,
} satisfies Record<string, LucideIcon>;

export type NavIcon = keyof typeof icons;

export interface NavItem {
  href: string;
  label: string;
  icon: NavIcon;
  /** Подсвечивать только при точном совпадении (для «Обзор» раздела). */
  exact?: boolean;
}

export function NavLinks({ items, onNavigate }: { items: NavItem[]; onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <ul className="grid gap-1">
      {items.map((item) => {
        const Icon = icons[item.icon];
        const active = item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(`${item.href}/`);
        return (
          <li key={item.href}>
            <Link
              href={item.href}
              onClick={onNavigate}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex min-h-11 items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors",
                active
                  ? "bg-sidebar-accent text-sidebar-primary"
                  : "text-sidebar-foreground/80 hover:bg-sidebar-accent/60 hover:text-sidebar-foreground",
              )}
            >
              <Icon className="size-[18px] shrink-0" aria-hidden />
              {item.label}
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
