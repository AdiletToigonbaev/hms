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

/** Категория с ценой часа и номер в ней; возвращает номер комнаты. */
async function createHourlyRoom(page: Page, suffix: string, typeName: string) {
  await page.goto("/admin/room-types/new");
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
}

test("календарь: бронь на шкале часов, перенос перетаскиванием с подтверждением цены, отмена без штрафов по умолчанию", async ({ page }) => {
  // 8 цифр: время + случайные — номер комнаты не совпадёт с номерами прошлых прогонов в той же базе.
  const suffix = `${String(Date.now() % 1_000_000).padStart(6, "0")}${Math.floor(Math.random() * 90 + 10)}`;
  const typeName = `Кал ${suffix}`;
  const date = isoDate(200 + (Math.floor(Date.now() / 1000) % 300));

  await loginOwner(page, "/admin/calendar");
  await expect(page.getByRole("heading", { level: 1, name: "Календарь" })).toBeVisible();
  await createHourlyRoom(page, suffix, typeName);

  // Бронь 10:00–12:00
  await page.goto(`/admin/bookings/new?kind=hourly&date=${date}&startHour=10&hours=2&adults=2&children=0`);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Найти номера" }).click();
  await page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true }).click();
  await page.getByLabel("Имя и фамилия гостя", { exact: true }).fill("Айгуль Календарная");
  await page.getByLabel("Телефон", { exact: true }).fill("+996 700 222 333");
  await page.getByRole("button", { name: "Создать бронь" }).click();
  await expect(page.getByRole("status").getByText("Бронь создана")).toBeVisible();
  // «Бронь Ч-2111-07» → «Ч-2111-07»
  const number = (await page.getByRole("heading", { level: 1 }).filter({ visible: true }).first().innerText()).replace(/^\S+\s+/, "").trim();

  // Календарь на этот день: блок брони в строке номера, по клику — подробности
  await page.goto(`/admin/calendar?date=${date}`);
  const block = page.getByRole("button", { name: new RegExp(`^Бронь ${number}, Айгуль Календарная`) });
  await expect(block).toBeVisible();
  await block.click();
  await expect(page.getByRole("heading", { name: `Бронь ${number}` })).toBeVisible();

  // Перетаскиваем на 2 часа вправо (ширина часа зависит от экрана — меряем по шапке) → окно с новым временем и ценой
  const hourPx = (await page.locator('[data-hour="10"]').boundingBox())!.width;
  const box = (await block.boundingBox())!;
  await page.mouse.move(box.x + 20, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + 60, box.y + box.height / 2, { steps: 5 });
  await page.mouse.move(box.x + 20 + 2 * hourPx, box.y + box.height / 2, { steps: 10 });
  await page.mouse.up();
  const dialog = page.getByRole("alertdialog");
  await expect(dialog.getByText(`Перенести бронь ${number}?`)).toBeVisible();
  await expect(dialog.getByText(/12:00–14:00/)).toBeVisible();
  await dialog.getByRole("button", { name: "Перенести" }).click();
  await expect(page.getByText(/перенесена/).filter({ visible: true }).first()).toBeVisible();

  // В брони — новое время и запись в истории; отмена далеко заранее — бесплатна
  await page.goto(`/admin/calendar?date=${date}`);
  await page.getByRole("button", { name: new RegExp(`^Бронь ${number}`) }).click();
  await page.getByRole("link", { name: "Открыть бронь" }).click();
  await expect(page.getByText(/12:00–14:00/).filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText("Бронь перенесена", { exact: true }).filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText("Штрафы за отмену не настроены — отмена бесплатна.").filter({ visible: true }).first()).toBeVisible();
});
