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

test("уборка: задача → уборщик на /hk берёт, проходит чек-лист и сдаёт → владелец принимает", async ({ page, browser }) => {
  const suffix = `${String(Date.now() % 1_000_000).padStart(6, "0")}${Math.floor(Math.random() * 90 + 10)}`;
  const typeName = `Уборка ${suffix}`;
  const maidEmail = `maid-${suffix}@hms.local`;

  // Номер и уборщик
  await loginOwner(page, "/admin/room-types/new");
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

  await page.goto("/owner/staff/new");
  await page.getByLabel("ФИО", { exact: true }).fill(`Уборщица ${suffix}`);
  await page.getByLabel("Email", { exact: true }).fill(maidEmail);
  await page.getByLabel("Роль", { exact: true }).selectOption({ label: "Уборщик" });
  await page.getByLabel("Временный пароль", { exact: true }).fill("temp-password-e2e");
  await page.getByRole("button", { name: "Создать" }).click();
  await expect(page.getByRole("heading", { name: `Уборщица ${suffix}` })).toBeVisible();

  // Администратор ставит задачу без исполнителя — её видят все уборщики как «Можно взять»
  await page.goto("/admin/housekeeping");
  await page.getByLabel("Номер", { exact: true }).selectOption({ label: suffix });
  await page.getByLabel("Заметка", { exact: true }).fill("Проверить мини-бар");
  await page.getByRole("button", { name: "Создать", exact: true }).click();
  await expect(page.getByText("Задача создана.").filter({ visible: true }).first()).toBeVisible();

  // Уборщик: первый вход → смена пароля → свой мобильный экран
  const maidContext = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const maid = await maidContext.newPage();
  await maid.goto("/login");
  await maid.getByLabel("Email или телефон", { exact: true }).fill(maidEmail);
  await maid.getByLabel("Пароль", { exact: true }).fill("temp-password-e2e");
  await maid.getByRole("button", { name: "Войти" }).click();
  await expect(maid).toHaveURL(/\/change-password$/);
  await maid.getByLabel("Текущий пароль").fill("temp-password-e2e");
  await maid.getByLabel("Новый пароль").fill("maid-own-password-1");
  await maid.getByRole("button", { name: "Сменить пароль" }).click();
  await expect(maid).toHaveURL(/\/hk$/);

  const task = maid.getByRole("article", { name: `Номер ${suffix}`, exact: true });
  await expect(task).toBeVisible();
  await expect(task.getByText("Проверить мини-бар")).toBeVisible();
  // Уборщик не видит финансы и ПД гостя
  await expect(maid.getByText(/сом/)).toHaveCount(0);

  await task.getByRole("button", { name: "Взять себе" }).click();
  await task.getByRole("button", { name: "Начать уборку" }).click();

  // Чек-лист: без всех пунктов сдать нельзя — отмечаем каждый
  const items = task.getByRole("region", { name: "Чек-лист" }).getByRole("button", { pressed: false });
  await expect(items.first()).toBeVisible();
  while ((await items.count()) > 0) {
    const before = await items.count();
    await items.first().click();
    await expect(items).toHaveCount(before - 1);
  }
  await task.getByRole("button", { name: "Уборка закончена" }).click();
  await expect(task.getByText("Ждёт проверки администратором.")).toBeVisible();
  await maidContext.close();

  // Владелец принимает уборку — задача уходит в «Выполненные»
  await page.goto("/admin/housekeeping");
  const card = page.getByRole("article", { name: `Номер ${suffix}`, exact: true });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: "Принять", exact: true }).click();
  await expect(card).toBeHidden();
  await page.goto("/admin/housekeeping?tab=done");
  await expect(page.getByText(`Номер ${suffix}`, { exact: true }).filter({ visible: true }).first()).toBeVisible();
});
