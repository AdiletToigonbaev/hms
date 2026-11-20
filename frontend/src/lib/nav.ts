import type { NavItem } from "@/components/layout/nav-links";
import type { Role } from "@/lib/api/types";

type Translate = (key: "ownerHome" | "adminHome" | "rooms" | "roomTypes" | "staff" | "settings" | "integrations" | "hkHome" | "accountHome") => string;

/** Меню персонала по ролям: владелец видит и свои разделы, и разделы администратора. */
export function staffNav(roles: readonly Role[], t: Translate): NavItem[] {
  const isOwner = roles.includes("Owner");
  const items: NavItem[] = [
    isOwner
      ? { href: "/owner", label: t("ownerHome"), icon: "dashboard", exact: true }
      : { href: "/admin", label: t("adminHome"), icon: "dashboard", exact: true },
    { href: "/admin/rooms", label: t("rooms"), icon: "rooms" },
    { href: "/admin/room-types", label: t("roomTypes"), icon: "roomTypes" },
  ];
  if (isOwner) {
    items.push(
      { href: "/owner/staff", label: t("staff"), icon: "staff" },
      { href: "/owner/settings", label: t("settings"), icon: "settings" },
      { href: "/owner/integrations", label: t("integrations"), icon: "integrations" },
    );
  }
  return items;
}

export function housekeeperNav(t: Translate): NavItem[] {
  return [{ href: "/hk", label: t("hkHome"), icon: "housekeeping", exact: true }];
}

export function guestNav(t: Translate): NavItem[] {
  return [{ href: "/account", label: t("accountHome"), icon: "bookings", exact: true }];
}
