# Спецификация API (REST, v1)

Базовый путь: `/api/v1`. JSON, UTF-8. Даты проживания — `YYYY-MM-DD`, время — ISO 8601 UTC. Ошибки — `application/problem+json` с полями `type`, `title`, `status`, `code`, `errors`, `traceId`. Пагинация: `?page=&pageSize=`; сортировка `?sort=field,-field`. Идемпотентные POST (отмечены **IK**) требуют заголовок `Idempotency-Key`.

**Доступ:** P — публично; G — гость (только свои данные); H — уборщик (только свои задачи); A — администратор с указанным разрешением; O — владелец (имеет все разрешения). Коды разрешений — `architecture/security-architecture.md` §3.
**Статус:** ✔ MVP (P0), ◐ P1, ⏳ P2 (после доступов банков/WhatsApp).
Каждая строка — один метод и путь. Детальные схемы запросов/ответов генерируются в OpenAPI (Swashbuckle/Microsoft.AspNetCore.OpenApi) в ходе разработки.

## Аутентификация (реализовано в Sprint 1)
- `POST /auth/login`, `/auth/refresh`, `/me/change-password` возвращают `TokenResponse`: `accessToken` (JWT HS256, 15 мин), `refreshToken` (14 дней), сроки и профиль `user`. API работает только по заголовку `Authorization: Bearer`; cookie не использует.
- Токены получает сервер Next.js (BFF) и хранит в httpOnly-cookie на своём домене; браузер к API напрямую не обращается.
- Refresh-токен одноразовый: при обновлении выдаётся новый. Повтор уже заменённого токена в течение 30 с (гонка вкладок) → `401 auth.refresh_token_rotated`; позже → вся сессия отзывается, `401 auth.refresh_token_invalid`.
- Access-токен проверяется на каждом запросе по актуальному состоянию пользователя (активен, SecurityStamp, права — кэш 30 с): блокировка и смена пароля действуют сразу.
- Пользователь с временным паролем получает `403 auth.password_change_required` везде, кроме `/auth/*`, `GET /me`, `POST /me/change-password`.
- Лимиты частоты (на IP): вход, регистрация, подтверждение и восстановление — 10/мин; обновление и выход — 60/мин; превышение → `429 rate_limited` с `Retry-After`.

## 1. Auth / Account
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| POST | /auth/register | P | ✔ |
| POST | /auth/login | P | ✔ |
| POST | /auth/refresh | P (refresh-cookie) | ✔ |
| POST | /auth/logout | P (refresh-токен в теле; отзывает всю сессию, идемпотентно) | ✔ |
| POST | /auth/confirm-email | P | ✔ |
| POST | /auth/resend-confirmation | P | ✔ |
| POST | /auth/forgot-password | P | ✔ |
| POST | /auth/reset-password | P | ✔ |
| POST | /auth/2fa/setup | A, O | ◐ |
| POST | /auth/2fa/verify | A, O | ◐ |
| POST | /auth/2fa/disable | A, O | ◐ |
| GET | /me | любой авторизованный | ✔ |
| PUT | /me | любой авторизованный | ✔ |
| POST | /me/change-password | любой авторизованный | ✔ |
| GET | /me/consents | G (Sprint 4, модуль Guests; до этого согласия фиксируются в аудите при регистрации) | ✔ |
| PUT | /me/consents | G (Sprint 4) | ✔ |

## 2. Staff / Permissions
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /staff | O (`staff.manage`) | ✔ |
| POST | /staff | O | ✔ |
| GET | /staff/{id} | O | ✔ |
| PUT | /staff/{id} | O | ✔ |
| POST | /staff/{id}/deactivate | O | ✔ |
| POST | /staff/{id}/activate | O | ✔ |
| POST | /staff/{id}/reset-password | O | ✔ |
| GET | /staff/{id}/permissions | O | ✔ |
| PUT | /staff/{id}/permissions | O | ✔ |
| GET | /roles | O | ✔ |
| GET | /roles/{name}/permissions | O | ✔ |
| GET | /permissions | O | ✔ |

## 3. Hotel / Settings / Integrations
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /hotel | A, O | ✔ |
| PUT | /hotel | O (`settings.manage`) | ✔ |
| GET | /hotel/settings | A, O | ✔ |
| PUT | /hotel/settings | O (`settings.manage`) | ✔ |
| GET | /public/hotel-info | P | ✔ |
| GET | /settings/integrations | O (`integrations.manage`) | ✔ |
| GET | /settings/integrations/{provider} | O | ✔ |
| PUT | /settings/integrations/{provider} | O (несекретные параметры, режим, вкл/выкл) | ✔ |
| POST | /settings/integrations/{provider}/test | O (проверка соединения; для банков/WhatsApp — ⏳ после доступов) | ✔ |

## 4. Rooms
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /public/room-types | P (каталог: описание, удобства, фото, цена «от») | ✔ |
| GET | /public/room-types/{id} | P | ✔ |
| GET | /room-types | A, O | ✔ |
| POST | /room-types | A (`room.manage`) | ✔ |
| GET | /room-types/{id} | A, O | ✔ |
| PUT | /room-types/{id} | A (`room.manage`) | ✔ |
| POST | /room-types/{id}/archive | A (`room.manage`) | ✔ |
| POST | /room-types/{id}/restore | A (`room.manage`) | ✔ |
| PUT | /room-types/{id}/amenities | A (`room.manage`) | ✔ |
| POST | /room-types/{id}/photos | A (`room.manage`) | ✔ |
| DELETE | /room-types/{id}/photos/{photoId} | A (`room.manage`) | ✔ |
| PUT | /room-types/{id}/photos/order | A (`room.manage`) | ✔ |
| GET | /rooms | A, O | ✔ |
| POST | /rooms | A (`room.manage`) | ✔ |
| GET | /rooms/{id} | A, O | ✔ |
| PUT | /rooms/{id} | A (`room.manage`) | ✔ |
| DELETE | /rooms/{id} | A (`room.manage`; только без истории, иначе 409 → архив) | ✔ |
| POST | /rooms/{id}/archive | A (`room.manage`) | ✔ |
| POST | /rooms/{id}/restore | A (`room.manage`) | ✔ |
| POST | /rooms/{id}/photos | A (`room.manage`) | ✔ |
| DELETE | /rooms/{id}/photos/{photoId} | A (`room.manage`) | ✔ |
| POST | /rooms/{id}/status | A (`room.status.change`; причина обязательна) | ✔ |
| GET | /rooms/{id}/status-history | A, O | ✔ |
| GET | /rooms/{id}/cleaning-history | A (`housekeeping.manage`) | ✔ |
| GET | /rooms/board | A, O (текущие статусы, включая вычисляемый Reserved) | ✔ |
| GET | /amenities | A, O | ✔ |
| POST | /amenities | A (`room.manage`) | ✔ |
| PUT | /amenities/{id} | A (`room.manage`) | ✔ |
| DELETE | /amenities/{id} | A (`room.manage`; только неиспользуемые) | ✔ |

## 5. Pricing
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /rates | A (`rate.view`), O | ✔ |
| POST | /rates | O (`rate.manage`) | ✔ |
| PUT | /rates/{id} | O | ✔ |
| DELETE | /rates/{id} | O | ✔ |
| GET | /discounts | A (`rate.view`), O | ✔ |
| POST | /discounts | O (`rate.manage`) | ✔ |
| PUT | /discounts/{id} | O | ✔ |
| POST | /discounts/{id}/deactivate | O | ✔ |
| GET | /services | A, O | ✔ |
| POST | /services | O (`rate.manage`) | ✔ |
| PUT | /services/{id} | O | ✔ |
| POST | /services/{id}/deactivate | O | ✔ |
| GET | /public/services | P (услуги, доступные к заказу онлайн) | ✔ |

## 6. Availability / Quote
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /public/availability?checkIn=&checkOut=&adults=&children= | P (категории, свободное количество, цена) | ✔ |
| POST | /public/bookings/quote | P (расчёт без создания; промокод) | ✔ |
| GET | /availability?checkIn=&checkOut=&roomTypeId= | A (`booking.create`; конкретные номера) | ✔ |
| POST | /bookings/quote | A (`booking.create`) | ✔ |

## 7. Bookings
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| POST | /bookings | A (`booking.create`), **IK**; 409 при конфликте | ✔ |
| GET | /bookings | A (`booking.view`); фильтры status, from, to, guestId, roomId, source | ✔ |
| GET | /bookings/{id} | A (`booking.view`) | ✔ |
| PUT | /bookings/{id} | A (`booking.edit`; гости, комментарий, количество) | ✔ |
| POST | /bookings/{id}/reschedule | A (`booking.edit`; новые даты и/или номер, пересчёт цены, 409 при конфликте; используется формой и drag&drop календаря) | ✔ |
| POST | /bookings/{id}/guests | A (`booking.edit`) | ✔ |
| DELETE | /bookings/{id}/guests/{guestId} | A (`booking.edit`) | ✔ |
| POST | /bookings/{id}/services | A (`booking.edit`) | ✔ |
| DELETE | /bookings/{id}/services/{itemId} | A (`booking.edit`) | ✔ |
| PUT | /bookings/{id}/discount | A (`booking.edit`) | ✔ |
| DELETE | /bookings/{id}/discount | A (`booking.edit`) | ✔ |
| POST | /bookings/{id}/confirm | A (`booking.edit`) | ✔ |
| GET | /bookings/{id}/cancel/preview | A (`booking.cancel`; расчёт штрафа) | ✔ |
| POST | /bookings/{id}/cancel | A (`booking.cancel`; исключение из штрафа — `booking.cancel.penalty_override`) | ✔ |
| POST | /bookings/{id}/no-show | A (`booking.edit`) | ✔ |
| GET | /bookings/{id}/history | A (`booking.view`) | ✔ |
| GET | /bookings/{id}/balance | A (`booking.view`) | ✔ |
| POST | /me/bookings | G, **IK**; 409 при конфликте | ✔ |
| GET | /me/bookings | G | ✔ |
| GET | /me/bookings/{id} | G | ✔ |
| GET | /me/bookings/{id}/balance | G | ✔ |
| GET | /me/bookings/{id}/cancel/preview | G | ✔ |
| POST | /me/bookings/{id}/cancel | G | ✔ |

## 8. Calendar
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /calendar?from=&to=&roomTypeId= | A (`calendar.view`) | ✔ |
| GET | /calendar/arrivals?date= | A (`calendar.view`) | ✔ |
| GET | /calendar/departures?date= | A (`calendar.view`) | ✔ |

## 9. Guests
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /guests | A (`guest.view`); поиск, фильтры | ✔ |
| POST | /guests | A (`guest.manage`) | ✔ |
| GET | /guests/{id} | A (`guest.view`) | ✔ |
| PUT | /guests/{id} | A (`guest.manage`) | ✔ |
| DELETE | /guests/{id} | A (`guest.manage`; soft delete) | ✔ |
| GET | /guests/{id}/bookings | A (`guest.view`) | ✔ |
| GET | /guests/{id}/payments | A (`guest.view` + `payment.view`) | ✔ |
| GET | /guests/{id}/stays | A (`guest.view`) | ✔ |
| GET | /guests/{id}/documents | A (`guest.documents.view`; маскированные номера) | ✖ |
| GET | /guests/{id}/documents/{docId} | A (`guest.documents.view`; расшифровка, запись в аудит) | ✖ |
| POST | /guests/{id}/documents | A (`guest.documents.manage`) | ✖ |
| DELETE | /guests/{id}/documents/{docId} | A (`guest.documents.manage`) | ✖ |
| GET | /guests/{id}/consents | A (`guest.view`) | ✔ |
| PUT | /guests/{id}/consents | A (`guest.manage`; источник обязателен) | ✔ |
| POST | /guests/{id}/merge | O | ◐ |

## 10. Front desk
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /bookings/{id}/check-in/preview | A (`checkin.perform`) | ✔ |
| POST | /bookings/{id}/check-in | A (`checkin.perform`), **IK** | ✔ |
| GET | /bookings/{id}/check-out/preview | A (`checkout.perform`; долг, услуги, досрочный выезд) | ✔ |
| POST | /bookings/{id}/check-out | A (`checkout.perform`), **IK** | ✔ |

## 11. Housekeeping / Maintenance / Files
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /housekeeping/tasks | A (`housekeeping.manage`) | ✔ |
| POST | /housekeeping/tasks | A (`housekeeping.manage`) | ✔ |
| GET | /housekeeping/tasks/{id} | A; H — свои | ✔ |
| PUT | /housekeeping/tasks/{id} | A (`housekeeping.manage`; приоритет, заметки) | ✔ |
| DELETE | /housekeeping/tasks/{id} | A (`housekeeping.manage`; только Pending/Assigned) | ✔ |
| POST | /housekeeping/tasks/{id}/assign | A (`housekeeping.manage`) | ✔ |
| POST | /housekeeping/tasks/{id}/start | H (`housekeeping.work`) | ✔ |
| PUT | /housekeeping/tasks/{id}/checklist/{itemId} | H | ✔ |
| POST | /housekeeping/tasks/{id}/complete | H | ✔ |
| POST | /housekeeping/tasks/{id}/photos | H | ◐ |
| POST | /housekeeping/tasks/{id}/inspect | A (`housekeeping.inspect`; approve/reject + причина) | ✔ |
| GET | /housekeeping/my/tasks | H | ✔ |
| GET | /housekeeping/my/history | H | ✔ |
| GET | /housekeeping/checklist-templates | A | ✔ |
| POST | /housekeeping/checklist-templates | A (`housekeeping.manage`) | ✔ |
| PUT | /housekeeping/checklist-templates/{id} | A (`housekeeping.manage`) | ✔ |
| DELETE | /housekeeping/checklist-templates/{id} | A (`housekeeping.manage`) | ✔ |
| GET | /maintenance-requests | A (`maintenance.manage`) | ✔ |
| POST | /maintenance-requests | A, H (`maintenance.create`) | ✔ |
| GET | /maintenance-requests/{id} | A; H — свои | ✔ |
| PUT | /maintenance-requests/{id} | A (`maintenance.manage`; статус, номер → Maintenance) | ✔ |
| GET | /housekeeping/my/maintenance-requests | H | ✔ |
| POST | /files | A, H — общая загрузка; в Sprint 2 не нужна (фото загружаются через `/room-types/{id}/photos`, `/rooms/{id}/photos`), появится с вложениями (Sprint 6–7) | ◐ |
| GET | /files/{id} | P — только публичные файлы (фото номеров); заголовки `nosniff`, `Content-Security-Policy: sandbox`, долгий кэш. Приватные файлы — через эндпоинты модулей-владельцев | ✔ |

## 12. Finance
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /payments | A (`payment.view`) | ✔ |
| GET | /payments/{id} | A (`payment.view`) | ✔ |
| POST | /payments/manual | A (`payment.register`), **IK** | ✔ |
| POST | /payments/{id}/reverse | A (`payment.reverse`), **IK**; причина обязательна | ✔ |
| GET | /me/payments | G | ✔ |
| GET | /refunds | A (`refund.request` или `refund.approve`) | ✔ |
| GET | /refunds/{id} | A | ✔ |
| POST | /refunds | A (`refund.request`), **IK** | ✔ |
| POST | /refunds/{id}/approve | A (`refund.approve`, в пределах лимита) | ✔ |
| POST | /refunds/{id}/reject | A (`refund.approve`) | ✔ |
| POST | /refunds/{id}/mark-paid | A (`refund.approve`), **IK**; фиксация ручной выплаты | ✔ |
| POST | /me/refunds | G, **IK**; запрос по своей брони | ✔ |
| GET | /me/refunds | G | ✔ |
| GET | /expenses | A (`expense.create` — свои; `expense.manage` — все) | ✔ |
| POST | /expenses | A (`expense.create`) | ✔ |
| GET | /expenses/{id} | A (свои) / O | ✔ |
| PUT | /expenses/{id} | O (`expense.manage`) | ✔ |
| POST | /expenses/{id}/void | O (`expense.manage`) | ✔ |
| GET | /expense-categories | A, O | ✔ |
| POST | /expense-categories | O | ✔ |
| PUT | /expense-categories/{id} | O | ✔ |
| POST | /expense-categories/{id}/deactivate | O | ✔ |

## 13. Online payments (⏳)
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| POST | /bookings/{id}/pay | A, **IK** — платёжная ссылка для гостя | ⏳ |
| POST | /me/bookings/{id}/pay | G, **IK** — создаёт платёж у провайдера, возвращает redirect URL | ⏳ |
| GET | /payments/{id}/status | G — свой; A | ⏳ |
| POST | /webhooks/mbank | P (проверка подлинности по документации банка) | ⏳ |
| POST | /webhooks/optima | P (проверка подлинности по документации банка) | ⏳ |
| POST | /refunds/{id}/refund-online | A (`refund.approve`), **IK** — возврат через банк | ⏳ |

## 14. WhatsApp
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /whatsapp/conversations | A (`whatsapp.send`) | ✔ |
| GET | /whatsapp/conversations/{guestId}/messages | A (`whatsapp.send`) | ✔ |
| POST | /whatsapp/conversations/{guestId}/resolve | A (`whatsapp.send`; закрыть передачу оператору) | ✔ |
| POST | /whatsapp/messages | A (`whatsapp.send`; вне окна — только шаблон); реальная отправка ⏳ | ✔ |
| POST | /whatsapp/messages/{id}/retry | A (`whatsapp.send`) | ✔ |
| GET | /whatsapp/templates | A, O | ✔ |
| POST | /whatsapp/templates | O (`whatsapp.templates.manage`) | ✔ |
| PUT | /whatsapp/templates/{id} | O | ✔ |
| POST | /whatsapp/templates/{id}/sync-status | O — статус модерации в Meta | ⏳ |
| GET | /whatsapp/auto-replies | O | ✔ |
| PUT | /whatsapp/auto-replies | O (меню, FAQ, тексты) | ✔ |
| GET | /webhooks/whatsapp | P (verify challenge) | ⏳ |
| POST | /webhooks/whatsapp | P (проверка подписи) | ⏳ |

## 15. Dashboards / Reporting
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /dashboard/owner?from=&to= | O (`dashboard.owner.view`) | ✔ |
| GET | /dashboard/operations?date= | A (`dashboard.operations.view`), O | ✔ |
| GET | /reports/financial?period=day\|week\|month\|year&date= | O; A (`finance.report.view`) | ✔ |
| GET | /reports/bookings | O; A (`finance.report.view`) | ✔ |
| GET | /reports/guests | O | ✔ |
| GET | /reports/rooms | O | ✔ |
| GET | /reports/staff | O | ✔ |
| GET | /reports/payments | O; A (`finance.report.view`) | ✔ |
| GET | /reports/refunds | O; A (`finance.report.view`) | ✔ |
| GET | /reports/expenses | O | ✔ |
| GET | /reports/{type}/export?format=xlsx\|pdf | O; A (`report.export` + право на отчёт) | ✔ |

Общие фильтры отчётов: `from`, `to`, `roomTypeId`, `paymentMethod`, `bookingStatus`, `staffId`.

## 16. Audit / System
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| GET | /audit-logs | O (`audit.view`) | ✔ |
| GET | /audit-logs/{id} | O (`audit.view`) | ✔ |
| GET | /health/live | внутренний (Nginx не публикует) | ✔ |
| GET | /health/ready | внутренний | ✔ |

## Коды ошибок домена (базовый набор)
`validation.failed` (400), `auth.invalid_credentials` (401), `auth.locked_out` (403), `auth.email_not_confirmed` (403), `auth.password_change_required` (403), `auth.refresh_token_invalid` (401), `auth.refresh_token_rotated` (401), `auth.invalid_token` (400), `auth.email_taken` (409), `auth.phone_taken` (409), `auth.wrong_password` (400), `auth.password_rejected` (400), `staff.not_found` (404), `staff.cannot_modify_owner` (403), `staff.permission_not_configurable` (422), `rate_limited` (429), `auth.forbidden` (403), `not_found` (404), `concurrency.conflict` (409), `idempotency.key_reused` (422), `booking.room_unavailable` (409), `booking.invalid_transition` (409), `booking.cancel_not_allowed` (422), `booking.capacity_exceeded` (422), `room.invalid_status_transition` (409), `room.has_history` (409), `payment.duplicate` (409), `payment.amount_mismatch` (422), `payment.already_reversed` (409), `refund.exceeds_paid` (422), `refund.limit_exceeded` (403), `housekeeping.invalid_transition` (409), `whatsapp.no_consent` (422), `whatsapp.outside_window` (422).

## Итог по объёму
Количество endpoints по разделам считается по таблицам выше (строка = endpoint); актуальная цифра — в конце раздела при каждом изменении: **всего 208, из них ✔ 190, ◐ 5, ⏳ 9, ✖ 4** (✖ — исключено решением заказчика: документы гостей не хранятся).

## Реализовано в Sprint 2 — уточнения
- **Фото** (`POST /room-types/{id}/photos`, `POST /rooms/{id}/photos`): `multipart/form-data`, поле `file`; тип определяется по содержимому (JPEG, PNG, WebP), до 10 МБ, не более 20 на категорию или номер; ответ — `PhotoDto` с `url` `/api/v1/files/{fileId}`. Ошибки: `file.unsupported_type`, `file.too_large`, `photo.too_many`.
- **Номера**: `GET /rooms?roomTypeId=&status=&floor=&includeArchived=&search=`; `GET /rooms/board` — неархивные номера по этажам. В `RoomDto` — собственные значения (null — как у категории), итоговые `Effective*`, `manualStatusTargets` (куда можно перевести вручную). `DELETE /rooms/{id}` — только без истории статусов, иначе `409 room.has_history`.
- **Статусы**: `POST /rooms/{id}/status` `{ status, reason }`, причина обязательна; вручную недоступны переходы в/из `Occupied` (`409 room.invalid_status_transition`) — их делают заселение и выселение.
- **Категории**: `GET /room-types?includeArchived=true`; архивирование — только без действующих номеров (`409 room_type.has_active_rooms`); `PUT /room-types/{id}/photos/order` `{ photoIds }`.
- **Каталог** `GET /public/room-types`: только категории, где есть номер в продаже (не в архиве и не «вне продажи»); `priceFrom` — минимальная цена по номерам категории.
- **Оптимистичная блокировка**: `PUT /hotel`, `/hotel/settings`, `/room-types/{id}`, `/rooms/{id}` принимают `version` из последнего ответа; устаревшая версия → `409 concurrency.conflict`.
- **Интеграции**: включить провайдера без реализованного адаптера нельзя (`422 integration.not_available`), без ключей в окружении — `422 integration.secrets_missing`; ключи вида `secret`/`token`/`key` в `publicConfig` запрещены (`400 integration.key_not_allowed`). `POST /settings/integrations/Email/test` ставит тестовое письмо текущему пользователю.

## Реализовано в Sprint 3 — уточнения
- **Тарифы** `GET /rates?roomTypeId=`; тело `POST/PUT /rates`: `{ roomTypeId, name, dateFrom, dateTo, daysOfWeek (маска 1–127, Пн=1…Вс=64), price, priority 0–100, isActive }`; `dateFrom/dateTo` — первая и последняя ночь включительно, период ≤ 2 лет. Удаление тарифа не меняет созданные брони (цена в снимке).
- **Скидки** `POST/PUT /discounts` `{ name, code?, type: Percent|Amount, value, validFrom?, validTo?, minNights }`; код хранится в верхнем регистре, повтор → `409 discount.code_taken`; есть `POST /discounts/{id}/activate`. **Услуги** — то же с `POST /services/{id}/activate`. Чтение тарифов, скидок и услуг доступно также с `booking.create` (администратор при оформлении брони).
- **Подбор** `GET /availability?checkIn=&checkOut=&adults=&children=&roomTypeId=` → номера в продаже, вмещающие гостей и свободные на `[checkIn, checkOut)`, с полным расчётом `quote`. `GET /public/availability` (тот же запрос, лимит 60/мин с IP) → только категории: `{ roomTypeId, roomTypeName, capacity, availableCount, nights, totalFrom }`, без номеров комнат. Ошибки: `400 validation.failed` (выезд не позже заезда, > 60 ночей), `422 booking.past_dates`.
- **Расчёт** `POST /bookings/quote` `{ roomId, checkIn, checkOut, adults, children, promoCode?, services?: [{ serviceId, quantity }] }` и `POST /public/bookings/quote` (вместо `roomId` — `roomTypeId`) → `PriceQuote { nights: [{ date, price, source }], accommodationTotal, discountId, discountName, discountAmount, services, servicesTotal, total }`. Ошибки промокода: `pricing.promo_not_found` (404), `pricing.promo_inactive`, `pricing.promo_out_of_period`, `pricing.promo_min_nights` (422); `pricing.service_unavailable` (404).
- **Создание брони** `POST /bookings` (заголовок **`Idempotency-Key`** обязателен, 8–100 символов `[A-Za-z0-9_-]`) `{ roomId, checkIn, checkOut, adults, children, guestName, guestPhone?, guestEmail?, comment?, promoCode?, services?, confirm }` → `201 BookingDto`; `confirm: false` — статус `Pending` (номер уже занят). Повтор с тем же ключом и телом — тот же ответ и заголовок `Idempotent-Replayed: true`; с другим телом — `422 idempotency.key_reused`; без ключа — `400 idempotency.key_required`; параллельный повтор — `409 idempotency.in_progress`. Ошибки: `409 booking.room_unavailable`, `422 booking.capacity_exceeded`, `422 booking.room_not_sellable`, `422 booking.past_dates`, `404 booking.room_not_found`.
- **Список** `GET /bookings?status=&from=&to=&roomId=&search=&page=&pageSize=` — `search` по номеру брони, имени, телефону, email; `from/to` — брони, затрагивающие период. **Изменение** `PUT /bookings/{id}` `{ guestName, guestPhone?, guestEmail?, adults, children, comment?, version? }` (без смены дат и номера — `reschedule` в Sprint 6 вместе с календарём). **Подтверждение** `POST /bookings/{id}/confirm`; **отмена** `POST /bookings/{id}/cancel` `{ reason }` — без штрафов (штрафы и `cancel/preview` — Sprint 5 с платежами). `BookingDto.allowedActions` — доступные действия по машине статусов.
- Ещё не реализованы (по плану): `reschedule`, гости брони, изменение услуг и скидки после создания, `no-show`, `balance` — Sprint 4–6; `/me/bookings` — Sprint 8.

## Почасовые брони и продление (доработка Sprint 3)
- **Вид брони** в `GET /availability`, `GET /public/availability`, `POST /bookings/quote`, `POST /public/bookings/quote`, `POST /bookings`: `kind=Nightly` (по умолчанию; `checkIn`, `checkOut`) или `kind=Hourly` (`date`, `startHour` 0–23 — начало в целый час, `hours`). Ошибки почасовой брони: `422 booking.hours_out_of_range` (вне `hourlyMinHours..hourlyMaxHours` настроек), `422 booking.start_in_past`, `422 booking.room_not_hourly` (нет цены часа). Пересечение с учётом уборки → `409 booking.room_unavailable`.
- **Ответы**: `BookingDto`/`BookingListItemDto` получили `kind`, `startsAt`, `endsAt` (время гостиницы `yyyy-MM-ddTHH:mm`), `hours`; `BookingDto` — `extensionsAmount`, `extensions[]`; `allowedActions` может содержать `extend`. `PriceQuote` — `kind` и `hours[]` (`from`, `to`, `hours`, `unitPrice`, `amount`, `source`; подряд идущие часы с одной ценой объединены). `PublicAvailabilityDto` — `kind`, `units`.
- **Продление**: `GET /bookings/{id}/extend/quote?units=` (`booking.edit`) → `{ kind, units, newEndsAt, amount, price, maxUnits, nextBookingStartsAt }`; `POST /bookings/{id}/extend` `{ units, version }` (`booking.edit`) → `BookingDto`. Ошибки: `409 booking.extend_not_allowed` (не Confirmed/CheckedIn), `409 booking.extension_conflict` (текст с крайним временем), `422 booking.extension_too_long`, `409 concurrency.conflict`.
- **Цены и номера**: `SaveRateRequest.unit` `Night|Hour`; `SaveRoomTypeRequest.hourlyPrice`, `SaveRoomRequest.hourlyPriceOverride`; `RoomDto.effectiveHourlyPrice`. **Настройки**: `hourlyMinHours`, `hourlyMaxHours`, `cleaningBufferMinutes` в `/hotel/settings`; `GET /public/hotel-info` отдаёт `hourlyMinHours`, `hourlyMaxHours`.
- **Номера сегодня**: `RoomDto.occupiedUntil` (номер сейчас по брони — до этого времени гостиницы) и `nextBookingAt` (ближайшая бронь сегодня); `displayStatus = Reserved`, если свободный номер занят бронью сейчас или сегодня есть бронь. Каталог `GET /public/room-types` — `hourlyPriceFrom` (минимальная цена часа, null — по часам не продаётся).
- **Скидки**: `minHours` (0–24) — условие для почасовых броней (`422 pricing.promo_min_hours`); `minNights` — только для посуточных.
- **Список броней**: фильтр `kind=Hourly|Nightly`; экран по умолчанию открывается на брони сегодняшнего дня.

## Реализовано в Sprint 4 — уточнения (Guests)
- `GET /guests?search=&filter=&page=&pageSize=` (`guest.view`) — поиск по имени, email и цифрам телефона; `filter`: `withBookings`, `whatsApp`, `noContact`. Обезличенные гости в список не попадают.
- `GET /guests/lookup?q=` (`guest.view` или `booking.create`) — до 8 гостей для выбора при брони.
- `POST /guests`, `PUT /guests/{id}` (`guest.manage`) `{ fullName, phone?, whatsAppPhone?, email?, birthDate?, notes?, version? }`; нужен телефон или email; дубль → `409 guest.duplicate` с полем `guestId`; устаревшая версия → `409 concurrency.conflict`.
- `GET /guests/{id}/bookings` (`guest.view` + `booking.view`); `GET /guests/{id}/consents` — история; `POST /guests/{id}/consents` `{ type: PersonalData|WhatsApp, granted, source: Portal|FrontDesk|Phone|WhatsApp|Written }` (`guest.manage`). Вместо `PUT /guests/{id}/consents` из плана — `POST` (запись в журнал, а не замена).
- `POST /guests/{id}/anonymize` (`guest.anonymize`, только владелец) вместо `DELETE /guests/{id}`; ошибки `guest.anonymized`, `guest.has_account`.
- `POST /bookings`: необязательный `guestId`; без него гость находится по телефону/email или создаётся. `BookingDto`/`BookingListItemDto` — поле `guestId`. Ошибки: `404 guest.not_found`, `422 guest.anonymized`.
- Регистрация на сайте (`POST /auth/register`) создаёт карточку гостя (или привязывает существующую по email/телефону) и записывает согласия.
- ProblemDetails может содержать доменные поля (например, `guestId`) — без персональных данных.
- Права: `guest.documents.*` удалены (документы не храним), добавлено `guest.anonymize` (только владелец). `GET/POST /guests/{id}/documents`, `POST /guests/{id}/merge` — не реализуются (✖ / P1).
