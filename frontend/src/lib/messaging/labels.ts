import type { MessageChannel, MessageKind, MessageStatus, WhatsAppTemplateStatus } from "@/lib/api/types";

/** Подписи статусов доставки (ТЗ §11). Цвет всегда дублируется текстом. */
export const messageStatusLabel: Record<MessageStatus, string> = {
  Queued: "В очереди",
  Sent: "Отправлено",
  Delivered: "Доставлено",
  Read: "Прочитано",
  Failed: "Не отправлено",
  Skipped: "Не отправлялось",
  Received: "Получено",
};

export const messageStatusTone: Record<MessageStatus, string> = {
  Queued: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  Sent: "bg-status-occupied/12 text-status-occupied border-status-occupied/30",
  Delivered: "bg-status-available/12 text-status-available border-status-available/30",
  Read: "bg-status-available/12 text-status-available border-status-available/30",
  Failed: "bg-status-out/12 text-status-out border-status-out/30",
  Skipped: "bg-secondary text-muted-foreground border-border",
  Received: "bg-secondary text-foreground border-border",
};

export const channelLabel: Record<MessageChannel, string> = { Email: "Email", WhatsApp: "WhatsApp" };

export const messageKindLabel: Record<MessageKind, string> = {
  Notification: "Уведомление",
  Manual: "Сотрудник",
  AutoReply: "Автоответ",
  Incoming: "От гостя",
  VerificationCode: "Код подтверждения",
};

export const templateStatusLabel: Record<WhatsAppTemplateStatus, string> = {
  NotSubmitted: "Не подан в Meta",
  Submitted: "На модерации",
  Approved: "Одобрен",
  Rejected: "Отклонён",
  Paused: "Приостановлен",
  Disabled: "Отключён",
};

export const templateStatuses = Object.keys(templateStatusLabel) as WhatsAppTemplateStatus[];

/** Когда закроется окно обслуживания: «ещё 5 ч 20 мин» (правило Meta — 24 часа после сообщения гостя). */
export function windowLeft(closesAt: string | null, now: Date = new Date()): string | null {
  if (!closesAt) return null;
  const minutes = Math.floor((new Date(closesAt).getTime() - now.getTime()) / 60_000);
  if (minutes <= 0) return null;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `ещё ${h} ч ${m} мин` : `ещё ${m} мин`;
}

/** Номер для показа: +996 555 123 456. */
export function formatPhone(phone: string): string {
  const m = /^\+996(\d{3})(\d{3})(\d{3})$/.exec(phone);
  return m ? `+996 ${m[1]} ${m[2]} ${m[3]}` : phone;
}
