import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

const ownerEmail = process.env.E2E_OWNER_EMAIL ?? "owner@hms.local";
const ownerPassword = process.env.E2E_OWNER_PASSWORD ?? "owner-new-password-1";
const mailpitUrl = process.env.E2E_MAILPIT_URL ?? "http://localhost:8025";

async function login(page: Page, email: string, password: string) {
  await page.getByLabel("Email или телефон", { exact: true }).fill(email);
  await page.getByLabel("Пароль", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Войти" }).click();
}

async function linkFromEmail(request: APIRequestContext, to: string, path: string): Promise<string> {
  for (let attempt = 0; attempt < 30; attempt++) {
    const search = await request.get(`${mailpitUrl}/api/v1/search?query=${encodeURIComponent(`to:${to}`)}`);
    const { messages } = (await search.json()) as { messages: { ID: string }[] };
    if (messages.length > 0) {
      const message = await (await request.get(`${mailpitUrl}/api/v1/message/${messages[0].ID}`)).json();
      const match = new RegExp(`https?://[^\\s"]+${path}\\?[^\\s"<]+`).exec(message.Text as string);
      if (match) return match[0];
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`Письмо для ${to} не пришло`);
}

function isoDate(daysFromNow: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysFromNow);
  return d.toISOString().slice(0, 10);
}

test("сайт: категория в каталоге, бронь, регистрация с возвратом к брони через письмо, бронь гостя, отмена в кабинете", async ({ page, browser, request }) => {
  // Самый длинный сценарий (каталог → регистрация → письмо → вход → бронь → отмена): при параллельных воркерах не укладывается в 30 с.
  test.slow();
  const suffix = `${String(Date.now() % 1_000_000).padStart(6, "0")}${Math.floor(Math.random() * 90 + 10)}`;
  const typeName = `Сайт ${suffix}`;
  const date = isoDate(160 + (Math.floor(Date.now() / 1000) % 300));

  // Владелец: категория с ценой часа и номер в продаже
  await page.goto("/admin/room-types/new");
  await login(page, ownerEmail, ownerPassword);
  await page.getByLabel("Название", { exact: true }).fill(typeName);
  await page.getByLabel("Вместимость, гостей", { exact: true }).fill("2");
  await page.getByLabel("Кроватей", { exact: true }).fill("1");
  await page.getByLabel("Базовая цена за ночь, сом", { exact: true }).fill("3000");
  await page.getByLabel("Цена за час, сом", { exact: true }).fill("700");
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: typeName })).toBeVisible();
  const typeId = /room-types\/([0-9a-f-]{36})/.exec(page.url())![1];
  await page.goto("/admin/rooms/new");
  await page.getByLabel("Номер комнаты", { exact: true }).fill(suffix);
  await page.getByLabel("Категория", { exact: true }).selectOption({ label: typeName });
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Номер комнаты ${suffix}` })).toBeVisible();

  // Гость без аккаунта (отдельный браузер): каталог → выбор номера → бронь → регистрация → письмо → вход → снова та же бронь
  const guestContext = await browser.newContext();
  const guest = await guestContext.newPage();
  const email = `portal-${Date.now()}@hms.local`;

  await guest.goto(`/rooms/${typeId}`);
  await expect(guest.getByRole("heading", { level: 1, name: typeName })).toBeVisible();
  await expect(guest.getByText("700,00 сом").first()).toBeVisible();

  await guest.goto(`/book?kind=hourly&date=${date}&startHour=11&hours=2&adults=2&children=0&roomTypeId=${typeId}`);
  await expect(guest.getByRole("heading", { level: 1, name: "Свободные номера" })).toBeVisible();
  await guest.getByRole("article", { name: new RegExp(`^Номер ${suffix}`) }).getByRole("link", { name: "Выбрать", exact: true }).click();
  await expect(guest.getByRole("heading", { level: 1, name: "Подтверждение брони" })).toBeVisible();
  await expect(guest.getByRole("heading", { level: 2, name: new RegExp(`^Номер ${suffix}`) })).toBeVisible();
  await expect(guest.getByText("1 400,00 сом").first()).toBeVisible();
  await guest.getByRole("link", { name: "Регистрация", exact: true }).click();
  await expect(guest).toHaveURL(/\/register\?next=/);
  await guest.getByLabel("ФИО", { exact: true }).fill("Асель Сайтова");
  await guest.getByLabel("Email", { exact: true }).fill(email);
  await guest.getByLabel("Пароль", { exact: true }).fill("guest-password-e2e");
  await guest.getByRole("checkbox", { name: "Я согласен(на) на обработку персональных данных" }).click();
  await guest.getByRole("button", { name: "Зарегистрироваться" }).click();
  await expect(guest.getByRole("heading", { name: "Проверьте почту" })).toBeVisible();

  // Ссылка из письма несёт адрес брони; после подтверждения «Войти» ведёт обратно к ней
  const link = await linkFromEmail(request, email, "/confirm-email");
  expect(link).toContain("next=");
  await guest.goto(link);
  await guest.getByRole("button", { name: "Подтвердить email" }).click();
  await expect(guest.getByText(/вернётесь к выбранной брони/)).toBeVisible();
  await guest.getByRole("link", { name: "Войти" }).click();
  await login(guest, email, "guest-password-e2e");
  await expect(guest).toHaveURL(/\/book\?/);
  await expect(guest.getByRole("heading", { level: 1, name: "Подтверждение брони" })).toBeVisible();
  await guest.getByLabel("Пожелания").fill("Поздний заезд");
  await guest.getByRole("button", { name: "Забронировать", exact: true }).click();

  // Кабинет: бронь ждёт подтверждения администратором; номер комнаты — тот, что выбрал гость
  await expect(guest).toHaveURL(/\/account\/bookings\//);
  await expect(guest.getByRole("heading", { level: 1, name: typeName })).toBeVisible();
  await expect(guest.getByText(`номер ${suffix}`).first()).toBeVisible();
  await expect(guest.getByText("Ожидает подтверждения").first()).toBeVisible();
  await expect(guest.getByText(/Бронь создана/)).toBeVisible();

  // Отмена по правилам (штрафов по умолчанию нет) — с подтверждением
  await guest.getByRole("button", { name: "Отменить бронь" }).click();
  await guest.getByRole("alertdialog").getByRole("button", { name: "Да, отменить" }).click();
  await expect(guest.getByText("Отменена").first()).toBeVisible();
  await guest.goto("/account?scope=history");
  await expect(guest.getByText(typeName).first()).toBeVisible();
  await guestContext.close();
});
