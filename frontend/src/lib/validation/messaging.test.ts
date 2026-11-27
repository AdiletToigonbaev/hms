import { describe, expect, it } from "vitest";
import { messagingSettingsSchema, readAutoReplies, sendMessageSchema, templateSchema } from "./messaging";

describe("формы сообщений", () => {
  it("сообщение: нужен получатель и текст", () => {
    expect(sendMessageSchema.safeParse({ guestId: "", conversationId: "", channel: "Email", text: "Привет" }).success).toBe(false);
    const ok = sendMessageSchema.safeParse({ guestId: "01a1f99b-5396-70da-a2cf-1d85306e5294", conversationId: "", bookingId: "", channel: "WhatsApp", text: " Привет ", subject: "" });
    expect(ok.success && ok.data.text).toBe("Привет");
    expect(ok.success && ok.data.subject).toBeNull();
  });

  it("шаблон: имя в Meta — только латиница, цифры и «_»", () => {
    const base = { emailSubject: "Тема", emailBody: "Текст", whatsAppBody: "Текст", whatsAppStatus: "Approved", isActive: "on" };
    expect(templateSchema.safeParse({ ...base, whatsAppName: "booking_confirmed" }).success).toBe(true);
    expect(templateSchema.safeParse({ ...base, whatsAppName: "Бронь" }).success).toBe(false);
  });

  it("настройки: время напоминания накануне — ЧЧ:ММ, минуты в пределах", () => {
    const base = { hourlyStartReminderMinutes: "60", hourlyEndReminderMinutes: "15", nightlyEndReminderMinutes: "60", greetingText: "a", fallbackText: "b", stopText: "c" };
    expect(messagingSettingsSchema.safeParse({ ...base, nightlyStartReminderTime: "18:00" }).success).toBe(true);
    expect(messagingSettingsSchema.safeParse({ ...base, nightlyStartReminderTime: "25:00" }).success).toBe(false);
    expect(messagingSettingsSchema.safeParse({ ...base, nightlyStartReminderTime: "18:00", hourlyStartReminderMinutes: "5" }).success).toBe(false);
  });

  it("меню автоответчика: ключевые слова через запятую, удалённые пункты пропускаются", () => {
    const form = new FormData();
    form.set("count", "2");
    form.set("items.0.number", "1");
    form.set("items.0.title", "Цены");
    form.set("items.0.keywords", "цен, стоит ,");
    form.set("items.0.body", "Цены: {{price_list}}");
    form.set("items.0.action", "Text");
    form.set("items.0.isActive", "on");
    form.set("items.1.removed", "true");
    const parsed = readAutoReplies(form);
    expect(parsed.success).toBe(true);
    expect(parsed.data).toHaveLength(1);
    expect(parsed.data?.[0].keywords).toEqual(["цен", "стоит"]);
  });
});
