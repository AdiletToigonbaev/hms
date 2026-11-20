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

test("почасовая бронь: цена часа у категории, бронь на 3 часа, продление на 1 час с подтверждением", async ({ page }) => {
  const suffix = String(Date.now() % 100000);
  const date = isoDate(300 + (Math.floor(Date.now() / 1000) % 400));
  const [y, m, d] = date.split("-");

  // Категория с ценой часа и номер
  await loginOwner(page, "/admin/room-types/new");
  const typeName = `Часы ${suffix}`;
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

  // Вкладка «По часам»: дата, начало, длительность
  await page.goto("/admin/bookings/new");
  await expect(page.getByLabel("Начало", { exact: true })).toBeVisible();
  await page.goto(`/admin/bookings/new?kind=hourly&date=${date}&startHour=10&hours=3&adults=2&children=0`);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Найти номера" }).click();
  await page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true }).click();
  await expect(page.getByText("1 500,00 сом", { exact: true }).filter({ visible: true }).first()).toBeVisible();

  await page.getByLabel("Имя и фамилия гостя", { exact: true }).fill("Бакыт Часовой");
  await page.getByLabel("Телефон", { exact: true }).fill("+996 700 123 456");
  await page.getByRole("button", { name: "Создать бронь" }).click();
  await expect(page.getByRole("status").getByText("Бронь создана")).toBeVisible();
  await expect(page.getByText(`${d}.${m}.${y} 10:00–13:00`).filter({ visible: true }).first()).toBeVisible();

  // Продление на 1 час: окно с новым временем и доплатой, затем подтверждение
  await page.getByLabel("На сколько часов", { exact: true }).selectOption("1");
  await page.getByRole("button", { name: "Продлить", exact: true }).click();
  const dialog = page.getByRole("alertdialog");
  await expect(dialog).toContainText(`${d}.${m}.${y} 14:00`);
  await expect(dialog).toContainText("500,00 сом");
  await dialog.getByRole("button", { name: "Да, продлить" }).click();
  await expect(page.getByText(`${d}.${m}.${y} 10:00–14:00`, { exact: true }).filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText("2 000,00 сом", { exact: true }).filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText("Бронь продлена").filter({ visible: true }).first()).toBeVisible();

  // В списке — время, а не только даты
  await page.goto(`/admin/bookings?search=${encodeURIComponent("Бакыт")}`);
  await expect(page.getByText("10:00–14:00").first()).toBeVisible();
});
