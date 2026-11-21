import { Sparkles, Wrench } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { ChecklistTemplate, HousekeepingStaff, HousekeepingTask, MaintenanceRequest, Room } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatHotelLocal } from "@/lib/format";
import { maintenanceSeverityLabel, maintenanceStatusLabel, maintenanceTypeLabel } from "@/lib/housekeeping/labels";
import { cn } from "@/lib/utils";
import {
  AssignForm,
  ChecklistTemplateForm,
  DeleteTaskButton,
  InspectForm,
  MaintenanceReportForm,
  MaintenanceUpdateForm,
  NewTaskForm,
  TaskStatusBadge,
  TaskTiming,
} from "./hk-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Housekeeping"))("title") };
}

const tabs = ["tasks", "done", "maintenance", "checklist"] as const;
type Tab = (typeof tabs)[number];

/** Уборка: доска задач (ждут — убирают — на проверке), выполненные, заявки о неисправностях, чек-лист. */
export default function HousekeepingPage({ searchParams }: PageProps<"/admin/housekeeping">) {
  return (
    <Suspense fallback={<LoadingState />}>
      <HousekeepingContent searchParams={searchParams} />
    </Suspense>
  );
}

async function HousekeepingContent({ searchParams }: Pick<PageProps<"/admin/housekeeping">, "searchParams">) {
  const params = await searchParams;
  const tab: Tab = tabs.includes(params.tab as Tab) ? (params.tab as Tab) : "tasks";
  const [t, user] = await Promise.all([getTranslations("Housekeeping"), getCurrentUser()]);
  const can = (p: string) => user?.permissions.includes(p) ?? false;
  const visibleTabs = tabs.filter((x) => (x === "maintenance" ? can("maintenance.manage") : x === "checklist" ? can("housekeeping.manage") : true));

  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} />
      <nav aria-label={t("title")} className="flex flex-wrap gap-1.5">
        {visibleTabs.map((x) => (
          <Link
            key={x}
            href={x === "tasks" ? "/admin/housekeeping" : `/admin/housekeeping?tab=${x}`}
            aria-current={x === tab ? "page" : undefined}
            className={cn(
              "rounded-full border px-3 py-1 text-sm font-medium transition",
              x === tab ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
            )}
          >
            {t(`tab_${x}`)}
          </Link>
        ))}
      </nav>
      {tab === "tasks" && <TasksBoard canManage={can("housekeeping.manage")} canInspect={can("housekeeping.inspect")} />}
      {tab === "done" && <DoneTasks />}
      {tab === "maintenance" && can("maintenance.manage") && <Maintenance />}
      {tab === "checklist" && can("housekeeping.manage") && <Checklist />}
    </div>
  );
}

function roomOptions(rooms: Room[]) {
  return rooms.filter((r) => !r.isArchived).map((r) => ({ id: r.id, number: r.number }));
}

async function TasksBoard({ canManage, canInspect }: { canManage: boolean; canInspect: boolean }) {
  const [t, tasks, staff, rooms] = await Promise.all([
    getTranslations("Housekeeping"),
    api<HousekeepingTask[]>("/housekeeping/tasks"),
    canManage ? api<HousekeepingStaff[]>("/housekeeping/staff") : null,
    canManage ? api<Room[]>("/rooms") : null,
  ]);
  if (!tasks.ok) return <ErrorState title={t("loadError")} />;
  const columns = [
    { key: "waiting", title: t("colWaiting"), items: tasks.data.filter((x) => ["Pending", "Assigned", "Rejected"].includes(x.status)) },
    { key: "progress", title: t("colProgress"), items: tasks.data.filter((x) => x.status === "InProgress") },
    { key: "inspect", title: t("colInspect"), items: tasks.data.filter((x) => x.status === "Completed") },
  ];
  const staffList = staff?.ok ? staff.data : [];

  return (
    <div className="grid gap-6">
      {canManage && rooms?.ok && (
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-lg">{t("newTask")}</h2>
            </CardTitle>
            <CardDescription>{t("newTaskHint")}</CardDescription>
          </CardHeader>
          <CardContent>
            <NewTaskForm rooms={roomOptions(rooms.data)} />
          </CardContent>
        </Card>
      )}
      {tasks.data.length === 0 ? (
        <EmptyState icon={Sparkles} title={t("noTasks")} description={t("noTasksHint")} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-3">
          {columns.map((c) => (
            <section key={c.key} aria-labelledby={`col-${c.key}`} className="grid content-start gap-3 rounded-2xl bg-muted/40 p-3">
              <h2 id={`col-${c.key}`} className="flex items-center justify-between font-heading text-base">
                {c.title}
                <span className="tabular rounded-full bg-card px-2 text-sm text-muted-foreground">{c.items.length}</span>
              </h2>
              {c.items.length === 0 && <p className="px-1 text-sm text-muted-foreground">{t("columnEmpty")}</p>}
              {c.items.map((task) => (
                <article
                  key={task.id}
                  aria-label={t("roomN", { n: task.roomNumber })}
                  className={cn("grid gap-2 rounded-xl border bg-card p-3", task.priority === "High" && "border-status-cleaning/60", task.isOverdue && "border-destructive/50")}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="font-heading text-lg leading-tight">{t("roomN", { n: task.roomNumber })}</p>
                      <p className="text-xs text-muted-foreground">
                        {task.roomTypeName} · {task.origin === "CheckOut" ? t("originCheckOut") : t("originManual")}
                        {task.priority === "High" && <span className="font-medium text-status-cleaning"> · {t("priorityHigh")}</span>}
                      </p>
                    </div>
                    <TaskStatusBadge status={task.status} />
                  </div>
                  <TaskTiming task={task} />
                  {task.notes && <p className="text-sm">{task.notes}</p>}
                  {task.status === "Rejected" && task.rejectReason && <p className="text-xs text-destructive">{t("rejectedBecause", { reason: task.rejectReason })}</p>}
                  <p className="text-xs text-muted-foreground">
                    {task.assignedToName ? t("assignedTo", { name: task.assignedToName }) : t("unassigned")}
                    {task.status === "InProgress" && task.checklist.length > 0 &&
                      ` · ${t("checklistProgress", { done: task.checklist.filter((i) => i.isDone).length, total: task.checklist.length })}`}
                  </p>
                  {canManage && task.allowedActions.includes("assign") && <AssignForm task={task} staff={staffList} />}
                  {canInspect && task.allowedActions.includes("inspect") && <InspectForm task={task} />}
                  {canManage && task.allowedActions.includes("delete") && (
                    <div className="justify-self-end">
                      <DeleteTaskButton task={task} />
                    </div>
                  )}
                </article>
              ))}
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

async function DoneTasks() {
  const [t, tasks] = await Promise.all([getTranslations("Housekeeping"), api<HousekeepingTask[]>("/housekeeping/tasks?status=done")]);
  if (!tasks.ok) return <ErrorState title={t("loadError")} />;
  if (tasks.data.length === 0) return <EmptyState icon={Sparkles} title={t("noDone")} description={t("noDoneHint")} />;
  return (
    <ul className="grid gap-2">
      {tasks.data.map((task) => (
        <li key={task.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border bg-card px-4 py-3 text-sm">
          <span className="font-medium">{t("roomN", { n: task.roomNumber })}</span>
          <span className="text-muted-foreground">{task.assignedToName ?? "—"}</span>
          <span className="tabular text-muted-foreground">
            {task.inspectedAt && formatHotelLocal(task.inspectedAt)} · {task.inspectedByName ?? t("autoAccepted")}
          </span>
        </li>
      ))}
    </ul>
  );
}

async function Maintenance() {
  const [t, requests, rooms] = await Promise.all([
    getTranslations("Housekeeping"),
    api<MaintenanceRequest[]>("/maintenance-requests"),
    api<Room[]>("/rooms"),
  ]);
  if (!requests.ok) return <ErrorState title={t("loadError")} />;
  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <div className="grid content-start gap-3">
        {requests.data.length === 0 ? (
          <EmptyState icon={Wrench} title={t("noRequests")} description={t("noRequestsHint")} />
        ) : (
          requests.data.map((r) => (
            <article key={r.id} className={cn("grid gap-2 rounded-xl border bg-card p-4", r.severity === "Critical" && "border-destructive/50")}>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="font-heading text-lg">
                  {t("roomN", { n: r.roomNumber })} · {maintenanceTypeLabel[r.type]}
                </p>
                <span className="text-xs text-muted-foreground">
                  {maintenanceStatusLabel[r.status]} · {maintenanceSeverityLabel[r.severity]}
                </span>
              </div>
              <p className="text-sm whitespace-pre-line">{r.description}</p>
              <p className="text-xs text-muted-foreground">
                {formatHotelLocal(r.createdAt)} · {r.reportedByName ?? "—"}
                {r.resolutionNote && ` · ${r.resolutionNote}`}
              </p>
              <MaintenanceUpdateForm request={r} />
            </article>
          ))
        )}
      </div>
      {rooms.ok && (
        <Card className="content-start">
          <CardHeader>
            <CardTitle>
              <h2 className="text-lg">{t("newRequest")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <MaintenanceReportForm rooms={roomOptions(rooms.data)} />
          </CardContent>
        </Card>
      )}
    </div>
  );
}

async function Checklist() {
  const [t, template] = await Promise.all([getTranslations("Housekeeping"), api<ChecklistTemplate>("/housekeeping/checklist-template")]);
  if (!template.ok) return <ErrorState title={t("loadError")} />;
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-lg">{t("templateTitle")}</h2>
        </CardTitle>
        <CardDescription>{t("templateDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <ChecklistTemplateForm template={template.data} />
      </CardContent>
    </Card>
  );
}
