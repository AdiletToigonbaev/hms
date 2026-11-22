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
  retries: 0,
  reporter: "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    locale: "ru-RU",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
