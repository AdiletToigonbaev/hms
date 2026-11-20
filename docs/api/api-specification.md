# Спецификация API (REST, v1)

Базовый путь: `/api/v1`. JSON, UTF-8. Даты проживания — `YYYY-MM-DD`, время — ISO 8601 UTC. Ошибки — `application/problem+json` с полями `type`, `title`, `status`, `code`, `errors`, `traceId`. Пагинация: `?page=&pageSize=`; сортировка `?sort=field,-field`. Идемпотентные POST принимают `Idempotency-Key`. Доступ: **P**ublic, **G**uest, **H**ousekeeper, **A**dmin(с разрешением), **O**wner.

Статус: ✔ MVP (P0), ◐ P1, ⏳ P2 (после доступов банков/WhatsApp).

## Auth / Account
| Метод | Путь | Доступ | Ст. |
|---|---|---|---|
| POST | /auth/register | P | ✔ |
| POST | /auth/login | P | ✔ |
| POST | /auth/refresh | P | ✔ |
| POST | /auth/logout | любой | ✔ |
| POST | /auth/confirm-email | P | ✔ |
| POST | /auth/forgot-password, /auth/reset-password | P | ◐ |
| POST | /auth/2fa/enable, /auth/2fa/verify | A,O | ◐ |
| GET/PUT | /me | любой | ✔ |
| POST | /me/change-password | любой | ✔ |

## Staff / Permissions
| Метод | Путь | Доступ |
|---|---|---|
| GET/POST | /staff | O |
| GET/PUT | /staff/{id} | O |
| POST | /staff/{id}/deactivate, /activate | O |
| GET/PUT | /staff/{id}/permissions | O |
| GET | /permissions, /roles | O |

## Hotel / Settings
| GET/PUT | /hotel | O (GET: A) |
| GET/PUT | /hotel/settings | O (GET: A) |
| GET | /public/hotel-info | P |

## Rooms
| Метод | Путь | Доступ |
|---|---|---|
| GET/POST | /room-types | GET: P/A; POST: A |
| GET/PUT/DELETE(archive) | /room-types/{id} | A |
| GET/POST | /rooms | A |
| GET/PUT | /rooms/{id} | A |
| POST | /rooms/{id}/archive, /restore | A |
| POST | /rooms/{id}/status | A (причина обязательна) |
| GET | /rooms/{id}/status-history | A |
| GET/POST | /amenities | A |
| POST/DELETE | /room-types/{id}/photos | A |

## Pricing
| GET/POST/PUT/DELETE | /rates | O |
| GET/POST/PUT/DELETE | /discounts | O |
| GET/POST/PUT/DELETE | /services | O/A(GET) |

## Availability (публично и для персонала)
| GET | /availability?checkIn=&checkOut=&adults=&children= | P — типы/номера и цена |
| POST | /bookings/quote | P/G/A — расчёт стоимости (без создания) |

## Bookings
| Метод | Путь | Доступ | Заметки |
|---|---|---|---|
| POST | /bookings | G/A | Idempotency-Key; 409 при конфликте |
| GET | /bookings | A,O | фильтры: status, from, to, guest, room, source |
| GET | /bookings/{id} | A,O; G своё | |
| PUT | /bookings/{id} | A | изменение гостей/примечаний |
| POST | /bookings/{id}/change-dates | A | пересчёт цены |
| POST | /bookings/{id}/change-room | A | |
| POST | /bookings/{id}/guests | A | |
| POST | /bookings/{id}/services | A | |
| POST | /bookings/{id}/confirm | A | |
| POST | /bookings/{id}/cancel | A; G своё | правила отмены |
| POST | /bookings/{id}/no-show | A | |
| GET | /bookings/{id}/history | A,O | |
| GET | /bookings/{id}/balance | A,O; G своё | |
| GET | /me/bookings | G | |
| POST | /me/bookings/{id}/cancel | G | |

## Calendar
| GET | /calendar?from=&to=&roomTypeId= | A,O |
| GET | /calendar/arrivals?date= , /departures?date= | A,O |

## Guests
| GET/POST | /guests | A,O |
| GET/PUT | /guests/{id} | A,O |
| GET | /guests/{id}/bookings, /payments, /stays | A,O |
| GET/POST/DELETE | /guests/{id}/documents | A (право documents) |
| GET/PUT | /guests/{id}/consents | A |
| POST | /guests/{id}/merge | O (P1) |

## Front desk
| POST | /bookings/{id}/check-in | A (Idempotency-Key) |
| GET | /bookings/{id}/check-out/preview | A |
| POST | /bookings/{id}/check-out | A (Idempotency-Key) |

## Housekeeping
| GET | /housekeeping/tasks | A (все); H (свои) |
| POST | /housekeeping/tasks | A |
| GET | /housekeeping/tasks/{id} | A, H свои |
| POST | /housekeeping/tasks/{id}/assign | A |
| POST | /housekeeping/tasks/{id}/start | H |
| PUT | /housekeeping/tasks/{id}/checklist/{itemId} | H |
| POST | /housekeeping/tasks/{id}/complete | H |
| POST | /housekeeping/tasks/{id}/inspect | A (approve/reject) |
| GET | /housekeeping/my/history | H |
| GET | /rooms/{id}/cleaning-history | A |
| GET/POST/PUT | /housekeeping/checklist-templates | A/O |
| GET/POST | /maintenance-requests | A; H (POST, свои) |
| PUT | /maintenance-requests/{id} | A |
| POST | /files | A,H (фото) |

## Finance
| GET | /payments | A (право), O |
| POST | /payments/manual | A (право payment.register), Idempotency-Key |
| GET | /payments/{id} | A,O; G своё |
| GET | /me/payments | G |
| POST | /refunds | A (запрос); G (запрос своего) |
| POST | /refunds/{id}/approve, /reject | O или A с лимитом |
| GET | /refunds | A,O |
| GET/POST/PUT | /expenses | A (создание), O |
| POST | /expenses/{id}/void | O |
| GET/POST | /expense-categories | O |

## Online payments (⏳)
| POST | /bookings/{id}/pay | G/A — создаёт платёж у провайдера, возвращает redirect URL |
| GET | /payments/{id}/status | G своё/A |
| POST | /webhooks/mbank | P (проверка подписи) |
| POST | /webhooks/optima | P (проверка подписи) |
| POST | /payments/{id}/refund | O/A (⏳ через банк) |

## WhatsApp (⏳ отправка; ✔ шаблоны/история)
| GET | /whatsapp/conversations, /whatsapp/messages?guestId= | A,O |
| POST | /whatsapp/messages | A (ручная отправка; вне окна 24 ч — только шаблон) |
| GET/POST/PUT | /whatsapp/templates | O |
| GET/POST | /webhooks/whatsapp | P (verify + подписанные события) |

## Reporting
| GET | /reports/dashboard?from=&to= | O |
| GET | /reports/financial?period=day\|week\|month\|year | O, A (право) |
| GET | /reports/bookings, /guests, /rooms, /staff, /payments, /refunds, /expenses | O |
| GET | /reports/{type}/export?format=xlsx\|pdf | O ◐ |

## Audit / System
| GET | /audit-logs | O |
| GET | /health/live, /health/ready | P (внутр.) |

## Коды ошибок домена (примеры)
`booking.room_unavailable` (409), `booking.invalid_transition` (409), `booking.cancel_not_allowed` (422), `payment.duplicate` (409), `payment.amount_mismatch` (422), `room.invalid_status_transition` (409), `auth.forbidden` (403), `validation.failed` (400).

## Итог по объёму
≈ 150 endpoints; MVP (✔) ≈ 110.
