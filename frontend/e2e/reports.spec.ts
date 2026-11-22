import { expect, test } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

test("владелец: обзор с показателями и графиками, отчёт с фильтром и выгрузка PDF/Excel", async ({ page }) => {
  await page.goto("/owner");
  await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
  await page.getByLabel("Пароль", { exact: true }).fill(ownerPassword);
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page).toHaveURL(/\/owner$/);

  // Обзор: деньги разделены — начислено, поступления, прибыль двумя способами
  await expect(page.getByRole("heading", { level: 2, name: "Деньги" })).toBeVisible();
  await expect(page.getByText("Начисленная выручка", { exact: true })).toBeVisible();
  await expect(page.getByText("Прибыль (по поступлениям)", { exact: true })).toBeVisible();
  await expect(page.getByText("Прибыль (по начислению)", { exact: true })).toBeVisible();
  await expect(page.getByRole("img", { name: "Начислено и поступило по дням" })).toBeVisible();
  // Табличное представление графика
  await page.getByRole("region", { name: "Начислено и поступило по дням" }).getByRole("button", { name: "Таблица" }).click();
  await expect(page.getByRole("region", { name: "Начислено и поступило по дням" }).getByRole("table")).toBeVisible();

  // Отчёт по платежам за неделю, способ — наличные
  await page.getByRole("link", { name: "Отчёты" }).first().click();
  await expect(page.getByRole("heading", { level: 2, name: "Финансовый отчёт" })).toBeVisible();
  await page.getByRole("link", { name: "Платежи", exact: true }).click();
  await expect(page.getByRole("heading", { level: 2, name: "Отчёт по платежам" })).toBeVisible();
  await page.getByRole("link", { name: "Неделя", exact: true }).click();
  await expect(page).toHaveURL(/period=week/);
  await page.getByLabel("Способ оплаты").selectOption({ label: "Наличные" });
  await page.getByRole("button", { name: "Показать" }).click();
  await expect(page.getByText("Способ оплаты: Наличные")).toBeVisible();

  // Время внутри дня: сегодня с 06:00 до 22:00 — в заголовке отчёта и в выгрузке
  await page.getByRole("link", { name: "Сегодня", exact: true }).click();
  await expect(page).toHaveURL(/period=day/);
  await page.locator("#fromHour").selectOption({ label: "06:00" });
  await page.locator("#toHour").selectOption({ label: "22:00" });
  await page.getByRole("button", { name: "Показать" }).click();
  await expect(page).toHaveURL(/fromHour=6/);
  await expect(page.getByText(/06:00–22:00/)).toBeVisible();
  await expect(page.getByLabel("Способ оплаты")).toHaveValue("Cash");

  // Выгрузка: файлы приходят с правильным типом
  const pdf = await page.request.get(await page.getByRole("link", { name: "PDF" }).getAttribute("href").then((h) => h!));
  expect(pdf.status()).toBe(200);
  expect(pdf.headers()["content-type"]).toContain("application/pdf");
  expect((await pdf.body()).subarray(0, 5).toString()).toBe("%PDF-");
  const xlsx = await page.request.get(await page.getByRole("link", { name: "Excel" }).getAttribute("href").then((h) => h!));
  expect(xlsx.status()).toBe(200);
  expect(xlsx.headers()["content-disposition"]).toContain(".xlsx");

  // Рабочий стол администратора доступен владельцу
  await page.goto("/admin");
  await expect(page.getByRole("heading", { level: 1, name: "Рабочий стол" })).toBeVisible();
  await expect(page.getByRole("region", { name: "Заезды сегодня" })).toBeVisible();

  // Журнал действий: записи с именем сотрудника и подписью действия по-русски
  await page.goto("/owner/audit");
  await expect(page.getByRole("heading", { level: 1, name: "Журнал действий" })).toBeVisible();
  await expect(page.getByText("Не удалось загрузить журнал.")).toHaveCount(0);
  await expect(page.getByRole("table").getByRole("row").nth(1)).toBeVisible();
  await page.getByLabel("Что").selectOption({ label: "Выгрузки отчётов" });
  await page.getByRole("button", { name: "Показать" }).click();
  await expect(page.getByRole("cell", { name: "Выгружен отчёт" }).first()).toBeVisible();
});
