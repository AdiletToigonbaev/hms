import { expect, test, type Page } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
// Временный пароль из Bootstrap (deploy/env.example) и пароль, на который тест меняет его при первом входе.
const ownerInitialPassword = process.env.E2E_OWNER_INITIAL_PASSWORD ?? "owner-dev-password";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

async function submitLogin(page: Page, login: string, password: string) {
  await page.getByLabel("Email или телефон", { exact: true }).fill(login);
  await page.getByLabel("Пароль", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Войти" }).click();
}

/** Вход владельца на открытой странице входа; на чистой БД — с обязательной сменой временного пароля. */
async function loginOwner(page: Page) {
  await submitLogin(page, ownerEmail, ownerPassword);
  const failed = page.getByRole("alert").filter({ hasText: "Неверный логин или пароль" });
  const outcome = await Promise.race([
    page.waitForURL(/\/(owner|change-password)/, { timeout: 15_000 }).then(() => "ok" as const),
    failed.waitFor({ state: "visible", timeout: 15_000 }).then(() => "failed" as const),
  ]);
  if (outcome === "failed") {
    await submitLogin(page, ownerEmail, ownerInitialPassword);
    await expect(page).toHaveURL(/\/change-password$/);
    await page.getByLabel("Текущий пароль").fill(ownerInitialPassword);
    await page.getByLabel("Новый пароль").fill(ownerPassword);
    await page.getByRole("button", { name: "Сменить пароль" }).click();
  }
}

test.beforeAll(async ({ browser }) => {
  // Гарантирует, что у владельца постоянный пароль (на чистом стеке — первый вход).
  const page = await browser.newPage();
  await page.goto("/login");
  await loginOwner(page);
  await expect(page).toHaveURL(/\/owner/);
  await page.close();
});

test("анонимный пользователь перенаправляется на вход с возвратом", async ({ page }) => {
  await page.goto("/owner/staff");
  await expect(page).toHaveURL(/\/login\?next=%2Fowner%2Fstaff/);
  await expect(page.getByRole("heading", { name: "Вход" })).toBeVisible();
});

test("неверный пароль показывает ошибку", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
  await page.getByLabel("Пароль", { exact: true }).fill("wrong-password-000");
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Неверный логин или пароль" })).toBeVisible();
});

test("владелец входит, создаёт администратора; администратор входит и меняет временный пароль", async ({ page, browser }) => {
  // Вход владельца с возвратом на исходную страницу
  await page.goto("/owner/staff");
  await submitLogin(page, ownerEmail, ownerPassword);
  await expect(page).toHaveURL(/\/owner\/staff$/);
  await expect(page.getByRole("heading", { name: "Сотрудники" })).toBeVisible();

  // Создание сотрудника
  const email = `admin-${Date.now()}@hms.local`;
  await page.getByRole("link", { name: "Добавить сотрудника" }).click();
  await page.getByLabel("ФИО", { exact: true }).fill("Администратор Е2Е");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Временный пароль", { exact: true }).fill("temp-password-e2e");
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { name: "Администратор Е2Е" })).toBeVisible();
  await expect(page.getByText("Временный пароль").first()).toBeVisible();

  // Индивидуальное право: финансовые отчёты
  await page.getByRole("checkbox", { name: "Финансовые отчёты", exact: true }).click();
  await expect(page.getByText("выдано")).toBeVisible();
  await page.getByRole("button", { name: "Сохранить права" }).click();
  await expect(page.getByText("Сохранено.").last()).toBeVisible();

  // Первый вход администратора в отдельном браузерном контексте
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await admin.goto("/login");
  await admin.getByLabel("Email или телефон", { exact: true }).fill(email);
  await admin.getByLabel("Пароль", { exact: true }).fill("temp-password-e2e");
  await admin.getByRole("button", { name: "Войти" }).click();
  await expect(admin).toHaveURL(/\/change-password$/);
  await admin.getByLabel("Текущий пароль").fill("temp-password-e2e");
  await admin.getByLabel("Новый пароль").fill("admin-own-password-1");
  await admin.getByRole("button", { name: "Сменить пароль" }).click();
  await expect(admin).toHaveURL(/\/admin$/);
  await expect(admin.getByRole("heading", { level: 1, name: "Рабочий стол" })).toBeVisible();

  // Администратору закрыт раздел владельца
  await admin.goto("/owner/staff");
  await expect(admin).toHaveURL(/\/admin$/);

  // Выход — через окно подтверждения: «Остаться» ничего не меняет
  await admin.getByRole("button", { name: "Выйти" }).filter({ visible: true }).first().click();
  await admin.getByRole("alertdialog").getByRole("button", { name: "Остаться" }).click();
  await expect(admin.getByRole("alertdialog")).toBeHidden();
  await expect(admin).toHaveURL(/\/admin$/);
  await admin.getByRole("button", { name: "Выйти" }).filter({ visible: true }).first().click();
  await admin.getByRole("alertdialog").getByRole("button", { name: "Да, выйти" }).click();
  await expect(admin).toHaveURL(/\/login$/);
  await admin.goto("/admin");
  await expect(admin).toHaveURL(/\/login/);
  await adminContext.close();
});
