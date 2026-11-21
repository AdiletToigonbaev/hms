import { expect, test, type Page } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

// Минимальный PNG 1×1 — для загрузки фото
const tinyPng = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "base64",
);

async function loginOwner(page: Page, next: string) {
  await page.goto(next);
  await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
  await page.getByLabel("Пароль", { exact: true }).fill(ownerPassword);
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page).toHaveURL(new RegExp(`${next.replace(/\//g, "\\/")}$`));
}

test("владелец создаёт категорию с удобствами и фото, затем номер, меняет статус и видит историю", async ({ page }) => {
  // Категория
  await loginOwner(page, "/admin/room-types/new");
  const typeName = `Семейный ${Date.now() % 100000}`;
  await page.getByLabel("Название", { exact: true }).fill(typeName);
  await page.getByLabel("Вместимость, гостей", { exact: true }).fill("4");
  await page.getByLabel("Кроватей", { exact: true }).fill("2");
  await page.getByLabel("Базовая цена за ночь, сом", { exact: true }).fill("5 200,50");
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: typeName })).toBeVisible();

  // Удобства
  await page.getByRole("checkbox", { name: "Wi-Fi", exact: true }).click();
  await page.getByRole("checkbox", { name: "Вид на горы", exact: true }).click();
  await page.getByRole("button", { name: "Сохранить удобства" }).click();
  await expect(page.getByText("Сохранено.").first()).toBeVisible();

  // Фото: файл с неверным содержимым отклоняется, PNG загружается
  const fileInput = page.locator('input[type="file"]');
  await fileInput.setInputFiles({ name: "fake.png", mimeType: "image/png", buffer: Buffer.from("not an image") });
  await page.getByRole("button", { name: "Загрузить" }).click();
  await expect(page.getByText("Поддерживаются только изображения JPEG, PNG и WebP.")).toBeVisible();
  await fileInput.setInputFiles({ name: "room.png", mimeType: "image/png", buffer: tinyPng });
  await page.getByRole("button", { name: "Загрузить" }).click();
  await expect(page.getByRole("button", { name: "Удалить фото" })).toHaveCount(1);
  const photoSrc = await page.locator('img[src^="/media/"]').first().getAttribute("src");
  const media = await page.request.get(photoSrc!);
  expect(media.status()).toBe(200);
  expect(media.headers()["content-type"]).toBe("image/png");

  // Номер этой категории
  await page.goto("/admin/rooms/new");
  const number = String(Date.now() % 100000);
  await page.getByLabel("Номер комнаты", { exact: true }).fill(number);
  await page.getByLabel("Этаж", { exact: true }).fill("3");
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Номер комнаты ${number}` })).toBeVisible();
  await expect(page.getByText("Свободен").first()).toBeVisible();

  // Смена статуса: без причины — ошибка, с причиной — история
  await page.getByLabel("Новый статус", { exact: true }).selectOption("Maintenance");
  await page.getByRole("button", { name: "Сменить статус" }).click();
  await expect(page.getByText("Укажите причину смены статуса.")).toBeVisible();
  await page.getByLabel("Причина", { exact: true }).fill("Замена смесителя");
  await page.getByRole("button", { name: "Сменить статус" }).click();
  await expect(page.getByText("Свободен → Ремонт")).toBeVisible();
  await expect(page.getByText("Замена смесителя")).toBeVisible();

  // Номер виден на схеме этажа с новым статусом
  await page.goto("/admin/rooms");
  const card = page.getByRole("link", { name: new RegExp(`^${number}`) });
  await expect(card).toContainText("Ремонт");
  await expect(card).toContainText("5 200,50");
});

test("владелец сохраняет настройки гостиницы; интеграции банков недоступны до получения доступов", async ({ page }) => {
  await loginOwner(page, "/owner/settings");
  // Меню разделов: клик подсвечивает пункт и на мгновение выделяет раздел
  const nav = page.getByRole("navigation", { name: "Разделы настроек" });
  await nav.getByRole("link", { name: "Оплата", exact: true }).click();
  await expect(nav.getByRole("link", { name: "Оплата", exact: true })).toHaveAttribute("aria-current", "location");
  await expect(page.locator("section#section-payment")).toHaveAttribute("data-flash", "");

  // Зеркально: правка поля в разделе подсвечивает его пункт в меню
  await page.locator("section#section-rooms").getByLabel("Уборка после почасовой брони, мин", { exact: true }).focus();
  await expect(nav.getByRole("link", { name: "Номера и уборка", exact: true })).toHaveAttribute("aria-current", "location");

  // Каждый раздел сохраняется своей кнопкой
  const payment = page.locator("section#section-payment");
  await payment.getByLabel("Предоплата, %", { exact: true }).fill("130");
  await payment.getByRole("button", { name: "Сохранить" }).click();
  await expect(payment.getByText("Предоплата — от 0 до 100%.")).toBeVisible();
  await payment.getByLabel("Предоплата, %", { exact: true }).fill("0");
  await payment.getByRole("button", { name: "Сохранить" }).click();
  await expect(payment.getByText("Раздел сохранён.")).toBeVisible();

  // Правила броней: описание обычными словами меняется вместе с цифрами, сохранение не трогает другие разделы
  const rules = page.locator("section#section-rules");
  // По умолчанию штрафов нет — отмена всегда бесплатна; срок бесплатной отмены появляется, когда задан штраф
  await expect(rules.getByText("Отмена всегда бесплатна", { exact: false }).first()).toBeVisible();
  await rules.getByLabel("Штраф за позднюю отмену — По часам", { exact: true }).fill("1");
  await rules.getByLabel("Бесплатная отмена — По часам", { exact: true }).fill("90");
  await expect(rules.getByText("Бесплатная отмена — не позже чем за 90 минут до начала", { exact: false })).toBeVisible();
  await rules.getByLabel("Штраф за позднюю отмену — По часам", { exact: true }).fill("0");
  await rules.getByLabel("Бесплатная отмена — По часам", { exact: true }).fill("120");
  await rules.getByRole("button", { name: "Сохранить" }).click();
  await expect(rules.getByText("Раздел сохранён.")).toBeVisible();
  await expect(payment.getByLabel("Предоплата, %", { exact: true })).toHaveValue("0");

  await page.goto("/owner/integrations");
  await expect(page.getByRole("heading", { name: "MBANK — интернет-эквайринг" })).toBeVisible();
  await expect(page.getByText("Ждём доступы").first()).toBeVisible();
});

test("владелец видит раздел номеров и свои разделы в меню", async ({ page }) => {
  await loginOwner(page, "/admin/rooms");
  await expect(page.getByRole("heading", { level: 1, name: "Номера" })).toBeVisible();
  // Меню строится по роли: у владельца в разделе администратора есть и его собственные пункты
  await expect(page.getByRole("link", { name: "Интеграции" }).first()).toBeVisible();
});
