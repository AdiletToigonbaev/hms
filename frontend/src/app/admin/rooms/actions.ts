"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { api } from "@/lib/api/server";
import { toFormState, withValues, type FormState } from "@/lib/api/problem";
import type { Photo, Room, RoomType } from "@/lib/api/types";
import { fieldErrors } from "@/lib/validation/auth";
import { roomSchema, roomTypeSchema, statusChangeSchema } from "@/lib/validation/rooms";

// Каждое действие — отдельная точка входа (прямой POST): права проверяет API на каждом вызове.

const TYPE_FIELDS = ["name", "description", "capacity", "bedsCount", "bedType", "basePrice", "sortOrder"] as const;
const ROOM_FIELDS = ["roomTypeId", "number", "floor", "capacity", "bedsCount", "bedType", "priceOverride", "description"] as const;
const uuid = z.string().uuid();

function authGuard(status: number) {
  if (status === 401) redirect("/login");
}

// ---------- Категории ----------

export async function saveRoomTypeAction(_: FormState, form: FormData): Promise<FormState> {
  const id = form.get("id") ? uuid.parse(form.get("id")) : null;
  const parsed = roomTypeSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, TYPE_FIELDS);

  const body = { ...parsed.data, version: form.get("version") ? Number(form.get("version")) : null };
  const result = await api<RoomType>(id ? `/room-types/${id}` : "/room-types", { method: id ? "PUT" : "POST", body });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, TYPE_FIELDS);
  }
  if (!id) redirect(`/admin/room-types/${result.data.id}`);
  revalidatePath(`/admin/room-types/${id}`);
  return { ok: true };
}

export async function setRoomTypeAmenitiesAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const amenityIds = form.getAll("amenityIds").map(String).filter((v) => uuid.safeParse(v).success);
  const result = await api(`/room-types/${id}/amenities`, { method: "PUT", body: { amenityIds } });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/room-types/${id}`);
  return { ok: true };
}

export async function archiveRoomTypeAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const archive = form.get("archive") === "true";
  const result = await api(`/room-types/${id}/${archive ? "archive" : "restore"}`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/room-types/${id}`);
  return { ok: true };
}

// ---------- Номера ----------

export async function saveRoomAction(_: FormState, form: FormData): Promise<FormState> {
  const id = form.get("id") ? uuid.parse(form.get("id")) : null;
  const parsed = roomSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ROOM_FIELDS);

  const body = { ...parsed.data, version: form.get("version") ? Number(form.get("version")) : null };
  const result = await api<Room>(id ? `/rooms/${id}` : "/rooms", { method: id ? "PUT" : "POST", body });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ROOM_FIELDS);
  }
  if (!id) redirect(`/admin/rooms/${result.data.id}`);
  revalidatePath(`/admin/rooms/${id}`);
  return { ok: true };
}

export async function changeRoomStatusAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const parsed = statusChangeSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return withValues({ ok: false, fieldErrors: fieldErrors(parsed.error) }, form, ["status", "reason"]);

  const result = await api<Room>(`/rooms/${id}/status`, { method: "POST", body: parsed.data });
  if (!result.ok) {
    authGuard(result.status);
    return withValues(toFormState(result.problem), form, ["status", "reason"]);
  }
  revalidatePath(`/admin/rooms/${id}`);
  return { ok: true };
}

export async function archiveRoomAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const archive = form.get("archive") === "true";
  const result = await api(`/rooms/${id}/${archive ? "archive" : "restore"}`, { method: "POST" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidatePath(`/admin/rooms/${id}`);
  return { ok: true };
}

export async function deleteRoomAction(_: FormState, form: FormData): Promise<FormState> {
  const id = uuid.parse(form.get("id"));
  const result = await api(`/rooms/${id}`, { method: "DELETE" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  redirect("/admin/rooms");
}

// ---------- Фото (категории и номера) ----------

function photoBase(form: FormData): string {
  const owner = form.get("owner") === "room" ? "rooms" : "room-types";
  return `/${owner}/${uuid.parse(form.get("ownerId"))}/photos`;
}

function revalidateOwner(form: FormData) {
  const owner = form.get("owner") === "room" ? "rooms" : "room-types";
  revalidatePath(`/admin/${owner}/${form.get("ownerId")}`);
}

export async function uploadPhotoAction(_: FormState, form: FormData): Promise<FormState> {
  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) return { ok: false, fieldErrors: { file: "Выберите файл изображения." } };
  if (file.size > 10 * 1024 * 1024) return { ok: false, fieldErrors: { file: "Файл больше 10 МБ." } };

  const upload = new FormData();
  upload.set("file", file, file.name);
  const result = await api<Photo>(photoBase(form), { method: "POST", body: upload });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidateOwner(form);
  return { ok: true };
}

export async function deletePhotoAction(_: FormState, form: FormData): Promise<FormState> {
  const photoId = uuid.parse(form.get("photoId"));
  const result = await api(`${photoBase(form)}/${photoId}`, { method: "DELETE" });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidateOwner(form);
  return { ok: true };
}

export async function reorderPhotosAction(_: FormState, form: FormData): Promise<FormState> {
  const photoIds = form.getAll("photoIds").map(String).filter((v) => uuid.safeParse(v).success);
  const result = await api(`${photoBase(form)}/order`, { method: "PUT", body: { photoIds } });
  if (!result.ok) {
    authGuard(result.status);
    return toFormState(result.problem);
  }
  revalidateOwner(form);
  return { ok: true };
}

