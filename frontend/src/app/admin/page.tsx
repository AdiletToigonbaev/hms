import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { QuickLinks } from "@/components/layout/quick-links";

export default async function AdminHomePage() {
  const t = await getTranslations("Areas");
  return (
    <div className="grid gap-8">
      <PageHeader title={t("adminTitle")} description={t("adminText")} />
      <QuickLinks
        links={[
          { href: "/admin/bookings", title: t("qlBookings"), description: t("qlBookingsText"), icon: "bookings" },
          { href: "/admin/guests", title: t("qlGuests"), description: t("qlGuestsText"), icon: "guests" },
          { href: "/admin/rooms", title: t("qlRooms"), description: t("qlRoomsText"), icon: "rooms" },
          { href: "/admin/room-types", title: t("qlTypes"), description: t("qlTypesText"), icon: "roomTypes" },
        ]}
      />
    </div>
  );
}
