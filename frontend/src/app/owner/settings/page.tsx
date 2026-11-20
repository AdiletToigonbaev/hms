import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { api } from "@/lib/api/server";
import type { Hotel, HotelSettings } from "@/lib/api/types";
import { SettingsNav } from "./settings-nav";
import {
  BookingRulesForm,
  HotelForm,
  PaymentForm,
  RoomsCleaningForm,
  SiteGuestsForm,
  SoonBadge,
} from "./settings-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Settings"))("title") };
}

export default async function SettingsPage() {
  const [t, errors, hotel, settings] = await Promise.all([
    getTranslations("Settings"),
    getTranslations("Errors"),
    api<Hotel>("/hotel"),
    api<HotelSettings>("/hotel/settings"),
  ]);

  if (!hotel.ok || !settings.ok) {
    return (
      <div className="grid gap-6">
        <PageHeader title={t("title")} description={t("description")} />
        <ErrorState title={errors("unknown")} />
      </div>
    );
  }

  const s = settings.data;
  const sections = [
    {
      id: "section-hotel",
      title: t("secHotel"),
      text: t("secHotelText"),
      soon: false,
      body: <HotelForm hotel={hotel.data} rules={s.rules} />,
    },
    {
      id: "section-rules",
      title: t("secRules"),
      text: t("secRulesText"),
      note: t("secRulesNote"),
      soon: true,
      body: <BookingRulesForm settings={s} />,
    },
    {
      id: "section-rooms",
      title: t("secRooms"),
      text: t("secRoomsText"),
      soon: false,
      body: <RoomsCleaningForm settings={s} />,
    },
    {
      id: "section-payment",
      title: t("secPayment"),
      text: t("secPaymentText"),
      note: t("secPaymentNote"),
      soon: true,
      body: <PaymentForm settings={s} />,
    },
    {
      id: "section-site",
      title: t("secSite"),
      text: t("secSiteText"),
      soon: false,
      body: <SiteGuestsForm settings={s} />,
    },
  ];

  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="grid gap-6 lg:grid-cols-[13rem_minmax(0,1fr)]">
        {/* Разделы: на ПК — меню слева (подсвечивает текущий раздел), на телефоне — ряд ссылок сверху */}
        <SettingsNav
          label={t("sections")}
          sections={sections.map(({ id, title }) => ({ id, title }))}
        />

        <div className="grid gap-6">
          {sections.map((section) => (
            <section
              key={section.id}
              id={section.id}
              aria-labelledby={`${section.id}-title`}
              className="grid scroll-mt-32 gap-4 rounded-2xl lg:scroll-mt-6 border bg-card p-5 shadow-xs transition-[box-shadow,border-color] duration-500 focus-within:border-primary/40 focus-within:shadow-md data-[flash]:border-primary data-[flash]:ring-4 data-[flash]:ring-primary/25"
            >
              <header className="grid gap-1">
                <div className="flex flex-wrap items-center gap-2">
                  <h2
                    id={`${section.id}-title`}
                    tabIndex={-1}
                    className="text-xl outline-none"
                  >
                    {section.title}
                  </h2>
                  {section.soon && <SoonBadge />}
                </div>
                <p className="text-sm text-muted-foreground">{section.text}</p>
                {section.note && (
                  <p className="text-xs text-status-cleaning">{section.note}</p>
                )}
              </header>
              {section.body}
            </section>
          ))}
        </div>
      </div>
    </div>
  );
}
