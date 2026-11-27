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

test("сообщения: шаблон с предпросмотром, проверка автоответа, письмо гостю из карточки и история", async ({ page }) => {
  const suffix = String(Date.now() % 10_000_000).padStart(7, "0");
  const email = `e2e-msg-${suffix}@example.com`;

  // Владелец: шаблон «Оплата получена» — новая тема, предпросмотр на примере.
  await loginOwner(page, "/owner/messages");
  await expect(page.getByRole("heading", { level: 1, name: "Шаблоны и автоответы" })).toBeVisible();
  await page.getByRole("link", { name: /Оплата получена/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Оплата получена" })).toBeVisible();
  const subject = page.getByLabel("Тема письма", { exact: true });
  await subject.fill(`Оплата по брони {{booking_number}} получена — спасибо ${suffix}`);
  await page.getByRole("button", { name: "Сохранить", exact: true }).click();
  await expect(page.getByText("Шаблон сохранён.")).toBeVisible();
  await page.reload();
  await expect(page.getByText(`Оплата по брони Ч-2811-01 получена — спасибо ${suffix}`)).toBeVisible();

  // Неизвестная переменная — ошибка от API с названием переменной.
  await page.getByLabel("Тема письма", { exact: true }).fill("Паспорт {{passport}}");
  await page.getByRole("button", { name: "Сохранить", exact: true }).click();
  await expect(page.getByText(/неизвестные переменные \{\{passport\}\}/)).toBeVisible();

  // Автоответчик: «сколько стоит» → пункт «Цены».
  await page.goto("/owner/messages/auto-replies");
  await page.getByLabel("Сообщение гостя", { exact: true }).fill("Сколько стоит номер?");
  await page.getByRole("button", { name: "Проверить", exact: true }).click();
  const answer = page.getByRole("status").filter({ hasText: "Ответ по пункту меню" });
  await expect(answer).toBeVisible();
  await expect(answer).toContainText("Цены:");

  // Гость с email: письмо из карточки → в истории сообщений.
  await page.goto("/admin/guests/new");
  await page.getByLabel("Имя и фамилия", { exact: true }).fill(`Айгерим ${suffix}`);
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByRole("button", { name: "Создать", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Айгерим ${suffix}` })).toBeVisible();

  await page.getByLabel("Канал", { exact: true }).selectOption("Email");
  await page.getByLabel("Тема письма", { exact: true }).fill("Ваш номер готов");
  await page.getByLabel("Текст", { exact: true }).fill(`Номер готов к заселению ${suffix}.`);
  await page.getByRole("button", { name: "Отправить", exact: true }).click();
  await expect(page.getByText("Сообщение поставлено в отправку.")).toBeVisible();

  // Фоновая отправка (outbox) — статус меняется на «Отправлено».
  await expect(async () => {
    await page.reload();
    await expect(page.getByRole("listitem").filter({ hasText: `Номер готов к заселению ${suffix}.` }).getByText("Отправлено", { exact: true })).toBeVisible();
  }).toPass({ timeout: 30_000 });

  await page.goto("/admin/messages?view=history&channel=Email");
  await expect(page.getByRole("link", { name: `Айгерим ${suffix}`, exact: true }).first()).toBeVisible();

  // Диалоги WhatsApp: без подключения — подсказка про интеграции.
  await page.goto("/admin/messages");
  await expect(page.getByRole("link", { name: "Диалоги WhatsApp", exact: true })).toBeVisible();
});
