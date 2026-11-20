import { expect, test, type Page } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

async function loginOwner(page: Page, next: string) {
  await page.goto(next);
  await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
  await page.getByLabel("Пароль", { exact: true }).fill(ownerPassword);
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page).toHaveURL(new RegExp(`${next.replace(/\//g, "\\/")}$`));
}

function isoDate(daysFromNow: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysFromNow);
  return d.toISOString().slice(0, 10);
}

test("владелец задаёт сезонный тариф, создаёт бронь через подбор номера и отменяет её", async ({ page }) => {
  const suffix = String(Date.now() % 100000);
  // Даты далеко в будущем и свои для каждого запуска — повторные прогоны не мешают друг другу.
  const offset = 200 + (Math.floor(Date.now() / 1000) % 500);
  const checkIn = isoDate(offset);
  const checkOut = isoDate(offset + 2);

  // Категория и номер
  await loginOwner(page, "/admin/room-types/new");
  const typeName = `Бронь ${suffix}`;
  await page.getByLabel("Название", { exact: true }).fill(typeName);
  await page.getByLabel("Вместимость, гостей", { exact: true }).fill("2");
  await page.getByLabel("Кроватей", { exact: true }).fill("1");
  await page.getByLabel("Базовая цена за ночь, сом", { exact: true }).fill("3000");
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: typeName })).toBeVisible();

  await page.goto("/admin/rooms/new");
  await page.getByLabel("Номер комнаты", { exact: true }).fill(suffix);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Номер комнаты ${suffix}` })).toBeVisible();

  // Сезонный тариф на первую ночь — 4000 вместо 3000
  await page.goto("/owner/pricing");
  const rate = page.locator("#rate-form");
  await rate.getByLabel("Название", { exact: true }).fill(`Сезон ${suffix}`);
  await rate.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await rate.getByLabel("С даты", { exact: true }).fill(checkIn);
  await rate.getByLabel("По дату", { exact: true }).fill(checkIn);
  await rate.getByLabel("Цена, сом", { exact: true }).fill("4000");
  await rate.getByRole("button", { name: "Добавить" }).click();
  await expect(page.getByRole("cell", { name: `Сезон ${suffix}`, exact: true })).toBeVisible();

  // Подбор номера: цена считается на backend — 4000 + 3000
  // Календарь: быстрая кнопка «Неделя» от выбранного заезда, затем нужные даты через адрес страницы
  // По умолчанию — почасовая бронь; посуточная — на вкладке «По суткам»
  await page.goto("/admin/bookings/new");
  await expect(page.getByRole("link", { name: "По часам", exact: true })).toHaveAttribute("aria-current", "page");
  await page.getByRole("link", { name: "По суткам", exact: true }).click();
  await page.getByRole("button", { name: /^Заезд — Выезд/ }).click();
  await page.getByRole("button", { name: "Неделя", exact: true }).click();
  await page.getByRole("button", { name: "Готово", exact: true }).click();
  await expect(page.getByRole("button", { name: /^Заезд — Выезд/ })).toContainText("7 ночей");
  await page.goto(`/admin/bookings/new?kind=nightly&checkIn=${checkIn}&checkOut=${checkOut}&adults=2&children=0`);
  await expect(page.getByRole("button", { name: /^Заезд — Выезд/ })).toContainText("2 ночи");
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Найти номера" }).click();
  await page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true }).click();
  await expect(page.getByText("Тариф «Сезон " + suffix + "»")).toBeVisible();

  // Без контакта — ошибка; с телефоном — бронь создана
  await page.getByLabel("Имя и фамилия гостя", { exact: true }).fill("Асель Тестова");
  await page.getByRole("button", { name: "Создать бронь" }).click();
  await expect(page.getByText("Укажите телефон или email гостя для связи.")).toBeVisible();
  await page.getByLabel("Телефон", { exact: true }).fill("+996 555 123 456");
  await page.getByRole("button", { name: "Создать бронь" }).click();
  await expect(page.getByRole("status").getByText("Бронь создана")).toBeVisible();
  await expect(page.getByText("Подтверждена").first()).toBeVisible();
  await expect(page.getByText("7 000,00 сом", { exact: true }).filter({ visible: true }).first()).toBeVisible();
  const number = (await page.getByRole("heading", { level: 1 }).textContent())!.replace("Бронь", "").trim();

  // Тот же номер на те же даты больше не предлагается
  await page.goto(`/admin/bookings/new?kind=nightly&checkIn=${checkIn}&checkOut=${checkOut}&adults=2&children=0`);
  await expect(page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true })).toHaveCount(0);

  // Отмена: причина обязательна, номер освобождается, история пишется
  await page.goto("/admin/bookings?search=" + encodeURIComponent(number));
  await page.getByRole("link", { name: number, exact: true }).click();
  await page.getByRole("button", { name: "Отменить бронь", exact: true }).click();
  await expect(page.getByText("Укажите причину отмены.").first()).toBeVisible();
  await page.getByLabel("Причина отмены", { exact: true }).fill("Гость передумал");
  // Окно подтверждения: «Нет» ничего не меняет, «Да» отменяет
  await page.getByRole("button", { name: "Отменить бронь", exact: true }).click();
  const dialog = page.getByRole("alertdialog");
  await expect(dialog).toContainText(`Отменить бронь ${number}?`);
  await dialog.getByRole("button", { name: "Нет, оставить" }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText("Подтверждена").filter({ visible: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Отменить бронь", exact: true }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: "Да, отменить бронь" }).click();
  await expect(page.getByText("Статус изменён: Подтверждена → Отменена")).toBeVisible();

  await page.goto(`/admin/bookings/new?kind=nightly&checkIn=${checkIn}&checkOut=${checkOut}&adults=2&children=0`);
  await expect(page.getByRole("link", { name: `Выбрать: Номер ${suffix}`, exact: true })).toBeVisible();
});
