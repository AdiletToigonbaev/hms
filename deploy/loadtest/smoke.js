// Нагрузочный smoke-тест сайта HMS (k6). Не стресс-тест: проверяет, что production-стек
// держит ожидаемую нагрузку гостиницы на 10–30 номеров с запасом, без ошибок и с быстрыми ответами.
//
// Запуск против своего сервера (с другого компьютера):
//   docker run --rm -i -e BASE_URL=https://hotel.example.com grafana/k6:2.4.0 run - < deploy/loadtest/smoke.js
// Локально против prod-local-check (см. docs/development/deployment.md, «Нагрузочный smoke»).
//
// Все запросы приходят с одного IP — как персонал за Wi-Fi стойки или гости за CGNAT оператора.
// Поиск свободных номеров (страница /book) API ограничивает 60 запросами в минуту с IP, поэтому
// он идёт отдельным сценарием ниже лимита; остальные страницы — с запасом выше реального потока.
import http from "k6/http";
import { check, sleep } from "k6";

const base = (__ENV.BASE_URL || "https://localhost").replace(/\/$/, "");
// В k6 нет глобального URL — имя хоста и путь разбираются регулярным выражением.
const host = base.replace(/^https?:\/\//, "").replace(/[:/].*$/, "");
const pathOf = (url) => url.replace(/^https?:\/\/[^/]+/, "");

export const options = {
  // Локальная проверка: имя сайта → контейнер nginx (самоподписанный сертификат).
  hosts: __ENV.TARGET_IP ? { [host]: __ENV.TARGET_IP } : {},
  insecureSkipTLSVerify: __ENV.INSECURE === "1",
  scenarios: {
    browse: {
      executor: "ramping-vus",
      exec: "browse",
      startVUs: 0,
      stages: [
        { duration: "20s", target: 20 },
        { duration: "60s", target: 20 },
        { duration: "10s", target: 0 },
      ],
    },
    search: {
      executor: "constant-arrival-rate",
      exec: "search",
      rate: 40,
      timeUnit: "1m",
      duration: "90s",
      preAllocatedVUs: 5,
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    "http_req_duration{kind:page}": ["p(95)<1500"],
    "http_req_duration{kind:static}": ["p(95)<300"],
    "http_req_duration{kind:search}": ["p(95)<2000"],
    checks: ["rate>0.99"],
  },
};

function isoDate(daysAhead) {
  const d = new Date(Date.now() + daysAhead * 86400000);
  return d.toISOString().slice(0, 10);
}

export function setup() {
  // Страницы категорий номеров — из sitemap (те же адреса, что видят поисковики).
  const res = http.get(`${base}/sitemap.xml`, { tags: { kind: "page" } });
  check(res, { "sitemap 200": (r) => r.status === 200 });
  const rooms = [...String(res.body).matchAll(/<loc>([^<]*\/rooms\/[^<]+)<\/loc>/g)].map((m) => pathOf(m[1]));
  // Статика сборки — из главной страницы (имена с хэшем меняются каждый релиз).
  const home = http.get(`${base}/`, { tags: { kind: "page" } });
  const assets = [...String(home.body).matchAll(/\/_next\/static\/[^"'\s)]+\.(?:js|css|woff2)/g)].map((m) => m[0]);
  return { rooms, assets: [...new Set(assets)].slice(0, 8) };
}

export function browse(data) {
  const home = http.get(`${base}/`, { tags: { kind: "page" } });
  check(home, { "главная 200": (r) => r.status === 200 });

  for (const asset of data.assets) {
    const r = http.get(`${base}${asset}`, { tags: { kind: "static" } });
    check(r, { "статика 200": (x) => x.status === 200 });
  }
  sleep(1 + Math.random() * 2);

  if (data.rooms.length > 0) {
    const room = data.rooms[Math.floor(Math.random() * data.rooms.length)];
    const r = http.get(`${base}${room}`, { tags: { kind: "page" } });
    check(r, { "номер 200": (x) => x.status === 200 });
  }
  sleep(1 + Math.random() * 2);

  const login = http.get(`${base}/login`, { tags: { kind: "page" } });
  check(login, { "вход 200": (r) => r.status === 200 });
  sleep(1 + Math.random() * 3);
}

export function search() {
  // Подбор номеров и расчёт цены: по часам на завтра или по суткам через неделю.
  const query =
    Math.random() < 0.5
      ? `kind=hourly&date=${isoDate(1)}&startHour=14&hours=3&adults=2`
      : `kind=nightly&checkIn=${isoDate(7)}&checkOut=${isoDate(9)}&adults=2`;
  const r = http.get(`${base}/book?${query}`, { tags: { kind: "search" } });
  check(r, {
    "подбор 200": (x) => x.status === 200,
    "без ошибки лимита": (x) => !String(x.body).includes("Слишком много запросов"),
  });
}
