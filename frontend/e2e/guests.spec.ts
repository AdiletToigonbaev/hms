import { expect, test, type Page } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";

async function loginOwner(page: Page, next: string) {
  await page.goto(next);
  await page.getByLabel("Email или телефон", { exact: true }).fill(ownerEmail);
  await page.getByLabel("Пароль", { exact: true }).fill(ownerPassword);
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page).toHaveURL(new RegExp(`${next.replace(/\//g, "\/")}$`));
}

test("гость: карточка, дубль по телефону, согласие на WhatsApp, обезличивание с подтверждением", async ({ page }) => {
  const suffix = String(Date.now() % 10_000_000).padStart(7, "0");
  const phone = `+99670${suffix}`;

  await loginOwner(page, "/admin/guests/new");
  await page.getByLabel("Имя и фамилия", { exact: true }).fill(`Нурлан ${suffix}`);
  await page.getByLabel("Телефон", { exact: true }).fill(phone);
  await page.getByLabel("Заметки администратора", { exact: true }).fill("Просит номер с видом во двор");
  await page.getByRole("button", { name: "Создать", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Нурлан ${suffix}` })).toBeVisible();
  const cardUrl = page.url();

  // Дубль по телефону — предложение открыть существующую карточку
  await page.goto("/admin/guests/new");
  await page.getByLabel("Имя и фамилия", { exact: true }).fill("Кто-то другой");
  await page.getByLabel("Телефон", { exact: true }).fill(phone);
  await page.getByRole("button", { name: "Создать", exact: true }).click();
  await expect(page.getByText("Такой гость уже есть в базе.")).toBeVisible();
  await page.getByRole("link", { name: "Открыть карточку" }).click();
  await expect(page).toHaveURL(cardUrl);

  // Согласие на WhatsApp: источник и история
  const whatsApp = page.locator("form", { hasText: "Сообщения в WhatsApp" });
  await whatsApp.getByLabel("Откуда", { exact: true }).selectOption("Phone");
  await whatsApp.getByRole("button", { name: "Отметить согласие" }).click();
  await expect(whatsApp.getByText("Дано", { exact: true })).toBeVisible();
  await expect(page.getByText("По телефону").filter({ visible: true }).first()).toBeVisible();

  // Поиск в списке по части номера
  await page.goto(`/admin/guests?search=${suffix.slice(-5)}`);
  await expect(page.getByRole("link", { name: `Нурлан ${suffix}`, exact: true })).toBeVisible();

  // Обезличивание: окно подтверждения, «Отмена» ничего не меняет
  await page.goto(cardUrl);
  await page.getByRole("button", { name: "Обезличить гостя" }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: "Отмена" }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Нурлан ${suffix}` })).toBeVisible();
  await page.getByRole("button", { name: "Обезличить гостя" }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: "Да, удалить данные" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Гость удалён" })).toBeVisible();
  await expect(page.getByText(phone)).toHaveCount(0);
});
