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

/** Ближайший целый час по времени гостиницы (Asia/Bishkek, UTC+6): бронь с него можно заселить сразу. */
function nextHotelHour(): { date: string; hour: number } {
  const local = new Date(Date.now() + 6 * 3600_000 + 3600_000);
  return { date: local.toISOString().slice(0, 10), hour: local.getUTCHours() };
}

test("стойка: заселение, выселение с долгом, номер уходит на уборку и задача появляется в «Уборке»", async ({ page }) => {
  const suffix = `${String(Date.now() % 1_000_000).padStart(6, "0")}${Math.floor(Math.random() * 90 + 10)}`;
  const typeName = `Стойка ${suffix}`;
  const { date, hour } = nextHotelHour();

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

  // Бронь с ближайшего часа на 2 часа — заселить можно сразу (за час до начала)
  await page.goto(`/admin/bookings/new?kind=hourly&date=${date}&startHour=${hour}&hours=2&adults=2&children=0`);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Найти номера" }).click();
  await page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true }).click();
  await page.getByLabel("Имя и фамилия гостя", { exact: true }).fill("Тимур Стойкин");
  await page.getByLabel("Телефон", { exact: true }).fill("+996 700 555 666");
  await page.getByRole("button", { name: "Создать бронь" }).click();
  await expect(page.getByRole("status").getByText("Бронь создана")).toBeVisible();

  // Заселение
  await expect(page.getByRole("heading", { level: 2, name: "Стойка" })).toBeVisible();
  await page.getByRole("button", { name: "Заселить", exact: true }).click();
  await expect(page.getByText("Гость заселён", { exact: true }).filter({ visible: true }).first()).toBeVisible();

  // Выселение: без оплаты кнопка неактивна, пока не отмечен долг
  const checkOut = page.getByRole("button", { name: "Выселить", exact: true });
  await expect(checkOut).toBeDisabled();
  await page.getByText(/Выселить с долгом/).click();
  await checkOut.click();
  await page.getByRole("alertdialog").getByRole("button", { name: "Да, выселить" }).click();
  await expect(page.getByText("Гость выехал", { exact: true }).filter({ visible: true }).first()).toBeVisible();

  // Номер на уборке, задача после выезда — в колонке «Ждут»
  await page.goto("/admin/housekeeping");
  const card = page.getByRole("article", { name: `Номер ${suffix}`, exact: true });
  await expect(card).toBeVisible();
  await expect(card.getByText("Ждёт уборщика")).toBeVisible();
  await expect(card.getByText(/после выезда/)).toBeVisible();
});
