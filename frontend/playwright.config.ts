import { defineConfig, devices } from "@playwright/test";

/**
 * E2E-тесты против запущенного стека (docker compose -f deploy/docker-compose.yml up -d --build).
 * Учётные данные владельца — из переменных окружения (значения по умолчанию — dev-стек после смены пароля).
 */
export default defineConfig({
  testDir: "./e2e",
  // Пароль владельца готовится один раз до всех тестов (на чистой БД CI — первый вход со сменой пароля).
  globalSetup: "./e2e/global-setup.ts",
  fullyParallel: false,
  // Локально — не больше 4 воркеров: при 8 (половина ядер) один сервер Next.js не успевает, сценарии упираются в таймаут 30 с
  // (17 сценариев: 8 воркеров — 38–45 с с падениями, 4 воркера — 23 с без падений). В CI — значение по умолчанию.
  workers: process.env.CI ? undefined : 4,
  retries: 0,
  reporter: "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    locale: "ru-RU",
    trace: "retain-on-failure",
    // Проверка production-сборки локально и в CI: nginx с временным самоподписанным сертификатом.
    ignoreHTTPSErrors: process.env.E2E_IGNORE_HTTPS_ERRORS === "1",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
