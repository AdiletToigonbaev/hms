"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { toFormState, type FormState } from "@/lib/api/problem";
import { api } from "@/lib/api/server";
import type { MyBooking } from "@/lib/api/types";
import { stayApiBody } from "@/lib/site/stay";

// Бронь гостя с сайта. Цену и свободный номер определяет API; браузер передаёт только выбор гостя.

const idempotencyKey = z.string().regex(/^[A-Za-z0-9_-]{8,100}$/);
const isoDate = z.string().regex(/^\d{4}-\d{2}-\d{2}$/);
const staySchema = z.object({
  roomTypeId: z.string().uuid(),
  hourly: z.boolean(),
  date: isoDate,
  startHour: z.number().int().min(0).max(23),
  hours: z.number().int().min(1).max(24),
  checkIn: isoDate,
  checkOut: isoDate,
  adults: z.number().int().min(1).max(10),
  children: z.number().int().min(0).max(10),
});

export async function createWebBookingAction(stayJson: string, _: FormState, form: FormData): Promise<FormState> {
  const stay = staySchema.safeParse(JSON.parse(stayJson));
  if (!stay.success) return { ok: false, error: "validation_failed" };
  const key = idempotencyKey.safeParse(form.get("idempotencyKey"));
  if (!key.success) return { ok: false, error: "idempotency.key_required" };
  const comment = String(form.get("comment") ?? "").trim().slice(0, 500);
  const promoCode = String(form.get("promoCode") ?? "").trim().slice(0, 50);

  const result = await api<MyBooking>("/me/bookings", {
    method: "POST",
    headers: { "Idempotency-Key": key.data },
    body: {
      ...stayApiBody(stay.data),
      roomTypeId: stay.data.roomTypeId,
      comment: comment || null,
      promoCode: promoCode || null,
    },
  });
  if (!result.ok) {
    if (result.status === 401) redirect("/login");
    return { ...toFormState(result.problem), values: { comment, promoCode } };
  }
  redirect(`/account/bookings/${result.data.id}?created=1`);
}
