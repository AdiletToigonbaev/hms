import { ArrowRight, BedDouble, CalendarRange, DoorOpen, PlugZap, Settings, Tags, UserRound, Users, type LucideIcon } from "lucide-react";
import Link from "next/link";

const icons = { bookings: CalendarRange, pricing: Tags, guests: UserRound, rooms: DoorOpen, roomTypes: BedDouble, staff: Users, settings: Settings, integrations: PlugZap } satisfies Record<string, LucideIcon>;

export interface QuickLink {
  href: string;
  title: string;
  description: string;
  icon: keyof typeof icons;
}

/** Карточки быстрого перехода на стартовых страницах кабинетов. */
export function QuickLinks({ links }: { links: QuickLink[] }) {
  return (
    <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {links.map((l) => {
        const Icon = icons[l.icon];
        return (
          <li key={l.href}>
            <Link
              href={l.href}
              className="group flex h-full items-start gap-4 rounded-2xl border bg-card p-5 shadow-xs transition hover:-translate-y-0.5 hover:shadow-md"
            >
              <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
                <Icon className="size-5" aria-hidden />
              </span>
              <span className="grid gap-1">
                <span className="flex items-center gap-1 font-heading text-lg">
                  {l.title}
                  <ArrowRight className="size-4 opacity-0 transition group-hover:translate-x-0.5 group-hover:opacity-100" aria-hidden />
                </span>
                <span className="text-sm text-muted-foreground">{l.description}</span>
              </span>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
