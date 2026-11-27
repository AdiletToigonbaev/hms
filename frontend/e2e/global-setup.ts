import { chromium, expect, type FullConfig } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
// Временный пароль из Bootstrap (deploy/env.example) и постоянный, на который он меняется при первом входе.
const ownerInitialPassword = process.env.E2E_OWNER_INITIAL_PASSWORD ?? "owner-dev-password";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

/**
 * Один раз до всех тестов: у владельца постоянный пароль. На чистой БД (CI) — первый вход со сменой временного пароля.
 * Без этого тесты из разных файлов, идущие параллельно, входили бы раньше, чем пароль сменён.
 */
export default async function globalSetup(config: FullConfig) {
  const { baseURL = "http://localhost:3000", ignoreHTTPSErrors } = config.projects[0].use;
  const browser = await chromium.launch();
  const page = await browser.newPage({ baseURL, locale: "ru-RU", ignoreHTTPSErrors });
  try {
    const login = async (password: string) => {
      await page.goto("/login");
      await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
      await page.getByLabel("Пароль", { exact: true }).fill(password);
      await page.getByRole("button", { name: "Войти" }).click();
      const failed = page.getByRole("alert").filter({ hasText: "Неверный логин или пароль" });
      return Promise.race([
        page.waitForURL(/\/(owner|change-password)/, { timeout: 30_000 }).then(() => true),
        failed.waitFor({ state: "visible", timeout: 30_000 }).then(() => false),
      ]);
    };

    if (await login(ownerPassword)) return;
    // Чистая база: временный пароль → обязательная смена.
    if (!(await login(ownerInitialPassword))) throw new Error("Владелец не входит ни с постоянным, ни с временным паролем.");
    await expect(page).toHaveURL(/\/change-password$/);
    await page.getByLabel("Текущий пароль").fill(ownerInitialPassword);
    await page.getByLabel("Новый пароль").fill(ownerPassword);
    await page.getByRole("button", { name: "Сменить пароль" }).click();
    await expect(page).toHaveURL(/\/owner/, { timeout: 30_000 });
  } finally {
    await browser.close();
  }
}
