import { expect, test, type APIRequestContext } from "@playwright/test";

const mailpitUrl = process.env.E2E_MAILPIT_URL ?? "http://localhost:8025";

/** Ссылка из последнего письма адресату (Mailpit API). Письма уходят фоном через outbox — ждём. */
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

test("гость регистрируется с согласием, подтверждает email и входит", async ({ page, request }) => {
  const email = `guest-${Date.now()}@hms.local`;

  await page.goto("/register");
  await page.getByLabel("ФИО", { exact: true }).fill("Гость Е2Е");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Пароль", { exact: true }).fill("guest-password-e2e");

  // Без согласия — ошибка
  await page.getByRole("button", { name: "Зарегистрироваться" }).click();
  await expect(page.getByText("Необходимо согласие на обработку персональных данных.")).toBeVisible();
  // Введённые данные не теряются после ошибки (кроме пароля — его вводят заново)
  await expect(page.getByLabel("ФИО", { exact: true })).toHaveValue("Гость Е2Е");
  await expect(page.getByLabel("Email", { exact: true })).toHaveValue(email);
  await page.getByLabel("Пароль", { exact: true }).fill("guest-password-e2e");

  // С согласием — регистрация проходит (регрессия: отмеченная галочка Base UI не засчитывалась)
  await page.getByRole("checkbox", { name: "Я согласен(на) на обработку персональных данных" }).click();
  await page.getByRole("button", { name: "Зарегистрироваться" }).click();
  await expect(page.getByRole("heading", { name: "Проверьте почту" })).toBeVisible();

  // Подтверждение по ссылке из письма
  await page.goto(await linkFromEmail(request, email, "/confirm-email"));
  await page.getByRole("button", { name: "Подтвердить email" }).click();
  await expect(page.getByText("Email подтверждён")).toBeVisible();

  // Вход гостем → личный кабинет
  await page.goto("/login");
  await page.getByLabel("Email или телефон", { exact: true }).fill(email);
  await page.getByLabel("Пароль", { exact: true }).fill("guest-password-e2e");
  await page.getByRole("button", { name: "Войти" }).click();
  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByRole("heading", { level: 1, name: "Мои брони" })).toBeVisible();
});
