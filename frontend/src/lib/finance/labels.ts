import type { PaymentMethod, PaymentStatus, PaymentType, RefundStatus } from "@/lib/api/types";

/** Подписи финансовых справочников. Цвет статуса всегда дублируется текстом. */
export const paymentMethodLabel: Record<PaymentMethod, string> = {
  Cash: "Наличные",
  BankTransfer: "Банковский перевод",
  CardPos: "Карта (терминал)",
  MBank: "MBANK (перевод)",
  Optima: "Optima (перевод)",
};

export const paymentMethods = Object.keys(paymentMethodLabel) as PaymentMethod[];

export const paymentTypeLabel: Record<PaymentType, string> = {
  Prepayment: "Предоплата",
  Stay: "Проживание",
  Service: "Услуги",
  Penalty: "Штраф",
  Other: "Прочее",
  Reversal: "Аннулирование",
};

/** Что можно зарегистрировать вручную (аннулирование создаётся только из платежа). */
export const registrableTypes: PaymentType[] = ["Stay", "Prepayment", "Service", "Penalty", "Other"];

export const paymentStatusLabel: Record<PaymentStatus, string> = {
  Pending: "Ожидает",
  Processing: "Обрабатывается",
  Paid: "Оплачен",
  Failed: "Ошибка оплаты",
  Cancelled: "Отменён",
  RefundPending: "Ожидает возврата",
  PartiallyRefunded: "Частично возвращён",
  Refunded: "Возвращён",
  Reversed: "Аннулирован",
};

export const paymentStatusTone: Record<PaymentStatus, string> = {
  Pending: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  Processing: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  Paid: "bg-status-available/12 text-status-available border-status-available/30",
  Failed: "bg-destructive/10 text-destructive border-destructive/30",
  Cancelled: "bg-status-out/12 text-status-out border-status-out/30",
  RefundPending: "bg-status-reserved/12 text-status-reserved border-status-reserved/30",
  PartiallyRefunded: "bg-status-reserved/12 text-status-reserved border-status-reserved/30",
  Refunded: "bg-status-out/12 text-status-out border-status-out/30",
  Reversed: "bg-status-out/12 text-status-out border-status-out/30",
};

export const refundStatusLabel: Record<RefundStatus, string> = {
  Requested: "Ждёт решения",
  Approved: "Одобрен — выдать деньги",
  Rejected: "Отклонён",
  Paid: "Выплачен",
};

export const refundStatusTone: Record<RefundStatus, string> = {
  Requested: "bg-status-cleaning/12 text-status-cleaning border-status-cleaning/30",
  Approved: "bg-status-reserved/12 text-status-reserved border-status-reserved/30",
  Rejected: "bg-status-out/12 text-status-out border-status-out/30",
  Paid: "bg-status-available/12 text-status-available border-status-available/30",
};

export const refundStatuses = Object.keys(refundStatusLabel) as RefundStatus[];

/** Тип платежа по умолчанию для брони: до заезда — предоплата, отменённая/неявка — штраф, иначе — проживание. */
export function defaultPaymentType(status: string, startsAt: string, now: string): PaymentType {
  if (status === "Cancelled" || status === "NoShow") return "Penalty";
  return startsAt > now ? "Prepayment" : "Stay";
}
