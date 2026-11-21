import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { QuickLinks } from "@/components/layout/quick-links";

export default async function OwnerHomePage() {
  const t = await getTranslations("Areas");
  return (
    <div className="grid gap-8">
      <PageHeader title={t("ownerTitle")} description={t("ownerText")} />
      <QuickLinks
        links={[
          { href: "/admin/bookings", title: t("qlBookings"), description: t("qlBookingsText"), icon: "bookings" },
          { href: "/admin/guests", title: t("qlGuests"), description: t("qlGuestsText"), icon: "guests" },
          { href: "/admin/rooms", title: t("qlRooms"), description: t("qlRoomsText"), icon: "rooms" },
          { href: "/admin/room-types", title: t("qlTypes"), description: t("qlTypesText"), icon: "roomTypes" },
          { href: "/owner/pricing", title: t("qlPricing"), description: t("qlPricingText"), icon: "pricing" },
          { href: "/owner/staff", title: t("qlStaff"), description: t("qlStaffText"), icon: "staff" },
          { href: "/owner/settings", title: t("qlSettings"), description: t("qlSettingsText"), icon: "settings" },
          { href: "/owner/integrations", title: t("qlIntegrations"), description: t("qlIntegrationsText"), icon: "integrations" },
        ]}
      />
    </div>
  );
}
