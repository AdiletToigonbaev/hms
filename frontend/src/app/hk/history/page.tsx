import { History } from "lucide-react";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { api } from "@/lib/api/server";
import type { HousekeepingTask } from "@/lib/api/types";
import { formatHotelLocal } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Housekeeping"))("historyTitle") };
}

/** Принятые уборки сотрудника (последние 50). */
export default function Page() {
  return (
    <Suspense fallback={<LoadingState />}>
      <MyHistory />
    </Suspense>
  );
}

async function MyHistory() {
  const [t, tasks] = await Promise.all([getTranslations("Housekeeping"), api<HousekeepingTask[]>("/housekeeping/my/history")]);
  if (!tasks.ok) return <ErrorState title={t("loadError")} />;
  return (
    <div className="mx-auto grid w-full max-w-lg gap-4">
      <h1 className="text-2xl">{t("historyTitle")}</h1>
      {tasks.data.length === 0 ? (
        <EmptyState icon={History} title={t("historyEmpty")} />
      ) : (
        <ul className="grid gap-2">
          {tasks.data.map((task) => (
            <li key={task.id} className="flex items-center justify-between gap-2 rounded-xl border bg-card px-4 py-3">
              <span className="font-medium">{t("roomN", { n: task.roomNumber })}</span>
              <span className="tabular text-sm text-muted-foreground">{task.inspectedAt && formatHotelLocal(task.inspectedAt)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
