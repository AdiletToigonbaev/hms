import { Wrench } from "lucide-react";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { api } from "@/lib/api/server";
import type { MaintenanceRequest } from "@/lib/api/types";
import { formatHotelLocal } from "@/lib/format";
import { maintenanceStatusLabel, maintenanceTypeLabel } from "@/lib/housekeeping/labels";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Housekeeping"))("requestsTitle") };
}

/** Заявки о неисправностях, которые отправил уборщик, и что с ними сделали. */
export default function Page() {
  return (
    <Suspense fallback={<LoadingState />}>
      <MyRequests />
    </Suspense>
  );
}

async function MyRequests() {
  const [t, requests] = await Promise.all([getTranslations("Housekeeping"), api<MaintenanceRequest[]>("/housekeeping/my/maintenance-requests")]);
  if (!requests.ok) return <ErrorState title={t("loadError")} />;
  return (
    <div className="mx-auto grid w-full max-w-lg gap-4">
      <h1 className="text-2xl">{t("requestsTitle")}</h1>
      <p className="text-sm text-muted-foreground">{t("requestsHint")}</p>
      {requests.data.length === 0 ? (
        <EmptyState icon={Wrench} title={t("noRequests")} />
      ) : (
        <ul className="grid gap-2">
          {requests.data.map((r) => (
            <li key={r.id} className="grid gap-1 rounded-xl border bg-card px-4 py-3">
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium">
                  {t("roomN", { n: r.roomNumber })} · {maintenanceTypeLabel[r.type]}
                </span>
                <span className="text-xs text-muted-foreground">{maintenanceStatusLabel[r.status]}</span>
              </div>
              <p className="text-sm">{r.description}</p>
              <p className="tabular text-xs text-muted-foreground">
                {formatHotelLocal(r.createdAt)}
                {r.resolutionNote && ` · ${r.resolutionNote}`}
              </p>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
