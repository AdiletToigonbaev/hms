import type { HousekeepingTaskStatus, MaintenanceSeverity, MaintenanceStatus, MaintenanceType } from "@/lib/api/types";

export const taskStatusLabel: Record<HousekeepingTaskStatus, string> = {
  Pending: "Ждёт уборщика",
  Assigned: "Назначена",
  InProgress: "Убирают",
  Completed: "Ждёт проверки",
  Inspected: "Принята",
  Rejected: "Вернули на доработку",
};

export const maintenanceTypeLabel: Record<MaintenanceType, string> = {
  Defect: "Неисправность",
  Damage: "Повреждение",
};

export const maintenanceSeverities: MaintenanceSeverity[] = ["Low", "Normal", "High", "Critical"];

export const maintenanceSeverityLabel: Record<MaintenanceSeverity, string> = {
  Low: "Не срочно",
  Normal: "Обычная",
  High: "Срочно",
  Critical: "Номер нельзя продавать",
};

export const maintenanceStatusLabel: Record<MaintenanceStatus, string> = {
  New: "Новая",
  InProgress: "В работе",
  Resolved: "Исправлено",
  Rejected: "Отклонена",
};

/** Цвет статуса задачи — из палитры статусов номера (docs/design/design-direction.md). */
export const taskStatusTone: Record<HousekeepingTaskStatus, string> = {
  Pending: "bg-muted text-foreground",
  Assigned: "bg-status-cleaning/15 text-status-cleaning",
  InProgress: "bg-status-cleaning/25 text-status-cleaning",
  Completed: "bg-primary/15 text-primary",
  Inspected: "bg-status-available/15 text-status-available",
  Rejected: "bg-destructive/10 text-destructive",
};
