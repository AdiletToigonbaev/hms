"use client";

import { AlertTriangle, CheckCircle2, Circle, Clock, LogIn, Wrench } from "lucide-react";
import { useTranslations } from "next-intl";
import { useActionState, useEffect, useRef, useState, useTransition } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormAlert } from "@/components/forms/form-alert";
import { NativeSelect, SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { ChecklistTemplate, HousekeepingStaff, HousekeepingTask, HousekeepingTaskStatus, MaintenanceRequest } from "@/lib/api/types";
import { hotelTimeZone } from "@/i18n/config";
import { hotelToday } from "@/lib/bookings/status";
import { formatHotelRelative } from "@/lib/format";
import {
  maintenanceSeverities,
  maintenanceSeverityLabel,
  maintenanceStatusLabel,
  maintenanceTypeLabel,
  taskStatusLabel,
  taskStatusTone,
} from "@/lib/housekeeping/labels";
import { cn } from "@/lib/utils";
import {
  assignTaskAction,
  createMaintenanceAction,
  createTaskAction,
  deleteTaskAction,
  inspectTaskAction,
  saveChecklistTemplateAction,
  setChecklistItemAction,
  taskStepAction,
  updateMaintenanceAction,
} from "./actions";

const textarea =
  "min-h-20 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-base outline-none md:text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive";

function newKey() {
  return crypto.randomUUID().replace(/-/g, "");
}

export function TaskStatusBadge({ status }: { status: HousekeepingTaskStatus }) {
  return <span className={cn("inline-flex rounded-full px-2 py-0.5 text-xs font-medium", taskStatusTone[status])}>{taskStatusLabel[status]}</span>;
}

/** Срок, просрочка и ближайший заезд — то, по чему уборщик и администратор выбирают очередность. */
export function TaskTiming({ task }: { task: HousekeepingTask }) {
  const t = useTranslations("Housekeeping");
  const today = hotelToday(hotelTimeZone);
  // Срок сегодня — просто время («до 02:50»), иначе «до завтра 09:00».
  const due = (local: string) => (local.startsWith(today) ? local.slice(11, 16) : formatHotelRelative(local, today));
  return (
    <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
      {task.dueAt && (
        <span className={cn("inline-flex items-center gap-1", task.isOverdue && "font-medium text-destructive")}>
          <Clock className="size-3.5" aria-hidden />
          {task.isOverdue ? t("overdue", { time: due(task.dueAt) }) : t("dueAt", { time: due(task.dueAt) })}
        </span>
      )}
      {task.nextArrivalAt && (
        <span className="inline-flex items-center gap-1">
          <LogIn className="size-3.5" aria-hidden />
          {t("nextArrival", { time: formatHotelRelative(task.nextArrivalAt, today) })}
        </span>
      )}
    </div>
  );
}

export function NewTaskForm({ rooms }: { rooms: { id: string; number: string }[] }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(createTaskAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (state.ok) formRef.current?.reset();
  }, [state]);
  const e = state.fieldErrors ?? {};
  return (
    <form ref={formRef} action={action} className="grid gap-3 sm:grid-cols-[10rem_10rem_1fr_auto] sm:items-end" noValidate>
      <div className="sm:col-span-4">
        <FormAlert state={state} success={t("taskCreated")} />
      </div>
      <SelectField name="roomId" label={t("room")} error={e.roomId} defaultValue="">
        <option value="" disabled>
          {t("chooseRoom")}
        </option>
        {rooms.map((r) => (
          <option key={r.id} value={r.id}>
            {r.number}
          </option>
        ))}
      </SelectField>
      <SelectField name="priority" label={t("priority")} defaultValue="Normal">
        <option value="Normal">{t("priorityNormal")}</option>
        <option value="High">{t("priorityHigh")}</option>
      </SelectField>
      <div className="grid gap-1.5">
        <Label htmlFor="notes">{t("notes")}</Label>
        <input id="notes" name="notes" maxLength={500} placeholder={t("notesPlaceholder")} className="h-9 rounded-lg border border-input bg-card px-2.5 text-base outline-none md:text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
      </div>
      <SubmitButton pending={pending}>{t("createTask")}</SubmitButton>
    </form>
  );
}

/** Назначить или переназначить уборщика (в скобках — сколько у него открытых задач). */
export function AssignForm({ task, staff }: { task: HousekeepingTask; staff: HousekeepingStaff[] }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(assignTaskAction, {});
  if (staff.length === 0) return <p className="text-xs text-muted-foreground">{t("noStaff")}</p>;
  return (
    <form action={action} className="grid gap-1.5">
      <input type="hidden" name="id" value={task.id} />
      <FormAlert state={state} />
      <div className="flex gap-2">
        <NativeSelect name="userId" aria-label={t("assignTo")} defaultValue={task.assignedTo ?? ""} className="h-8">
          <option value="" disabled>
            {t("assignTo")}
          </option>
          {staff.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name} ({s.openTasks})
            </option>
          ))}
        </NativeSelect>
        <SubmitButton pending={pending} size="sm" variant="outline">
          {task.assignedTo ? t("reassign") : t("assign")}
        </SubmitButton>
      </div>
    </form>
  );
}

/** Проверка уборки: принять (номер «Свободен») или вернуть с причиной. */
export function InspectForm({ task }: { task: HousekeepingTask }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(inspectTaskAction, {});
  const [rejecting, setRejecting] = useState(false);
  return (
    <form action={action} className="grid gap-2" noValidate>
      <input type="hidden" name="id" value={task.id} />
      <FormAlert state={state} />
      {rejecting && (
        <div className="grid gap-1">
          <Label htmlFor={`reason-${task.id}`}>{t("rejectReason")}</Label>
          <textarea id={`reason-${task.id}`} name="reason" maxLength={500} className={textarea} aria-invalid={state.fieldErrors?.reason ? true : undefined} />
          {state.fieldErrors?.reason && <p className="text-xs text-destructive">{state.fieldErrors.reason}</p>}
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        {!rejecting && (
          <SubmitButton pending={pending} size="sm" name="decision" value="approve">
            {t("approve")}
          </SubmitButton>
        )}
        {rejecting ? (
          <>
            <SubmitButton pending={pending} size="sm" variant="destructive" name="decision" value="reject">
              {t("rejectSend")}
            </SubmitButton>
            <Button type="button" size="sm" variant="ghost" onClick={() => setRejecting(false)}>
              {t("cancel")}
            </Button>
          </>
        ) : (
          <Button type="button" size="sm" variant="outline" onClick={() => setRejecting(true)}>
            {t("reject")}
          </Button>
        )}
      </div>
    </form>
  );
}

export function DeleteTaskButton({ task }: { task: HousekeepingTask }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(deleteTaskAction, {});
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={action}>
      <input type="hidden" name="id" value={task.id} />
      <FormAlert state={state} />
      <SubmitButton
        pending={pending}
        size="sm"
        variant="ghost"
        className="text-muted-foreground"
        onClick={(e) => {
          e.preventDefault();
          setAsking(true);
        }}
      >
        {t("delete")}
      </SubmitButton>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("deleteTitle", { room: task.roomNumber })}
        description={t("deleteText")}
        confirmLabel={t("deleteYes")}
        cancelLabel={t("cancel")}
        destructive
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}

/** Шаблон чек-листа: по пункту на строку. Изменения — для новых задач; начатые задачи не меняются. */
export function ChecklistTemplateForm({ template }: { template: ChecklistTemplate }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(saveChecklistTemplateAction, {});
  return (
    <form action={action} className="grid max-w-xl gap-3" noValidate>
      <input type="hidden" name="version" value={template.version} />
      <FormAlert state={state} success={t("templateSaved")} />
      <Label htmlFor="items">{t("templateItems")}</Label>
      <textarea
        id="items"
        name="items"
        rows={Math.max(8, template.items.length + 2)}
        defaultValue={template.items.join("\n")}
        className={textarea}
        aria-invalid={state.fieldErrors?.items ? true : undefined}
        aria-describedby="items-hint"
      />
      <p id="items-hint" className="text-xs text-muted-foreground">
        {state.fieldErrors?.items ?? t("templateHint")}
      </p>
      <div>
        <SubmitButton pending={pending}>{t("save")}</SubmitButton>
      </div>
    </form>
  );
}

/** Решение по заявке: в работу (можно закрыть номер на ремонт), исправлено или отклонено. */
export function MaintenanceUpdateForm({ request }: { request: MaintenanceRequest }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(updateMaintenanceAction, {});
  const canClose = request.roomStatus === "Available" || request.roomStatus === "Cleaning";
  return (
    <form action={action} className="grid gap-2" noValidate>
      <input type="hidden" name="id" value={request.id} />
      <input type="hidden" name="version" value={request.version} />
      <FormAlert state={state} />
      <div className="flex flex-wrap gap-2">
        <NativeSelect name="status" aria-label={t("colStatus")} defaultValue={request.status === "New" ? "InProgress" : "Resolved"} className="h-8 w-auto">
          {(["InProgress", "Resolved", "Rejected"] as const).map((s) => (
            <option key={s} value={s}>
              {maintenanceStatusLabel[s]}
            </option>
          ))}
        </NativeSelect>
        <input
          name="note"
          maxLength={500}
          placeholder={t("note")}
          aria-label={t("note")}
          aria-invalid={state.fieldErrors?.note ? true : undefined}
          className="h-8 min-w-40 flex-1 rounded-lg border border-input bg-card px-2.5 text-base outline-none md:text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive"
        />
        <SubmitButton pending={pending} size="sm">
          {t("save")}
        </SubmitButton>
      </div>
      {state.fieldErrors?.note && <p className="text-xs text-destructive">{state.fieldErrors.note}</p>}
      {canClose && (
        <label className="flex items-center gap-2 text-xs">
          <Checkbox name="closeRoom" value="true" />
          {t("closeRoom")}
        </label>
      )}
    </form>
  );
}

/** Сообщить о неисправности или повреждении — уборщик прямо из задачи, администратор — из раздела. */
export function MaintenanceReportForm({ roomId, taskId, rooms, onDone }: { roomId?: string; taskId?: string; rooms?: { id: string; number: string }[]; onDone?: () => void }) {
  const t = useTranslations("Housekeeping");
  const [state, action, pending] = useActionState<FormState, FormData>(createMaintenanceAction, {});
  const [key, setKey] = useState(newKey);
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => {
    // Новый ключ после ответа: повтор той же заявки не создаст дубль, следующая — создаст.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (state.ok !== undefined) setKey(newKey());
    if (state.ok) {
      formRef.current?.reset();
      onDone?.();
    }
  }, [state, onDone]);
  const e = state.fieldErrors ?? {};
  return (
    <form ref={formRef} action={action} className="grid gap-3" noValidate>
      <input type="hidden" name="idempotencyKey" value={key} />
      {taskId && <input type="hidden" name="taskId" value={taskId} />}
      {roomId && <input type="hidden" name="roomId" value={roomId} />}
      <FormAlert state={state} success={t("reported")} />
      {!roomId && rooms && (
        <SelectField name="roomId" label={t("room")} error={e.roomId} defaultValue="">
          <option value="" disabled>
            {t("chooseRoom")}
          </option>
          {rooms.map((r) => (
            <option key={r.id} value={r.id}>
              {r.number}
            </option>
          ))}
        </SelectField>
      )}
      <div className="grid gap-3 min-[420px]:grid-cols-2">
        <SelectField name="type" label={t("type")} defaultValue="Defect" error={e.type}>
          {(["Defect", "Damage"] as const).map((x) => (
            <option key={x} value={x}>
              {maintenanceTypeLabel[x]}
            </option>
          ))}
        </SelectField>
        <SelectField name="severity" label={t("severity")} defaultValue="Normal" error={e.severity}>
          {maintenanceSeverities.map((x) => (
            <option key={x} value={x}>
              {maintenanceSeverityLabel[x]}
            </option>
          ))}
        </SelectField>
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor={`description-${taskId ?? "new"}`}>{t("descriptionLabel")}</Label>
        <textarea
          id={`description-${taskId ?? "new"}`}
          name="description"
          maxLength={1000}
          placeholder={t("descriptionPlaceholder")}
          className={textarea}
          aria-invalid={e.description ? true : undefined}
        />
        {e.description && <p className="text-xs text-destructive">{e.description}</p>}
      </div>
      <div>
        <SubmitButton pending={pending} variant="outline" className="h-11 w-full sm:w-auto">
          <Wrench aria-hidden />
          {t("report")}
        </SubmitButton>
      </div>
    </form>
  );
}

/** Шаг уборщика одной кнопкой: взять, начать, завершить. */
function StepButton({ task, step, label, variant }: { task: HousekeepingTask; step: "take" | "start" | "complete"; label: string; variant?: "outline" }) {
  const [state, action, pending] = useActionState<FormState, FormData>(taskStepAction, {});
  return (
    <form action={action} className="grid gap-2">
      <input type="hidden" name="id" value={task.id} />
      <input type="hidden" name="step" value={step} />
      <FormAlert state={state} />
      <SubmitButton pending={pending} size="lg" variant={variant} className="h-12 w-full text-base">
        {label}
      </SubmitButton>
    </form>
  );
}

/**
 * Карточка задачи на телефоне уборщика: номер, срок, заметки, чек-лист крупными строками (палец),
 * кнопки шага. Отметки пунктов сохраняются сразу. Персональных данных гостя здесь нет.
 */
export function MaidTaskCard({ task: initial }: { task: HousekeepingTask }) {
  const t = useTranslations("Housekeeping");
  const [task, setTask] = useState(initial);
  const [error, setError] = useState<FormState>({});
  const [pendingItem, setPendingItem] = useState<string | null>(null);
  const [, startTransition] = useTransition();
  const [reporting, setReporting] = useState(false);
  useEffect(() => {
    // Сервер прислал свежую задачу (после шага) — показываем её.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTask(initial);
  }, [initial]);

  const working = task.status === "InProgress";
  const done = task.checklist.filter((i) => i.isDone).length;
  const toggle = (itemId: string, value: boolean) => {
    setPendingItem(itemId);
    startTransition(async () => {
      const result = await setChecklistItemAction(task.id, itemId, value);
      if (result.ok) {
        setTask(result.task);
        setError({});
      } else {
        setError(result.state);
      }
      setPendingItem(null);
    });
  };

  return (
    <article className={cn("grid gap-4 rounded-2xl border bg-card p-4 shadow-xs", task.priority === "High" && "border-status-cleaning/60")} aria-labelledby={`task-${task.id}`}>
      <header className="grid gap-1">
        <div className="flex items-center justify-between gap-2">
          <h2 id={`task-${task.id}`} className="font-heading text-2xl">
            {t("roomN", { n: task.roomNumber })}
          </h2>
          <TaskStatusBadge status={task.status} />
        </div>
        <p className="text-sm text-muted-foreground">
          {task.roomTypeName}
          {task.priority === "High" && <span className="ml-2 font-medium text-status-cleaning">· {t("priorityHigh")}</span>}
        </p>
        <TaskTiming task={task} />
      </header>

      {task.notes && <p className="rounded-lg bg-muted/60 px-3 py-2 text-sm">{task.notes}</p>}
      {task.status === "Rejected" && task.rejectReason && (
        <p role="status" className="flex gap-2 rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
          <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
          {t("rejectedBecause", { reason: task.rejectReason })}
        </p>
      )}

      {(working || task.status === "Completed") && (
        <section aria-label={t("checklist")} className="grid gap-1">
          <p className="text-xs text-muted-foreground">{t("checklistProgress", { done, total: task.checklist.length })}</p>
          <FormAlert state={error} />
          <ul className="grid gap-1">
            {task.checklist.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  disabled={!working || pendingItem === item.id}
                  aria-pressed={item.isDone}
                  onClick={() => toggle(item.id, !item.isDone)}
                  className={cn(
                    "flex min-h-12 w-full items-center gap-3 rounded-xl border px-3 text-left text-base transition",
                    item.isDone ? "border-status-available/40 bg-status-available/10" : "bg-card hover:border-primary/40",
                    pendingItem === item.id && "opacity-60",
                  )}
                >
                  {item.isDone ? <CheckCircle2 className="size-5 shrink-0 text-status-available" aria-hidden /> : <Circle className="size-5 shrink-0 text-muted-foreground" aria-hidden />}
                  <span className={cn(item.isDone && "text-muted-foreground line-through")}>{item.text}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="grid gap-2">
        {task.allowedActions.includes("take") && <StepButton task={task} step="take" label={t("take")} />}
        {task.allowedActions.includes("start") && <StepButton task={task} step="start" label={task.status === "Rejected" ? t("restart") : t("start")} />}
        {task.allowedActions.includes("complete") && (
          <>
            {done < task.checklist.length && <p className="text-center text-xs text-muted-foreground">{t("completeHint")}</p>}
            <StepButton key={done < task.checklist.length ? "waiting" : "ready"} task={task} step="complete" label={t("complete")} />
          </>
        )}
        {task.status === "Completed" && <p className="text-center text-sm text-muted-foreground">{t("waitingInspection")}</p>}
      </div>

      {task.assignedTo && task.status !== "Completed" && (
        <div className="border-t pt-3">
          {reporting ? (
            <div className="grid gap-2">
              <MaintenanceReportForm roomId={task.roomId} taskId={task.id} />
              <Button type="button" variant="ghost" className="h-11 w-full" onClick={() => setReporting(false)}>
                {t("hide")}
              </Button>
            </div>
          ) : (
            <Button type="button" variant="ghost" className="h-11 w-full text-muted-foreground" onClick={() => setReporting(true)}>
              <Wrench aria-hidden />
              {t("reportProblem")}
            </Button>
          )}
        </div>
      )}
    </article>
  );
}
