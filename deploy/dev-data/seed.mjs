// Правдоподобные тестовые данные для ЛОКАЛЬНОЙ базы разработки (не для production!).
// Запуск из корня репозитория при поднятом стеке (docker compose up):
//   node deploy/dev-data/seed.mjs            — сбросить тестовые данные и заполнить заново
// Что делает:
//   1. deploy/dev-data/reset.sql — удаляет брони, гостей, номера, тарифы и пользователей, кроме владельца;
//   2. через API (с проверками и расчётом цен) создаёт сотрудников, категории, номера, тарифы, скидки, услуги,
//      гостей с согласиями и брони: неделя назад, сегодня (упор), две недели вперёд — больше всего ближайшая неделя;
//   3. прошедшие брони создаются через API на дальнюю дату того же дня недели и сдвигаются SQL в прошлое
//      (API не даёт бронировать в прошлом), им ставятся статусы «выехал», «заселён», «неявка», «отменена».
// Имена, телефоны и email вымышленные (email — example.com).

import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const API = process.env.HMS_API ?? "http://localhost:5080/api/v1";
const OWNER_LOGIN = process.env.HMS_OWNER_LOGIN ?? "owner@hms.local";
const OWNER_PASSWORD = process.env.HMS_OWNER_PASSWORD ?? "owner-new-password-1";
const DB_CONTAINER = process.env.HMS_DB_CONTAINER ?? "hms-db-1";
const API_CONTAINER = process.env.HMS_API_CONTAINER ?? "hms-api-1";
const TZ_OFFSET_MIN = 6 * 60; // Asia/Bishkek, без перехода на летнее время

// ---------- Утилиты ----------

let seed = 20261121;
function rnd() {
  // mulberry32 — воспроизводимый «случайный» набор
  seed |= 0;
  seed = (seed + 0x6d2b79f5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
const pick = (arr) => arr[Math.floor(rnd() * arr.length)];
const chance = (p) => rnd() < p;
function weighted(pairs) {
  const total = pairs.reduce((s, [, w]) => s + w, 0);
  let x = rnd() * total;
  for (const [v, w] of pairs) if ((x -= w) < 0) return v;
  return pairs[pairs.length - 1][0];
}

/** Местное время гостиницы в минутах от эпохи (как UTC). */
const localNowMin = Math.floor(Date.now() / 60000) + TZ_OFFSET_MIN;
const MIN_DAY = 1440;
const todayStart = Math.floor(localNowMin / MIN_DAY) * MIN_DAY;
const iso = (min) => new Date(min * 60000).toISOString().slice(0, 10);
const hhmm = (min) => new Date(min * 60000).toISOString().slice(11, 16);
const sqlLocal = (min) => `timestamp '${iso(min)} ${hhmm(min)}' at time zone 'Asia/Bishkek'`;

function sql(text) {
  const dir = mkdtempSync(join(tmpdir(), "hms-seed-"));
  const file = join(dir, "q.sql");
  writeFileSync(file, text);
  execFileSync("docker", ["cp", file, `${DB_CONTAINER}:/tmp/hms-seed.sql`]);
  return execFileSync("docker", ["exec", DB_CONTAINER, "sh", "-c", 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f /tmp/hms-seed.sql'], {
    encoding: "utf8",
  });
}

let token = "";
async function call(method, path, body, headers = {}) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: { "content-type": "application/json", ...(token ? { authorization: `Bearer ${token}` } : {}), ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const err = new Error(`${method} ${path} → ${res.status} ${data?.code ?? ""} ${data?.detail ?? data?.title ?? text}`);
    err.status = res.status;
    err.code = data?.code;
    throw err;
  }
  return data;
}
const key = () => crypto.randomUUID().replace(/-/g, "");

// ---------- 1. Сброс ----------

console.log("1/6 Сброс тестовых данных…");
const photoPaths = sql("\\t on\nselect path from platform.stored_files f join rooms.room_photos p on p.file_id = f.id;")
  .split("\n")
  .map((s) => s.trim())
  .filter(Boolean);
sql(readFileSync(join(here, "reset.sql"), "utf8"));
for (const p of photoPaths) {
  if (/^[\w/.-]+$/.test(p)) execFileSync("docker", ["exec", API_CONTAINER, "rm", "-f", `/app/data/files/${p}`]);
}

const login = await call("POST", "/auth/login", { login: OWNER_LOGIN, password: OWNER_PASSWORD });
token = login.accessToken;

// ---------- 2. Сотрудники ----------

console.log("2/6 Сотрудники…");
const staff = [
  { fullName: "Айжан Токтосунова", email: "admin@hms.local", phone: "+996555100201", role: "Administrator", initialPassword: "admin-dev-password" },
  { fullName: "Нурлан Бейшеналиев", email: "admin2@hms.local", phone: "+996700100202", role: "Administrator", initialPassword: "admin-dev-password" },
  { fullName: "Гульнара Асанова", email: "maid@hms.local", phone: "+996770100203", role: "Housekeeper", initialPassword: "maid-dev-password" },
];
for (const s of staff) await call("POST", "/staff", s);

// ---------- 3. Номерной фонд и цены ----------

console.log("3/6 Категории, номера, тарифы…");
const amenities = Object.fromEntries((await call("GET", "/amenities")).map((a) => [a.name, a.id]));
const types = [
  {
    name: "Стандарт",
    description: "Уютный номер с двуспальной кроватью и душем. Подходит для короткого отдыха и ночёвки.",
    capacity: 2, bedsCount: 1, bedType: "Double", basePrice: 3500, hourlyPrice: 600, sortOrder: 1,
    amenities: ["Wi-Fi", "Телевизор", "Душ", "Фен", "Чайник", "Кондиционер"],
    rooms: [["101", 1], ["102", 1], ["103", 1], ["104", 1], ["105", 1], ["106", 1]],
  },
  {
    name: "Комфорт",
    description: "Просторный номер с большой кроватью, рабочим столом и видом на горы.",
    capacity: 2, bedsCount: 1, bedType: "Queen", basePrice: 4500, hourlyPrice: 800, sortOrder: 2,
    amenities: ["Wi-Fi", "Телевизор", "Душ", "Фен", "Чайник", "Кондиционер", "Холодильник", "Рабочий стол", "Вид на горы"],
    rooms: [["201", 2], ["202", 2], ["203", 2], ["204", 2]],
  },
  {
    name: "Семейный",
    description: "Две комнаты: большая кровать и две отдельные. Для семьи с детьми.",
    capacity: 4, bedsCount: 3, bedType: "Twin", basePrice: 6500, hourlyPrice: 1100, sortOrder: 3,
    amenities: ["Wi-Fi", "Телевизор", "Ванна", "Фен", "Чайник", "Кондиционер", "Холодильник", "Балкон"],
    rooms: [["301", 3], ["302", 3]],
  },
  {
    name: "Люкс",
    description: "Номер с гостиной, ванной и балконом с видом на горы.",
    capacity: 2, bedsCount: 1, bedType: "King", basePrice: 9000, hourlyPrice: 1500, sortOrder: 4,
    amenities: ["Wi-Fi", "Телевизор", "Ванна", "Фен", "Чайник", "Кондиционер", "Холодильник", "Сейф", "Балкон", "Вид на горы"],
    rooms: [["401", 4], ["402", 4]],
  },
];
const rooms = []; // { id, number, type }
for (const t of types) {
  const type = await call("POST", "/room-types", {
    name: t.name, description: t.description, capacity: t.capacity, bedsCount: t.bedsCount, bedType: t.bedType,
    basePrice: t.basePrice, sortOrder: t.sortOrder, version: null, hourlyPrice: t.hourlyPrice,
  });
  t.id = type.id;
  await call("PUT", `/room-types/${t.id}/amenities`, { amenityIds: t.amenities.map((a) => amenities[a]).filter(Boolean) });
  for (const [number, floor] of t.rooms) {
    const room = await call("POST", "/rooms", {
      roomTypeId: t.id, number, floor, capacity: null, bedsCount: null, bedType: null, priceOverride: null, description: null, version: null,
      // Угловой 204 — чуть дороже.
      hourlyPriceOverride: number === "204" ? 900 : null,
    });
    rooms.push({ id: room.id, number, type: t });
  }
}
// Номер 106 — ремонт сантехники: вне продажи.
const outRoom = rooms.find((r) => r.number === "106");
await call("POST", `/rooms/${outRoom.id}/status`, { status: "OutOfService", reason: "Ремонт сантехники, мастер придёт в понедельник" });

const from = iso(todayStart - 30 * MIN_DAY);
const to = iso(todayStart + 700 * MIN_DAY); // тариф — не дольше двух лет
const FRI_SAT = 16 + 32; // Пт + Сб
const FRI_SUN = 16 + 32 + 64;
for (const t of types) {
  await call("POST", "/rates", { roomTypeId: t.id, name: "Выходные ночи", dateFrom: from, dateTo: to, daysOfWeek: FRI_SAT, price: Math.round(t.basePrice * 1.15 / 100) * 100, priority: 10, isActive: true, unit: "Night" });
  await call("POST", "/rates", { roomTypeId: t.id, name: "Выходные часы", dateFrom: from, dateTo: to, daysOfWeek: FRI_SUN, price: Math.round(t.hourlyPrice * 1.2 / 50) * 50, priority: 10, isActive: true, unit: "Hour" });
}
await call("POST", "/discounts", { name: "Долгое проживание", code: "WEEK10", type: "Percent", value: 10, validFrom: null, validTo: null, minNights: 5, minHours: 0 });
await call("POST", "/discounts", { name: "Постоянным гостям", code: "SVOI7", type: "Percent", value: 7, validFrom: null, validTo: null, minNights: 0, minHours: 0 });
await call("POST", "/discounts", { name: "Дневной отдых 4+ часа", code: "DAY4", type: "Amount", value: 300, validFrom: null, validTo: null, minNights: 0, minHours: 4 });
const services = {};
for (const s of [
  { key: "breakfast", name: "Завтрак", description: "Шведский стол с 7:00 до 10:00", price: 450, isPerNight: true, isPublic: true },
  { key: "early", name: "Ранний заезд", description: "С 10:00, если номер свободен", price: 1200, isPerNight: false, isPublic: true },
  { key: "late", name: "Поздний выезд", description: "До 16:00, если номер свободен", price: 1200, isPerNight: false, isPublic: true },
  { key: "transfer", name: "Трансфер из аэропорта", description: "Встреча с табличкой", price: 1800, isPerNight: false, isPublic: true },
  { key: "extraBed", name: "Дополнительная кровать", description: null, price: 800, isPerNight: true, isPublic: false },
]) {
  services[s.key] = (await call("POST", "/services", { name: s.name, description: s.description, price: s.price, isPerNight: s.isPerNight, isPublic: s.isPublic })).id;
}

// ---------- 4. Гости ----------

console.log("4/6 Гости…");
const firstM = ["Азамат", "Бакыт", "Эрлан", "Тимур", "Нурбек", "Данияр", "Алмаз", "Руслан", "Улан", "Чынгыз", "Сергей", "Артём", "Максат", "Жоомарт", "Мирлан"];
const firstF = ["Айгерим", "Жылдыз", "Асель", "Нургуль", "Айпери", "Мээрим", "Елена", "Динара", "Бермет", "Алина", "Каныкей", "Зарина", "Ольга", "Чолпон", "Назира"];
const last = ["Абдыкадыров", "Жумабеков", "Токтогулов", "Осмонов", "Сыдыков", "Мамытов", "Исаков", "Калыев", "Бообеков", "Эшимов", "Иванов", "Кадыров", "Турдубаев", "Алымкулов", "Шаршеев", "Петров", "Усенов", "Касымов"];
const prefixes = ["700", "705", "707", "550", "555", "557", "770", "775", "990", "999", "500", "509"];
const usedPhones = new Set();
function phone() {
  for (;;) {
    const p = `+996${pick(prefixes)}${String(Math.floor(rnd() * 1e6)).padStart(6, "0")}`;
    if (!usedPhones.has(p)) return usedPhones.add(p), p;
  }
}
const notesPool = [
  "Просит тихий номер, подальше от лифта.",
  "Постоянный гость, предпочитает 2 этаж.",
  "Аллергия на перо — подушки синтетические.",
  "Приезжает на машине, нужна парковка.",
  "Оплачивает переводом, просит чек на почту.",
  null, null, null, null,
];
const guests = [];
for (let i = 0; i < 42; i++) {
  const female = i % 2 === 1;
  let surname = pick(last);
  if (female) surname = surname.endsWith("ов") || surname.endsWith("ев") ? `${surname}а` : surname;
  const name = `${female ? pick(firstF) : pick(firstM)} ${surname}`;
  const translit = `guest${i + 1}`;
  const g = await call("POST", "/guests", {
    fullName: name,
    phone: phone(),
    whatsAppPhone: null,
    email: chance(0.45) ? `${translit}@example.com` : null,
    birthDate: chance(0.3) ? `${1965 + Math.floor(rnd() * 38)}-${String(1 + Math.floor(rnd() * 12)).padStart(2, "0")}-${String(1 + Math.floor(rnd() * 28)).padStart(2, "0")}` : null,
    notes: pick(notesPool),
    version: null,
  });
  if (chance(0.85)) await call("POST", `/guests/${g.id}/consents`, { type: "PersonalData", granted: true, source: pick(["FrontDesk", "FrontDesk", "Phone", "Written"]) });
  if (chance(0.5)) await call("POST", `/guests/${g.id}/consents`, { type: "WhatsApp", granted: true, source: pick(["FrontDesk", "WhatsApp", "Phone"]) });
  // Постоянные гости: первые 8 — бронируют чаще.
  guests.push({ id: g.id, name, phone: g.phone, weight: i < 8 ? 5 : 1 });
}
const pickGuest = () => weighted(guests.map((g) => [g, g.weight]));

// ---------- 5. Расписание броней ----------

console.log("5/6 Расписание броней…");
const BUFFER = 30;
const horizonStart = todayStart - 7 * MIN_DAY;
const horizonEnd = todayStart + 15 * MIN_DAY;
const nextHour = Math.ceil((localNowMin + 1) / 60) * 60;

/** Насколько занята гостиница в этот день (ближайшая неделя — плотнее всего). */
function density(dayStart) {
  const d = (dayStart - todayStart) / MIN_DAY;
  const weekday = new Date(dayStart * 60000).getUTCDay(); // 0 — вс
  const weekend = weekday === 5 || weekday === 6 ? 1.25 : 1;
  const base = d < 0 ? 0.8 : d <= 7 ? 1 - d * 0.04 : 0.45 - (d - 8) * 0.04;
  return Math.max(0.1, base * weekend);
}

const plan = [];
for (const room of rooms.filter((r) => r !== outRoom)) {
  let t = horizonStart + 12 * 60 + Math.floor(rnd() * 6) * 60;
  while (t < horizonEnd) {
    const dayStart = Math.floor(t / MIN_DAY) * MIN_DAY;
    const dens = density(dayStart);
    const hourOfDay = (t - dayStart) / 60;
    // Посуточно — чаще семейные и люкс, заезд только в 14:00.
    const nightlyShare = room.type.name === "Семейный" ? 0.55 : room.type.name === "Люкс" ? 0.4 : 0.15;
    if (chance(nightlyShare * 0.5) && hourOfDay <= 14) {
      const start = dayStart + 14 * 60;
      const nights = weighted([[1, 5], [2, 4], [3, 2], [4, 1], [6, 1]]);
      const end = dayStart + nights * MIN_DAY + 12 * 60;
      if (end > horizonEnd) break;
      if (chance(dens)) plan.push({ room, kind: "Nightly", start, end, nights });
      t = end + BUFFER + (chance(0.6) ? 30 + Math.floor(rnd() * 3) * 60 : 30 + Math.floor(rnd() * 8) * 60);
      continue;
    }
    // Почасово: начало — целый час с 08:00 до 23:00; длительность 2–6 ч, чаще 2–3.
    let start = Math.ceil(t / 60) * 60;
    const h = (start - dayStart) / 60;
    if (h < 8) start = dayStart + (8 + Math.floor(rnd() * 3)) * 60;
    if (h > 22) {
      t = dayStart + MIN_DAY + 8 * 60;
      continue;
    }
    const hours = weighted([[2, 40], [3, 30], [4, 15], [5, 8], [6, 7]]);
    const extend = start >= nextHour && start < todayStart + 4 * MIN_DAY && chance(0.12) ? 1 : 0;
    const end = start + (hours + extend) * 60;
    if (end > horizonEnd) break;
    if (chance(dens)) plan.push({ room, kind: "Hourly", start, end, hours, extend });
    // Пауза: днём короче, вечером бывает плотно; иногда номер пустует полдня.
    const gap = weighted([[0, 3], [60, 4], [120, 3], [180, 2], [300, 1], [600, 1]]);
    t = end + BUFFER + gap;
  }
}

// Один опаздывающий гость: подтверждённая бронь началась в прошлом часе, никто не пришёл.
const lateStart = Math.floor(localNowMin / 60) * 60 - 60;
const overlaps = (room, st, en) => plan.some((p) => p.room === room && p.start < en + BUFFER && st < p.end + BUFFER);
const lateRoom = rooms.find((r) => r !== outRoom && !overlaps(r, lateStart, lateStart + 120));
if (lateRoom) plan.push({ room: lateRoom, kind: "Hourly", start: lateStart, end: lateStart + 120, hours: 2, extend: 0, late: true });

plan.sort((a, b) => a.start - b.start);

// Статус по времени: прошедшие — выехали (иногда неявка/отмена), идущие сейчас — заселены, будущие — подтверждены
// (иногда ждут подтверждения или отменены заранее).
for (const p of plan) {
  if (p.late) p.status = "Confirmed";
  else if (p.end <= localNowMin) p.status = weighted([["CheckedOut", 88], ["NoShow", 4], ["Cancelled", 8]]);
  else if (p.start < nextHour) p.status = p.start <= localNowMin ? "CheckedIn" : "Confirmed";
  else if (p.kind === "Nightly" && p.start > todayStart + 2 * MIN_DAY && chance(0.25)) p.status = "Pending";
  else p.status = weighted([["Confirmed", 92], ["Cancelled", 8]]);
}
// Одна почасовая бронь на ближайшие часы ждёт подтверждения (удержание 30 мин — потом истечёт сама).
const soon = plan.find((p) => p.kind === "Hourly" && p.status === "Confirmed" && p.start >= nextHour + 60 && p.start < localNowMin + 6 * 60 && !p.extend);
if (soon) soon.status = "Pending";

const comments = [
  "Гость приедет на такси, позвонит с ресепшена.",
  "Нужна детская кроватка.",
  "Просили номер с видом на горы.",
  "Командировка, нужен счёт на организацию.",
  "Годовщина — подготовить номер.",
  "Поздний заезд, ориентировочно в 23:00.",
];
const cancelReasons = ["Гость изменил планы", "Перенёс поездку", "Нашёл вариант ближе к работе", "Не отвечает на звонки, отмена по просьбе по телефону"];

// ---------- 6. Создание через API ----------

console.log(`6/6 Брони: ${plan.length}…`);
const staging = new Map(); // своя «неделя хранения» у каждой брони номера — без пересечений
const shifts = [];
let created = 0;
let failed = 0;
for (const p of plan) {
  const guest = pickGuest();
  const past = p.start < nextHour;
  // Прошедшие и текущие — на дальнюю дату того же дня недели (40+ недель вперёд, у каждой брони номера своя неделя), затем сдвиг SQL.
  const n = staging.get(p.room) ?? 0;
  if (past) staging.set(p.room, n + 1);
  const offsetDays = past ? 7 * (40 + n) : 0;
  const s = p.start + offsetDays * MIN_DAY;
  const adults = p.room.type.capacity >= 4 ? weighted([[2, 3], [3, 2], [4, 1]]) : weighted([[1, 2], [2, 5]]);
  const children = p.room.type.capacity >= 4 && adults < 4 && chance(0.5) ? 1 : 0;
  const serviceLines = [];
  if (p.kind === "Nightly") {
    if (chance(0.55)) serviceLines.push({ serviceId: services.breakfast, quantity: adults });
    if (chance(0.12)) serviceLines.push({ serviceId: services.transfer, quantity: 1 });
    if (chance(0.1)) serviceLines.push({ serviceId: services.late, quantity: 1 });
  }
  const promo =
    p.kind === "Hourly" && p.hours >= 4 && chance(0.25) ? "DAY4" : guest.weight > 1 && chance(0.3) ? "SVOI7" : p.kind === "Nightly" && p.nights >= 5 ? "WEEK10" : null;
  const body = {
    roomId: p.room.id,
    adults,
    children,
    guestName: guest.name,
    guestPhone: guest.phone,
    guestEmail: null,
    comment: chance(0.15) ? pick(comments) : null,
    promoCode: promo,
    services: serviceLines,
    confirm: p.status !== "Pending",
    guestId: guest.id,
    ...(p.kind === "Hourly"
      ? { kind: "Hourly", date: iso(s), startHour: Number(hhmm(s).slice(0, 2)), hours: p.hours, checkIn: null, checkOut: null }
      : { kind: "Nightly", checkIn: iso(s), checkOut: iso(s + p.nights * MIN_DAY) }),
  };
  let booking;
  try {
    booking = await call("POST", "/bookings", body, { "Idempotency-Key": key() });
  } catch (e) {
    failed++;
    console.warn(`  пропуск (${p.room.number}, ${iso(p.start)} ${hhmm(p.start)}): ${e.message}`);
    continue;
  }
  created++;
  if (p.extend) {
    try {
      booking = await call("POST", `/bookings/${booking.id}/extend`, { units: p.extend, version: booking.version });
    } catch (e) {
      console.warn(`  продление не удалось: ${e.message}`);
    }
  }
  if (!past && p.status === "Cancelled") {
    await call("POST", `/bookings/${booking.id}/cancel`, { reason: pick(cancelReasons), waivePenalty: false });
  }
  if (past) shifts.push({ id: booking.id, days: offsetDays, status: p.status, start: p.start, end: p.end, total: booking.totalAmount, hours: p.hours, nights: p.nights, kind: p.kind });
}

// Прошедшие и текущие брони: сдвиг в прошлое, статус, правдоподобные даты создания и история.
const stmts = ["BEGIN;"];
for (const x of shifts) {
  const interval = `interval '${x.days} days'`;
  const created = x.start - (60 + Math.floor(rnd() * 72 * 60));
  const blocking = x.status === "CheckedIn" || x.status === "Confirmed";
  stmts.push(
    `UPDATE bookings.booking_rooms SET starts_at = starts_at - ${interval}, ends_at = ends_at - ${interval}, blocked_until = blocked_until - ${interval},
       check_in = check_in - ${x.days}, check_out = check_out - ${x.days}, is_blocking = ${blocking} WHERE booking_id = '${x.id}';`,
    `UPDATE bookings.bookings SET starts_at = starts_at - ${interval}, ends_at = ends_at - ${interval}, check_in = check_in - ${x.days}, check_out = check_out - ${x.days},
       status = '${x.status}', created_at = ${sqlLocal(created)}, updated_at = ${sqlLocal(Math.min(x.end, localNowMin))}, confirmed_at = ${sqlLocal(created)} WHERE id = '${x.id}';`,
    `UPDATE bookings.booking_history h SET changed_at = ${sqlLocal(created)},
       new_values = CASE WHEN h.event_type = 'created' THEN h.new_values || jsonb_build_object('startsAt', b.starts_at, 'endsAt', b.ends_at) ELSE h.new_values END
       FROM bookings.bookings b WHERE b.id = h.booking_id AND h.booking_id = '${x.id}';`,
  );
  const hist = (from, to, at) =>
    `INSERT INTO bookings.booking_history (id, booking_id, event_type, old_values, new_values, changed_by, changed_at, comment)
       VALUES (gen_random_uuid(), '${x.id}', 'status_changed', '{"status":"${from}"}', '{"status":"${to}"}', NULL, ${sqlLocal(at)}, NULL);`;
  if (x.status === "CheckedIn" || x.status === "CheckedOut") stmts.push(hist("Confirmed", "CheckedIn", x.start + Math.floor(rnd() * 20)));
  if (x.status === "CheckedOut") stmts.push(hist("CheckedIn", "CheckedOut", x.end - Math.floor(rnd() * 25)));
  if (x.status === "NoShow") {
    const unit = x.kind === "Hourly" ? x.hours : x.nights;
    const penalty = Math.round((x.total / Math.max(1, unit)) * 100) / 100;
    stmts.push(`UPDATE bookings.bookings SET penalty_amount = ${penalty}, penalty_kind = 'NoShow' WHERE id = '${x.id}';`, hist("Confirmed", "NoShow", x.start + 30));
  }
  if (x.status === "Cancelled") {
    const at = x.start - (30 + Math.floor(rnd() * 600));
    const late = x.start - at < (x.kind === "Hourly" ? 120 : 24 * 60);
    const unit = x.kind === "Hourly" ? x.hours : x.nights;
    const penalty = late ? Math.round((x.total / Math.max(1, unit)) * 100) / 100 : 0;
    stmts.push(
      `UPDATE bookings.bookings SET cancelled_at = ${sqlLocal(at)}, cancel_reason = '${pick(cancelReasons)}', penalty_amount = ${penalty},
         penalty_kind = ${late ? "'LateCancel'" : "NULL"} WHERE id = '${x.id}';`,
      hist("Confirmed", "Cancelled", at),
    );
  }
}
// Номера: заселённые — «Занят»; освободившиеся за последний час — «Уборка».
stmts.push(
  `UPDATE rooms.rooms r SET status = 'Occupied' WHERE EXISTS (SELECT 1 FROM bookings.booking_rooms br JOIN bookings.bookings b ON b.id = br.booking_id
     WHERE br.room_id = r.id AND b.status = 'CheckedIn');`,
  `UPDATE rooms.rooms r SET status = 'Cleaning' WHERE status = 'Available' AND EXISTS (SELECT 1 FROM bookings.booking_rooms br JOIN bookings.bookings b ON b.id = br.booking_id
     WHERE br.room_id = r.id AND b.status = 'CheckedOut' AND br.ends_at > now() - interval '60 minutes');`,
  // Гости появились в базе раньше своих первых броней.
  `UPDATE guests.guests g SET created_at = LEAST(g.created_at, (SELECT min(b.created_at) - interval '1 day' FROM bookings.bookings b WHERE b.guest_id = g.id))
     WHERE EXISTS (SELECT 1 FROM bookings.bookings b WHERE b.guest_id = g.id);`,
  "COMMIT;",
);
sql(stmts.join("\n"));

const summary = sql(
  "\\pset footer off\nselect status as \"Статус\", kind as \"Вид\", count(*) as \"Броней\" from bookings.bookings group by 1, 2 order by 1, 2;",
);
console.log(summary);
console.log(`Готово: броней ${created}${failed ? `, пропущено ${failed}` : ""}, гостей ${guests.length}, номеров ${rooms.length}.`);
console.log("Сотрудники (первый вход — смена пароля): admin@hms.local / admin-dev-password, admin2@hms.local / admin-dev-password, maid@hms.local / maid-dev-password");
