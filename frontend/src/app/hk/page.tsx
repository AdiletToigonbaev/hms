import { Sparkles } from "lucide-react";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { MaidTaskCard } from "@/app/admin/housekeeping/hk-forms";
import { api } from "@/lib/api/server";
import { AutoRefresh } from "@/components/auto-refresh";
import type { HousekeepingTask } from "@/lib/api/types";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Housekeeping"))("myTitle") };
}

/** Телефон уборщика: свои задачи и свободные (можно взять). Сначала срочные и с ближайшим сроком. */
export default function Page() {
  return (
    <Suspense fallback={<LoadingState />}>
      <MyTasks />
    </Suspense>
  );
}

async function MyTasks() {
  const [t, tasks] = await Promise.all([getTranslations("Housekeeping"), api<HousekeepingTask[]>("/housekeeping/my/tasks")]);
  if (!tasks.ok) return <ErrorState title={t("loadError")} />;
  const mine = tasks.data.filter((x) => x.assignedTo !== null);
  const free = tasks.data.filter((x) => x.assignedTo === null);
  return (
    <div className="mx-auto grid w-full max-w-lg gap-6">
      <AutoRefresh seconds={30} />
      <h1 className="text-2xl">{t("myTitle")}</h1>
      {tasks.data.length === 0 && <EmptyState icon={Sparkles} title={t("myEmpty")} description={t("myEmptyHint")} />}
      {mine.length > 0 && (
        <section className="grid gap-3" aria-label={t("mine")}>
          {mine.map((task) => (
            <MaidTaskCard key={task.id} task={task} />
          ))}
        </section>
      )}
      {free.length > 0 && (
        <section className="grid gap-3" aria-labelledby="free-title">
          <h2 id="free-title" className="font-heading text-lg">
            {t("free")}
          </h2>
          {free.map((task) => (
            <MaidTaskCard key={task.id} task={task} />
          ))}
        </section>
      )}
    </div>
  );
}
