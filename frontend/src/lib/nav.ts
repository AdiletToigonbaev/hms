import type { NavItem } from "@/components/layout/nav-links";
import type { Role } from "@/lib/api/types";

type Translate = (key: "ownerHome" | "adminHome" | "bookings" | "calendar" | "payments" | "refunds" | "expenses" | "reports" | "audit" | "messages" | "messaging" | "housekeeping" | "hkHistory" | "hkRequests" | "guests" | "pricing" | "rooms" | "roomTypes" | "staff" | "settings" | "integrations" | "hkHome" | "accountHome" | "accountPayments" | "accountProfile" | "toSite") => string;

/** Меню персонала по ролям: владелец видит и свои разделы, и разделы администратора. */
export function staffNav(roles: readonly Role[], t: Translate, permissions: readonly string[] = []): NavItem[] {
  const isOwner = roles.includes("Owner");
  const items: NavItem[] = [
    isOwner
      ? { href: "/owner", label: t("ownerHome"), icon: "dashboard", exact: true }
      : { href: "/admin", label: t("adminHome"), icon: "dashboard", exact: true },
    { href: "/admin/calendar", label: t("calendar"), icon: "calendar" },
    { href: "/admin/bookings", label: t("bookings"), icon: "bookings" },
    { href: "/admin/guests", label: t("guests"), icon: "guests" },
    // Переписка с гостями — у кого есть право «Сообщения» (whatsapp.send); у владельца — всегда.
    ...(isOwner || permissions.includes("whatsapp.send") ? [{ href: "/admin/messages", label: t("messages"), icon: "messages" as const }] : []),
    { href: "/admin/housekeeping", label: t("housekeeping"), icon: "housekeeping" },
    { href: "/admin/payments", label: t("payments"), icon: "payments" },
    { href: "/admin/refunds", label: t("refunds"), icon: "refunds" },
    { href: "/admin/expenses", label: t("expenses"), icon: "expenses" },
    // Отчёты — владельцу и сотруднику с правом «Финансовые отчёты» (доступ к каждому отчёту проверяет API).
    ...(isOwner || permissions.includes("finance.report.view") ? [{ href: "/admin/reports", label: t("reports"), icon: "reports" as const }] : []),
    { href: "/admin/rooms", label: t("rooms"), icon: "rooms" },
    { href: "/admin/room-types", label: t("roomTypes"), icon: "roomTypes" },
  ];
  if (isOwner) {
    items.push(
      { href: "/owner/pricing", label: t("pricing"), icon: "pricing" },
      { href: "/owner/staff", label: t("staff"), icon: "staff" },
      { href: "/owner/audit", label: t("audit"), icon: "audit" },
      { href: "/owner/messages", label: t("messaging"), icon: "messaging" },
      { href: "/owner/settings", label: t("settings"), icon: "settings" },
      { href: "/owner/integrations", label: t("integrations"), icon: "integrations" },
    );
  }
  return items;
}

export function housekeeperNav(t: Translate): NavItem[] {
  return [
    { href: "/hk", label: t("hkHome"), icon: "housekeeping", exact: true },
    { href: "/hk/history", label: t("hkHistory"), icon: "history" },
    { href: "/hk/requests", label: t("hkRequests"), icon: "maintenance" },
  ];
}

export function guestNav(t: Translate): NavItem[] {
  return [
    { href: "/account", label: t("accountHome"), icon: "account", exact: true },
    { href: "/account/payments", label: t("accountPayments"), icon: "payments" },
    { href: "/account/profile", label: t("accountProfile"), icon: "guests" },
    { href: "/", label: t("toSite"), icon: "hotel", exact: true },
  ];
}
