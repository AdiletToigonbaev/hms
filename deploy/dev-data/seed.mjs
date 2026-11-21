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
// Правила гостиницы (штрафы, удержание) — данные согласуются с настройками владельца.
const rules = await call("GET", "/hotel/settings");

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
    capacity: 4, bedsCount: 3, bedType: "Twin", basePrice: 6500, hourlyPrice: 1100, sortOrder: 3, cleaningNightlyMinutes: 60,
    amenities: ["Wi-Fi", "Телевизор", "Ванна", "Фен", "Чайник", "Кондиционер", "Холодильник", "Балкон"],
    rooms: [["301", 3], ["302", 3]],
  },
  {
    name: "Люкс",
    description: "Номер с гостиной, ванной и балконом с видом на горы.",
    capacity: 2, bedsCount: 1, bedType: "King", basePrice: 9000, hourlyPrice: 1500, sortOrder: 4, cleaningHourlyMinutes: 45, cleaningNightlyMinutes: 90,
    amenities: ["Wi-Fi", "Телевизор", "Ванна", "Фен", "Чайник", "Кондиционер", "Холодильник", "Сейф", "Балкон", "Вид на горы"],
    rooms: [["401", 4], ["402", 4]],
  },
];
const rooms = []; // { id, number, type }
for (const t of types) {
  const type = await call("POST", "/room-types", {
    name: t.name, description: t.description, capacity: t.capacity, bedsCount: t.bedsCount, bedType: t.bedType,
    basePrice: t.basePrice, sortOrder: t.sortOrder, version: null, hourlyPrice: t.hourlyPrice,
    cleaningHourlyMinutes: t.cleaningHourlyMinutes ?? null, cleaningNightlyMinutes: t.cleaningNightlyMinutes ?? null,
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
const futures = [];
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
  if (!past) futures.push({ id: booking.id, status: p.status, start: p.start, total: booking.totalAmount, kind: p.kind });
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
  // Штрафы — только если владелец включил их в настройках (по умолчанию штрафов нет).
  const unit = x.kind === "Hourly" ? x.hours : x.nights;
  const perUnit = Math.round((x.total / Math.max(1, unit)) * 100) / 100;
  if (x.status === "NoShow") {
    const units = x.kind === "Hourly" ? rules.hourlyPolicy.noShowPenaltyHours : rules.cancellationPolicy.noShowPenaltyNights;
    const penalty = Math.min(x.total, perUnit * Math.min(units, unit));
    stmts.push(
      penalty > 0 ? `UPDATE bookings.bookings SET penalty_amount = ${penalty}, penalty_kind = 'NoShow' WHERE id = '${x.id}';` : "",
      hist("Confirmed", "NoShow", x.start + 30),
    );
  }
  if (x.status === "Cancelled") {
    const at = x.start - (30 + Math.floor(rnd() * 600));
    const freeBefore = x.kind === "Hourly" ? rules.hourlyPolicy.freeCancelMinutesBeforeStart : rules.cancellationPolicy.freeCancelHoursBeforeCheckIn * 60;
    const units = x.kind === "Hourly" ? rules.hourlyPolicy.lateCancelPenaltyHours : rules.cancellationPolicy.lateCancelPenaltyNights;
    const late = units > 0 && x.start - at < freeBefore;
    const penalty = late ? Math.min(x.total, perUnit * Math.min(units, unit)) : 0;
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
);
// Будущие брони оформлены в последние дни (а не все в момент запуска скрипта).
for (const f of futures) {
  const created = Math.min(localNowMin - 20, f.start - 60 - Math.floor(rnd() * 6 * MIN_DAY));
  stmts.push(
    `UPDATE bookings.bookings SET created_at = ${sqlLocal(created)}, confirmed_at = CASE WHEN confirmed_at IS NULL THEN NULL ELSE ${sqlLocal(created)} END WHERE id = '${f.id}';`,
    `UPDATE bookings.booking_history SET changed_at = ${sqlLocal(created)} WHERE booking_id = '${f.id}' AND event_type = 'created';`,
  );
}
// Номера «Ч-2111-07» по дню создания (сначала временные — чтобы не столкнуться с уникальным индексом), затем счётчики дней.
stmts.push(
  `UPDATE bookings.bookings SET number = 'tmp-' || left(id::text, 15);`,
  `WITH n AS (
     SELECT id, kind, (created_at AT TIME ZONE 'Asia/Bishkek')::date AS d,
            row_number() OVER (PARTITION BY hotel_id, (created_at AT TIME ZONE 'Asia/Bishkek')::date ORDER BY created_at, id) AS rn
     FROM bookings.bookings)
   UPDATE bookings.bookings b
   SET number = CASE WHEN n.kind = 'Hourly' THEN 'Ч' ELSE 'С' END || '-' || to_char(n.d, 'DDMM') || '-' || lpad(n.rn::text, 2, '0'),
       number_year = EXTRACT(YEAR FROM n.d)::int
   FROM n WHERE b.id = n.id;`,
  `TRUNCATE bookings.booking_number_counters;`,
  `INSERT INTO bookings.booking_number_counters (hotel_id, day, last)
   SELECT hotel_id, (created_at AT TIME ZONE 'Asia/Bishkek')::date, count(*) FROM bookings.bookings GROUP BY 1, 2;`,
  "COMMIT;",
);
sql(stmts.join("\n"));

await seedFinance();

const summary = sql(
  "\\pset footer off\nselect status as \"Статус\", kind as \"Вид\", count(*) as \"Броней\" from bookings.bookings group by 1, 2 order by 1, 2;",
);
console.log(summary);
console.log(`Готово: броней ${created}${failed ? `, пропущено ${failed}` : ""}, гостей ${guests.length}, номеров ${rooms.length}.`);
console.log("Сотрудники (первый вход — смена пароля): admin@hms.local / admin-dev-password, admin2@hms.local / admin-dev-password, maid@hms.local / maid-dev-password");

// ---------- Финансы: оплаты, долги, возвраты, сторно, расходы ----------

/**
 * Через API (с проверками остатка и идемпотентностью), затем время оплат прошлых броней сдвигается в прошлое SQL
 * при временно отключённой защите от изменения платежей — только в этом скрипте для локальной базы.
 */
async function seedFinance() {
  console.log("Финансы…");
  const backdate = []; // { id, at }
  const balanceOf = (id) => call("GET", `/bookings/${id}/balance`);
  const pay = async (bookingId, amount, type, method, comment = null) =>
    (await call("POST", "/payments/manual", { bookingId, type, method, amount, comment }, { "Idempotency-Key": key() })).payment;
  const method = () => weighted([["Cash", 45], ["MBank", 25], ["CardPos", 18], ["BankTransfer", 7], ["Optima", 5]]);

  let payments = 0;
  // Прошедшие брони: выехавшие почти все оплачены, у некоторых остался долг; неявки — штраф оплачен через раз.
  for (const x of shifts) {
    const b = await balanceOf(x.id);
    if (b.balance <= 0) continue;
    if (x.status === "CheckedOut") {
      if (chance(0.08)) {
        const part = Math.round(b.balance * 0.5);
        if (part > 0) backdate.push({ id: (await pay(x.id, part, "Stay", method())).id, at: x.start + 5 }), payments++;
        continue; // остался долг
      }
      backdate.push({ id: (await pay(x.id, b.balance, "Stay", method())).id, at: x.start + Math.floor(rnd() * 20) });
      payments++;
    } else if (x.status === "CheckedIn") {
      const amount = x.kind === "Nightly" ? Math.round(b.balance / 2) : b.balance;
      backdate.push({ id: (await pay(x.id, amount, "Stay", method())).id, at: x.start + 10 });
      payments++;
    } else if ((x.status === "NoShow" || x.status === "Cancelled") && chance(0.5)) {
      backdate.push({ id: (await pay(x.id, b.balance, "Penalty", method(), "Штраф по правилам гостиницы")).id, at: x.end });
      payments++;
    }
  }

  // Будущие: часть броней с предоплатой (половина или полностью), остальные — оплата при заезде.
  const confirmed = futures.filter((f) => f.status === "Confirmed");
  for (const f of confirmed) {
    if (!chance(f.kind === "Nightly" ? 0.6 : 0.25)) continue;
    const b = await balanceOf(f.id);
    if (b.balance <= 0) continue;
    const amount = chance(0.5) ? b.balance : Math.round(b.balance / 2);
    const p = await pay(f.id, amount, "Prepayment", weighted([["MBank", 50], ["Optima", 15], ["BankTransfer", 20], ["Cash", 15]]));
    // Предоплату внесли за последние дни (но не позже начала брони).
    backdate.push({ id: p.id, at: Math.min(f.start - 60, localNowMin - 30 - Math.floor(rnd() * 3 * MIN_DAY)) });
    payments++;
  }

  // Аннулирование: оплату ошибочно провели наличными, хотя был перевод MBANK.
  const forReversal = confirmed.find((f) => f.kind === "Hourly");
  if (forReversal) {
    const b = await balanceOf(forReversal.id);
    if (b.balance > 0) {
      const wrong = await pay(forReversal.id, b.balance, "Prepayment", "Cash");
      await call("POST", `/payments/${wrong.id}/reverse`, { reason: "Ошиблись способом: оплата была переводом MBANK" }, { "Idempotency-Key": key() });
      await pay(forReversal.id, b.balance, "Prepayment", "MBank", "Перевод MBANK");
      payments += 3;
    }
  }

  // Переплата и возвраты: гость внёс предоплату и отменил заранее (без штрафа).
  const refundables = confirmed.filter((f) => f.start > localNowMin + 2 * MIN_DAY && f !== forReversal).slice(-3);
  for (const [i, f] of refundables.entries()) {
    const b = await balanceOf(f.id);
    if (b.balance <= 0) continue;
    const p = await pay(f.id, b.balance, "Prepayment", "MBank");
    await call("POST", `/bookings/${f.id}/cancel`, { reason: "Гость отменил поездку", waivePenalty: false });
    if (i === 2) continue; // переплата без запроса — видна в «Долги → Переплата»
    const refund = await call("POST", "/refunds", { paymentId: p.id, amount: b.balance, reason: "Отмена брони заранее — предоплата возвращается", method: "MBank" }, { "Idempotency-Key": key() });
    if (i === 0) {
      await call("POST", `/refunds/${refund.id}/approve`, { comment: "Отмена в срок" });
      await call("POST", `/refunds/${refund.id}/mark-paid`, {}, { "Idempotency-Key": key() });
    }
    payments++;
  }

  // Время оплат — как будто их принимали при заезде/предоплате, а не в момент запуска скрипта.
  if (backdate.length > 0) {
    const values = backdate.map((b) => `('${b.id}'::uuid, ${sqlLocal(Math.min(b.at, localNowMin - 1))})`).join(",\n");
    sql(`BEGIN;
ALTER TABLE finance.payments DISABLE TRIGGER payments_immutable;
ALTER TABLE finance.payment_transactions DISABLE TRIGGER payment_transactions_append_only;
UPDATE finance.payments p SET paid_at = v.at, created_at = v.at FROM (VALUES ${values}) AS v(id, at) WHERE p.id = v.id;
UPDATE finance.payment_transactions t SET occurred_at = v.at FROM (VALUES ${values}) AS v(id, at) WHERE t.payment_id = v.id;
ALTER TABLE finance.payment_transactions ENABLE TRIGGER payment_transactions_append_only;
ALTER TABLE finance.payments ENABLE TRIGGER payments_immutable;
COMMIT;`);
  }

  // Расходы за последний месяц.
  const categories = Object.fromEntries((await call("GET", "/expense-categories")).map((c) => [c.name, c.id]));
  const expense = (cat, amount, daysAgo, description, supplier, how = "Cash") =>
    call("POST", "/expenses", {
      categoryId: categories[cat], amount, date: iso(todayStart - daysAgo * MIN_DAY), method: how, supplier, description, version: null,
    }, { "Idempotency-Key": key() });
  let expenses = 0;
  for (let d = 30; d >= 0; d--) {
    if (d % 3 === 0) expenses++, await expense("Бельё и прачечная", 1800 + Math.round(rnd() * 12) * 100, d, "Стирка постельного белья и полотенец", "Прачечная");
    if (d % 7 === 2) expenses++, await expense("Моющие и расходные материалы", 2500 + Math.round(rnd() * 20) * 100, d, "Моющие средства, туалетная бумага, мыло", "Оптовый склад хозтоваров", "BankTransfer");
    if (d % 2 === 1) expenses++, await expense("Продукты (завтраки)", 1200 + Math.round(rnd() * 15) * 100, d, "Продукты для завтраков", "Рынок");
    if (d === 25) expenses++, await expense("Коммунальные услуги", 38400, d, "Электроэнергия за месяц", "Энергосбыт", "BankTransfer");
    if (d === 24) expenses++, await expense("Коммунальные услуги", 9600, d, "Вода и канализация за месяц", "Водоканал", "BankTransfer");
    if (d === 20) expenses++, await expense("Ремонт и обслуживание", 4500, d, "Замена смесителя в номере 106", "Сантехник", "Cash");
    if (d === 15) expenses++, await expense("Зарплата", 95000, d, "Зарплата персонала, первая половина месяца", null, "BankTransfer");
    if (d === 10) expenses++, await expense("Реклама", 6000, d, "Продвижение в соцсетях", "Рекламное агентство", "CardPos");
    if (d === 4) expenses++, await expense("Ремонт и обслуживание", 2200, d, "Ремонт замка двери 203", "Мастер по замкам", "Cash");
  }
  console.log(`  платежей ${payments}, расходов ${expenses}`);
}
