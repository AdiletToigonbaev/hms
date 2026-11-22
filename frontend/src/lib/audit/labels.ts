/** Подписи действий журнала аудита. Неизвестный код показывается как есть — новые действия не теряются. */
export const auditActionLabel: Record<string, string> = {
  "auth.login.succeeded": "Вход в систему",
  "auth.login.failed": "Неудачная попытка входа",
  "auth.logout": "Выход из системы",
  "auth.registered": "Регистрация гостя на сайте",
  "auth.email_confirmed": "Подтверждение email",
  "auth.password_reset.requested": "Запрос сброса пароля",
  "auth.password_reset.completed": "Пароль сброшен",
  "auth.refresh.reuse_detected": "Повторное использование сессии (подозрение на кражу)",
  "account.updated": "Изменён профиль",
  "account.password_changed": "Изменён пароль",
  "account.deleted": "Гость удалил аккаунт",
  "staff.created": "Добавлен сотрудник",
  "staff.updated": "Изменён сотрудник",
  "staff.activated": "Сотрудник активирован",
  "staff.deactivated": "Сотрудник отключён",
  "staff.password_reset": "Сброшен пароль сотрудника",
  "staff.permissions.changed": "Изменены права сотрудника",
  "hotel.updated": "Изменены данные гостиницы",
  "hotel.settings.updated": "Изменены настройки гостиницы",
  "integration.updated": "Изменена интеграция",
  "room.created": "Создан номер",
  "room.updated": "Изменён номер",
  "room.deleted": "Удалён номер",
  "room.archived": "Номер в архиве",
  "room.restored": "Номер восстановлен",
  "room.status_changed": "Изменён статус номера",
  "room_type.created": "Создана категория",
  "room_type.updated": "Изменена категория",
  "room_type.archived": "Категория в архиве",
  "room_type.restored": "Категория восстановлена",
  "room_type.amenities_changed": "Изменены удобства категории",
  "room_type.photo_added": "Добавлено фото категории",
  "room_type.photo_deleted": "Удалено фото категории",
  "amenity.created": "Добавлено удобство",
  "amenity.deleted": "Удалено удобство",
  "rate.created": "Создан тариф",
  "rate.updated": "Изменён тариф",
  "rate.deleted": "Удалён тариф",
  "discount.created": "Создана скидка",
  "discount.updated": "Изменена скидка",
  "discount.activated": "Скидка включена",
  "discount.deactivated": "Скидка выключена",
  "service.created": "Добавлена услуга",
  "service.updated": "Изменена услуга",
  "booking.created": "Создана бронь",
  "booking.updated": "Изменена бронь",
  "booking.confirmed": "Бронь подтверждена",
  "booking.cancelled": "Бронь отменена",
  "booking.expired": "Бронь истекла",
  "booking.no_show": "Неявка",
  "booking.rescheduled": "Бронь перенесена",
  "booking.extended": "Бронь продлена",
  "booking.checked_in": "Заселение",
  "booking.checked_out": "Выселение",
  "guest.created": "Создан гость",
  "guest.updated": "Изменён гость",
  "guest.updated_by_guest": "Гость изменил профиль",
  "guest.anonymized": "Данные гостя удалены",
  "guest.anonymized_by_guest": "Гость удалил свои данные",
  "guest.consent.granted": "Согласие дано",
  "guest.consent.revoked": "Согласие отозвано",
  "guest.consent_granted": "Согласие дано",
  "guest.consent_withdrawn": "Согласие отозвано",
  "payment.registered": "Принят платёж",
  "payment.reversed": "Платёж аннулирован",
  "refund.requested": "Запрошен возврат",
  "refund.requested_by_guest": "Гость запросил возврат",
  "refund.approved": "Возврат одобрен",
  "refund.rejected": "Возврат отклонён",
  "refund.paid": "Возврат выплачен",
  "expense.created": "Добавлен расход",
  "expense.updated": "Изменён расход",
  "expense.voided": "Расход аннулирован",
  "expense.category.created": "Добавлена категория расходов",
  "expense.category.updated": "Изменена категория расходов",
  "expense.category.activated": "Категория расходов включена",
  "expense.category.deactivated": "Категория расходов выключена",
  "housekeeping.task_created": "Создана задача уборки",
  "housekeeping.task_updated": "Изменена задача уборки",
  "housekeeping.task_deleted": "Удалена задача уборки",
  "housekeeping.task_assigned": "Назначен уборщик",
  "housekeeping.task_taken": "Уборщик взял задачу",
  "housekeeping.task_started": "Уборка начата",
  "housekeeping.task_completed": "Уборка закончена",
  "housekeeping.task_accepted": "Уборка принята",
  "housekeeping.task_rejected": "Уборка возвращена",
  "housekeeping.checklist_updated": "Изменён чек-лист",
  "maintenance.created": "Заявка о неисправности",
  "maintenance.updated": "Изменена заявка о неисправности",
  "report.exported": "Выгружен отчёт",
};

/** Группы действий для фильтра (префикс кода). */
export const auditGroups: { prefix: string; label: string }[] = [
  { prefix: "booking.", label: "Брони" },
  { prefix: "payment.", label: "Платежи" },
  { prefix: "refund.", label: "Возвраты" },
  { prefix: "expense.", label: "Расходы" },
  { prefix: "guest.", label: "Гости" },
  { prefix: "housekeeping.", label: "Уборка" },
  { prefix: "maintenance.", label: "Неисправности" },
  { prefix: "room", label: "Номера и категории" },
  { prefix: "rate.", label: "Тарифы" },
  { prefix: "discount.", label: "Скидки" },
  { prefix: "staff.", label: "Сотрудники и права" },
  { prefix: "hotel.", label: "Настройки гостиницы" },
  { prefix: "auth.", label: "Входы и пароли" },
  { prefix: "report.", label: "Выгрузки отчётов" },
];

export function auditLabel(action: string): string {
  return auditActionLabel[action] ?? action;
}

/** Ссылка на объект действия в кабинете (если для него есть страница). */
export function auditEntityHref(entityType: string | null, entityId: string | null): string | null {
  if (!entityType || !entityId || !/^[0-9a-f-]{36}$/i.test(entityId)) return null;
  switch (entityType) {
    case "Booking":
      return `/admin/bookings/${entityId}`;
    case "Payment":
      return `/admin/payments/${entityId}`;
    case "Guest":
      return `/admin/guests/${entityId}`;
    case "Room":
      return `/admin/rooms/${entityId}`;
    case "RoomType":
      return `/admin/room-types/${entityId}`;
    case "Expense":
      return `/admin/expenses/${entityId}`;
    default:
      return null;
  }
}
