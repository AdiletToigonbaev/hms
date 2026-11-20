"use client";

import { ArrowDown, ArrowUp, ImagePlus, Trash2 } from "lucide-react";
import Image from "next/image";
import { useTranslations } from "next-intl";
import { useActionState } from "react";
import { Field } from "@/components/forms/field";
import { FormAlert } from "@/components/forms/form-alert";
import { SelectField } from "@/components/forms/native-select";
import { SubmitButton } from "@/components/forms/submit-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { FormState } from "@/lib/api/problem";
import type { Amenity, Photo, Room, RoomStatus, RoomType } from "@/lib/api/types";
import { mediaUrl } from "@/lib/api/media";
import { bedTypeLabel, bedTypes, roomStatusLabel } from "@/lib/rooms/status";
import {
  archiveRoomAction,
  archiveRoomTypeAction,
  changeRoomStatusAction,
  deletePhotoAction,
  deleteRoomAction,
  reorderPhotosAction,
  saveRoomAction,
  saveRoomTypeAction,
  setRoomTypeAmenitiesAction,
  uploadPhotoAction,
} from "./actions";

const textarea =
  "min-h-24 w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

// ---------- Категория ----------

export function RoomTypeForm({ type }: { type?: RoomType }) {
  const t = useTranslations("RoomTypes");
  const common = useTranslations("Rooms");
  const [state, action, pending] = useActionState<FormState, FormData>(saveRoomTypeAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      {type && <input type="hidden" name="id" value={type.id} />}
      {type && <input type="hidden" name="version" value={type.version} />}
      <FormAlert state={state} success={common("saved")} />
      <Field name="name" label={t("name")} required defaultValue={v.name ?? type?.name} error={e.name} />
      <div className="grid gap-4 sm:grid-cols-3">
        <Field name="capacity" type="number" min={1} max={20} label={t("capacity")} required defaultValue={v.capacity ?? type?.capacity ?? 2} error={e.capacity} />
        <Field name="bedsCount" type="number" min={1} max={20} label={t("bedsCount")} required defaultValue={v.bedsCount ?? type?.bedsCount ?? 1} error={e.bedsCount} />
        <SelectField name="bedType" label={t("bedType")} defaultValue={v.bedType ?? type?.bedType ?? "Double"} error={e.bedType} key={v.bedType ?? type?.bedType}>
          {bedTypes.map((b) => (
            <option key={b} value={b}>
              {bedTypeLabel[b]}
            </option>
          ))}
        </SelectField>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field name="basePrice" inputMode="decimal" label={t("basePrice")} required defaultValue={v.basePrice ?? type?.basePrice} error={e.basePrice} className="tabular" />
        <Field name="sortOrder" type="number" min={0} max={1000} label={t("sortOrder")} defaultValue={v.sortOrder ?? type?.sortOrder ?? 0} error={e.sortOrder} />
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="description">{t("descriptionLabel")}</Label>
        <textarea id="description" name="description" className={textarea} defaultValue={v.description ?? type?.description ?? ""} maxLength={2000} />
      </div>
      <div>
        <SubmitButton pending={pending}>{type ? common("save") : common("create")}</SubmitButton>
      </div>
    </form>
  );
}

export function AmenitiesForm({ typeId, all, selected }: { typeId: string; all: Amenity[]; selected: string[] }) {
  const t = useTranslations("RoomTypes");
  const common = useTranslations("Rooms");
  const [state, action, pending] = useActionState<FormState, FormData>(setRoomTypeAmenitiesAction, {});
  return (
    <form action={action} className="grid gap-4">
      <input type="hidden" name="id" value={typeId} />
      <FormAlert state={state} success={common("saved")} />
      <div className="grid gap-2 sm:grid-cols-2">
        {all.map((a) => (
          <label key={a.id} className="flex min-h-10 items-center gap-2.5 rounded-lg border bg-card px-3 text-sm">
            <Checkbox name="amenityIds" value={a.id} defaultChecked={selected.includes(a.id)} />
            {a.name}
          </label>
        ))}
      </div>
      <div>
        <SubmitButton pending={pending} variant="outline">
          {t("saveAmenities")}
        </SubmitButton>
      </div>
    </form>
  );
}

export function ArchiveTypeForm({ type }: { type: RoomType }) {
  const t = useTranslations("RoomTypes");
  const [state, action, pending] = useActionState<FormState, FormData>(archiveRoomTypeAction, {});
  return (
    <form action={action} className="grid gap-3">
      <input type="hidden" name="id" value={type.id} />
      <input type="hidden" name="archive" value={String(!type.isArchived)} />
      <FormAlert state={state} />
      <p className="text-sm text-muted-foreground">{t("archiveHint")}</p>
      <div>
        <SubmitButton pending={pending} variant="outline">
          {type.isArchived ? t("restore") : t("archive")}
        </SubmitButton>
      </div>
    </form>
  );
}

// ---------- Номер ----------

export function RoomForm({ room, types }: { room?: Room; types: RoomType[] }) {
  const t = useTranslations("Rooms");
  const [state, action, pending] = useActionState<FormState, FormData>(saveRoomAction, {});
  const e = state.fieldErrors ?? {};
  const v = state.values ?? {};
  return (
    <form action={action} className="grid gap-4" noValidate>
      {room && <input type="hidden" name="id" value={room.id} />}
      {room && <input type="hidden" name="version" value={room.version} />}
      <FormAlert state={state} success={t("saved")} />
      <div className="grid gap-4 sm:grid-cols-3">
        <Field name="number" label={t("number")} hint={t("numberHint")} required defaultValue={v.number ?? room?.number} error={e.number} />
        <Field name="floor" type="number" min={-5} max={200} label={t("floor")} defaultValue={v.floor ?? room?.floor ?? ""} error={e.floor} />
        <SelectField
          name="roomTypeId"
          label={t("type")}
          defaultValue={v.roomTypeId ?? room?.roomTypeId ?? ""}
          key={v.roomTypeId ?? room?.roomTypeId}
          error={e.roomTypeId}
        >
          <option value="" disabled>
            {t("chooseType")}
          </option>
          {types.map((type) => (
            <option key={type.id} value={type.id}>
              {type.name}
            </option>
          ))}
        </SelectField>
      </div>
      <fieldset className="grid gap-4 rounded-xl border border-dashed p-4">
        <legend className="px-1 text-sm font-medium">{t("overrides")}</legend>
        <p className="-mt-2 text-xs text-muted-foreground">{t("overridesHint")}</p>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Field name="priceOverride" inputMode="decimal" label={t("priceOverride")} defaultValue={v.priceOverride ?? room?.priceOverride ?? ""} error={e.priceOverride} className="tabular" />
          <Field name="capacity" type="number" min={1} max={20} label={t("capacity")} defaultValue={v.capacity ?? room?.capacity ?? ""} error={e.capacity} />
          <Field name="bedsCount" type="number" min={1} max={20} label={t("bedsCount")} defaultValue={v.bedsCount ?? room?.bedsCount ?? ""} error={e.bedsCount} />
          <SelectField name="bedType" label={t("bedType")} defaultValue={v.bedType ?? room?.bedType ?? ""} key={v.bedType ?? room?.bedType ?? ""} error={e.bedType}>
            <option value="">{t("asType")}</option>
            {bedTypes.map((b) => (
              <option key={b} value={b}>
                {bedTypeLabel[b]}
              </option>
            ))}
          </SelectField>
        </div>
      </fieldset>
      <div className="grid gap-1.5">
        <Label htmlFor="description">{t("descriptionLabel")}</Label>
        <textarea id="description" name="description" className={textarea} defaultValue={v.description ?? room?.description ?? ""} maxLength={2000} />
      </div>
      <div>
        <SubmitButton pending={pending}>{room ? t("save") : t("create")}</SubmitButton>
      </div>
    </form>
  );
}

export function StatusChangeForm({ room }: { room: Room }) {
  const t = useTranslations("Rooms");
  const [state, action, pending] = useActionState<FormState, FormData>(changeRoomStatusAction, {});
  const targets = room.manualStatusTargets as RoomStatus[];
  if (targets.length === 0) {
    return <p className="text-sm text-muted-foreground">{room.status === "Occupied" ? t("statusOnlyByCheckIn") : t("noManualTargets")}</p>;
  }
  return (
    <form action={action} className="grid gap-4" noValidate>
      <input type="hidden" name="id" value={room.id} />
      <FormAlert state={state} success={t("saved")} />
      <div className="grid gap-4 sm:grid-cols-[minmax(0,14rem)_1fr]">
        <SelectField name="status" label={t("newStatus")} defaultValue={state.values?.status ?? targets[0]} key={state.values?.status} error={state.fieldErrors?.status}>
          {targets.map((s) => (
            <option key={s} value={s}>
              {roomStatusLabel[s]}
            </option>
          ))}
        </SelectField>
        <Field name="reason" label={t("reason")} hint={t("reasonHint")} required maxLength={500} defaultValue={state.values?.reason} error={state.fieldErrors?.reason} />
      </div>
      <p className="text-xs text-muted-foreground">{t("statusOnlyByCheckIn")}</p>
      <div>
        <SubmitButton pending={pending}>{t("changeStatus")}</SubmitButton>
      </div>
    </form>
  );
}

export function RoomDangerZone({ room }: { room: Room }) {
  const t = useTranslations("Rooms");
  const [archiveState, archive, archivePending] = useActionState<FormState, FormData>(archiveRoomAction, {});
  const [deleteState, remove, deletePending] = useActionState<FormState, FormData>(deleteRoomAction, {});
  return (
    <div className="grid gap-6">
      <form action={archive} className="grid gap-3">
        <input type="hidden" name="id" value={room.id} />
        <input type="hidden" name="archive" value={String(!room.isArchived)} />
        <FormAlert state={archiveState} />
        <p className="text-sm text-muted-foreground">{t("archiveHint")}</p>
        <div>
          <SubmitButton pending={archivePending} variant="outline">
            {room.isArchived ? t("restore") : t("archive")}
          </SubmitButton>
        </div>
      </form>
      <form action={remove} className="grid gap-3">
        <input type="hidden" name="id" value={room.id} />
        <FormAlert state={deleteState} />
        <p className="text-sm text-muted-foreground">{t("deleteHint")}</p>
        <div>
          <SubmitButton pending={deletePending} variant="destructive">
            <Trash2 aria-hidden />
            {t("delete")}
          </SubmitButton>
        </div>
      </form>
    </div>
  );
}

// ---------- Фото ----------

export function PhotoManager({ owner, ownerId, photos, sortable }: { owner: "room" | "type"; ownerId: string; photos: Photo[]; sortable?: boolean }) {
  const t = useTranslations("Rooms");
  const [uploadState, upload, uploading] = useActionState<FormState, FormData>(uploadPhotoAction, {});
  const [deleteState, remove] = useActionState<FormState, FormData>(deletePhotoAction, {});
  const [orderState, reorder] = useActionState<FormState, FormData>(reorderPhotosAction, {});
  const ids = photos.map((p) => p.id);
  const moved = (index: number, delta: number) => {
    const next = [...ids];
    [next[index], next[index + delta]] = [next[index + delta], next[index]];
    return next;
  };

  return (
    <div className="grid gap-4">
      <FormAlert state={deleteState.error ? deleteState : orderState} />
      {photos.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("noPhotos")}</p>
      ) : (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {photos.map((p, i) => (
            <li key={p.id} className="group relative overflow-hidden rounded-xl border bg-muted">
              <Image src={mediaUrl(p.fileId)} alt="" width={320} height={240} unoptimized className="aspect-[4/3] w-full object-cover" />
              <div className="absolute inset-x-1 bottom-1 flex justify-end gap-1">
                {sortable && i > 0 && (
                  <form action={reorder}>
                    <PhotoOwner owner={owner} ownerId={ownerId} />
                    {moved(i, -1).map((id) => (
                      <input key={id} type="hidden" name="photoIds" value={id} />
                    ))}
                    <IconSubmit label={t("moveUp")}>
                      <ArrowUp className="size-4" aria-hidden />
                    </IconSubmit>
                  </form>
                )}
                {sortable && i < photos.length - 1 && (
                  <form action={reorder}>
                    <PhotoOwner owner={owner} ownerId={ownerId} />
                    {moved(i, 1).map((id) => (
                      <input key={id} type="hidden" name="photoIds" value={id} />
                    ))}
                    <IconSubmit label={t("moveDown")}>
                      <ArrowDown className="size-4" aria-hidden />
                    </IconSubmit>
                  </form>
                )}
                <form action={remove}>
                  <PhotoOwner owner={owner} ownerId={ownerId} />
                  <input type="hidden" name="photoId" value={p.id} />
                  <IconSubmit label={t("deletePhoto")} destructive>
                    <Trash2 className="size-4" aria-hidden />
                  </IconSubmit>
                </form>
              </div>
            </li>
          ))}
        </ul>
      )}
      <form action={upload} className="grid gap-2">
        <PhotoOwner owner={owner} ownerId={ownerId} />
        <FormAlert state={uploadState} />
        <Label htmlFor={`file-${ownerId}`}>{t("photos")}</Label>
        <div className="flex flex-wrap items-center gap-2">
          <input
            id={`file-${ownerId}`}
            name="file"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            required
            className="max-w-full text-sm file:mr-3 file:rounded-lg file:border-0 file:bg-secondary file:px-3 file:py-2 file:text-sm file:font-medium"
          />
          <SubmitButton pending={uploading} variant="outline">
            <ImagePlus aria-hidden />
            {t("upload")}
          </SubmitButton>
        </div>
        {uploadState.fieldErrors?.file && <p className="text-xs text-destructive">{uploadState.fieldErrors.file}</p>}
        <p className="text-xs text-muted-foreground">{t("photosHint")}</p>
      </form>
    </div>
  );
}

function PhotoOwner({ owner, ownerId }: { owner: "room" | "type"; ownerId: string }) {
  return (
    <>
      <input type="hidden" name="owner" value={owner} />
      <input type="hidden" name="ownerId" value={ownerId} />
    </>
  );
}

function IconSubmit({ label, destructive, children }: { label: string; destructive?: boolean; children: React.ReactNode }) {
  return (
    <button
      type="submit"
      aria-label={label}
      title={label}
      className={
        destructive
          ? "grid size-8 place-items-center rounded-lg bg-card/90 text-destructive shadow-sm hover:bg-card"
          : "grid size-8 place-items-center rounded-lg bg-card/90 shadow-sm hover:bg-card"
      }
    >
      {children}
    </button>
  );
}
