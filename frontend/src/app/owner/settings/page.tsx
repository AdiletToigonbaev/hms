import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Hotel, HotelSettings } from "@/lib/api/types";
import { HotelForm, SettingsForm } from "./settings-forms";

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
  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} />
      {!hotel.ok || !settings.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : (
        <div className="grid gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
          <Card className="content-start">
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("hotel")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <HotelForm hotel={hotel.data} />
            </CardContent>
          </Card>
          <Card>
            <CardContent>
              <SettingsForm settings={settings.data} />
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
