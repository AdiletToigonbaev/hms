import { expect, test, type Page } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

async function loginOwner(page: Page, next: string) {
  await page.goto(next);
  await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
  await page.getByLabel("Пароль", { exact: true }).fill(ownerPassword);
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page).toHaveURL(new RegExp(`${next.replace(/\//g, "\\/").replace(/\?/g, "\\?")}$`));
}

function isoDate(daysFromNow: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysFromNow);
  return d.toISOString().slice(0, 10);
}

test("оплата брони: частичная оплата, остаток, возврат с одобрением и выплатой; расход", async ({ page }) => {
  // 8 цифр: время + случайные — номер комнаты не совпадёт с номерами прошлых прогонов в той же базе.
  const suffix = `${String(Date.now() % 1_000_000).padStart(6, "0")}${Math.floor(Math.random() * 90 + 10)}`;
  const typeName = `Фин ${suffix}`;
  const date = isoDate(150 + (Math.floor(Date.now() / 1000) % 300));

  await loginOwner(page, "/admin/room-types/new");
  await page.getByLabel("Название", { exact: true }).fill(typeName);
  await page.getByLabel("Вместимость, гостей", { exact: true }).fill("2");
  await page.getByLabel("Кроватей", { exact: true }).fill("1");
  await page.getByLabel("Базовая цена за ночь, сом", { exact: true }).fill("3000");
  await page.getByLabel("Цена за час, сом", { exact: true }).fill("500");
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: typeName })).toBeVisible();
  await page.goto("/admin/rooms/new");
  await page.getByLabel("Номер комнаты", { exact: true }).fill(suffix);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Номер комнаты ${suffix}` })).toBeVisible();

  // Бронь на 2 часа = 1 000 сом
  await page.goto(`/admin/bookings/new?kind=hourly&date=${date}&startHour=11&hours=2&adults=2&children=0`);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Найти номера" }).click();
  await page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true }).click();
  await page.getByLabel("Имя и фамилия гостя", { exact: true }).fill("Динара Платёжная");
  await page.getByLabel("Телефон", { exact: true }).fill("+996 700 333 444");
  await page.getByRole("button", { name: "Создать бронь" }).click();
  await expect(page.getByRole("status").getByText("Бронь создана")).toBeVisible();

  // Предоплата 400 MBANK → осталось 600
  const amount = page.getByLabel("Сумма, сом", { exact: true }).filter({ visible: true });
  await amount.fill("400");
  await page.getByLabel("Способ оплаты", { exact: true }).filter({ visible: true }).selectOption({ label: "MBANK (перевод)" });
  await page.getByRole("button", { name: "Принять оплату", exact: true }).click();
  await expect(page.getByText("Оплата принята.").filter({ visible: true })).toBeVisible();
  await expect(page.getByText("600,00 сом", { exact: true }).filter({ visible: true }).first()).toBeVisible();

  // Сверх остатка API не примет
  await amount.fill("700");
  await page.getByRole("button", { name: "Принять оплату", exact: true }).click();
  await expect(page.getByText(/больше остатка/).filter({ visible: true }).first()).toBeVisible();

  // Возврат по платежу: запрос → одобрение → выдача
  await page.getByRole("link", { name: /Предоплата · MBANK/ }).click();
  await expect(page.getByRole("heading", { level: 2, name: "Возврат денег" })).toBeVisible();
  await page.getByLabel("Сумма возврата, сом", { exact: true }).fill("400");
  const reason = `Гость передумал ${suffix}`;
  await page.getByLabel("Причина", { exact: true }).fill(reason);
  await page.getByRole("button", { name: "Запросить возврат" }).click();
  // Вся сумма в возврате — форма исчезает, возврат появляется в списке платежа
  await expect(page.getByText("Ждёт решения").filter({ visible: true }).first()).toBeVisible();

  await page.goto("/admin/refunds?status=Requested");
  const row = page.getByRole("row").filter({ hasText: reason }).first();
  await row.getByRole("button", { name: "Одобрить" }).click();
  await expect(row).toBeHidden(); // одобрен — ушёл из «Ждёт решения»
  await page.goto("/admin/refunds?status=Approved");
  await page.getByRole("row").filter({ hasText: reason }).first().getByRole("button", { name: "Деньги выданы" }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: "Да, выданы" }).click();
  await expect(page.getByRole("row").filter({ hasText: reason })).toBeHidden();
  await page.goto("/admin/refunds?status=Paid");
  await expect(page.getByRole("row").filter({ hasText: reason }).first()).toBeVisible();

  // Расход
  await page.goto("/admin/expenses");
  const description = `Стирка белья ${suffix}`;
  await page.getByLabel("Категория", { exact: true }).filter({ visible: true }).last().selectOption({ label: "Бельё и прачечная" });
  await page.getByLabel("Сумма, сом", { exact: true }).fill("1800");
  await page.getByLabel("Что оплатили", { exact: true }).fill(description);
  await page.getByRole("button", { name: "Внести расход" }).click();
  await expect(page.getByText("Расход внесён.").filter({ visible: true })).toBeVisible();
  await expect(page.getByRole("cell", { name: description })).toBeVisible();
});
