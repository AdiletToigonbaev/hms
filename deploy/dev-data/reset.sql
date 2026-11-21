-- Сброс тестовых данных ЛОКАЛЬНОЙ базы разработки (не для production!).
-- Удаляет брони, гостей, номера, категории, фото номеров, тарифы, скидки, услуги и всех пользователей, кроме владельца.
-- Оставляет: владельца (и его пароль), гостиницу и её настройки, интеграции, справочник удобств.
-- Журнал аудита защищён от удаления на уровне БД и остаётся как есть.
BEGIN;

TRUNCATE bookings.booking_extensions, bookings.booking_history, bookings.booking_services, bookings.booking_rooms, bookings.bookings;
ALTER SEQUENCE bookings.booking_number_seq RESTART WITH 1;

TRUNCATE guests.guest_consents, guests.guests;

TRUNCATE pricing.room_rates, pricing.discounts, pricing.services;

-- Фото номеров: строки в platform.stored_files удаляются здесь, сами файлы — скриптом seed.mjs в контейнере api.
CREATE TEMP TABLE photo_files ON COMMIT DROP AS SELECT file_id FROM rooms.room_photos;
TRUNCATE rooms.room_photos, rooms.room_status_history, rooms.room_type_amenities;
DELETE FROM platform.stored_files WHERE id IN (SELECT file_id FROM photo_files);
DELETE FROM rooms.rooms;
DELETE FROM rooms.room_types;

-- Пользователи, кроме владельца (сотрудники и гости из E2E).
CREATE TEMP TABLE drop_users ON COMMIT DROP AS
  SELECT id FROM identity.users WHERE normalized_email IS DISTINCT FROM 'OWNER@HMS.LOCAL';
DELETE FROM identity.refresh_tokens WHERE user_id IN (SELECT id FROM drop_users);
DELETE FROM identity.user_permissions WHERE user_id IN (SELECT id FROM drop_users);
DELETE FROM identity.user_roles WHERE user_id IN (SELECT id FROM drop_users);
DELETE FROM identity.user_claims WHERE user_id IN (SELECT id FROM drop_users);
DELETE FROM identity.user_logins WHERE user_id IN (SELECT id FROM drop_users);
DELETE FROM identity.user_tokens WHERE user_id IN (SELECT id FROM drop_users);
DELETE FROM identity.users WHERE id IN (SELECT id FROM drop_users);

TRUNCATE platform.idempotency_keys;

COMMIT;
