import { ChevronLeft, ChevronRight, ScrollText } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { readHour } from "@/components/dashboard/period-filter";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { Button, buttonVariants } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { Paged, Staff } from "@/lib/api/types";
import { auditEntityHref, auditGroups, auditLabel } from "@/lib/audit/labels";
import { addDays, hotelToday } from "@/lib/bookings/status";
import { formatHotelDateTime } from "@/lib/format";
import { cn } from "@/lib/utils";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Audit"))("title") };
}

interface AuditLog {
  id: string;
  at: string;
  userId: string | null;
  userName: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  before: unknown;
  after: unknown;
  ip: string | null;
}

const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);
const iso = /^\d{4}-\d{2}-\d{2}$/;
const uuid = /^[0-9a-f-]{36}$/i;
const PAGE_SIZE = 50;

/** Час суток гостиницы (UTC+6, без перехода на летнее время) в ISO — для фильтра API. */
const at = (date: string, hour = 0) => `${date}T${String(hour).padStart(2, "0")}:00:00+06:00`;
const pad = (n: number) => String(n).padStart(2, "0");

/**
 * Журнал действий (ТЗ §3.1 «Контроль действий сотрудников»): кто, что и когда сделал.
 * Персональные данные гостей в журнал не пишутся — только названия изменённых полей.
 */
export default function AuditPage({ searchParams }: PageProps<"/owner/audit">) {
  return (
    <Suspense fallback={<LoadingState rows={8} />}>
      <AuditContent searchParams={searchParams} />
    </Suspense>
  );
}

async function AuditContent({ searchParams }: Pick<PageProps<"/owner/audit">, "searchParams">) {
  const params = await searchParams;
  const today = hotelToday(hotelTimeZone);
  const from = iso.test(str(params.from) ?? "") ? str(params.from)! : addDays(today, -6);
  const to = iso.test(str(params.to) ?? "") ? str(params.to)! : today;
  const userId = uuid.test(str(params.userId) ?? "") ? str(params.userId)! : "";
  const group = auditGroups.some((g) => g.prefix === str(params.action)) ? str(params.action)! : "";
  const page = Math.max(1, Number(str(params.page)) || 1);
  // Время: «сегодня с 06:00 до 22:00»; без часов — сутки целиком.
  const fromHour = readHour(params.fromHour);
  const toHour = readHour(params.toHour);

  const query = new URLSearchParams({ from: at(from, fromHour ?? 0), to: toHour === null ? at(addDays(to, 1)) : at(to, toHour), page: String(page), pageSize: String(PAGE_SIZE) });
  if (userId) query.set("userId", userId);
  if (group) query.set("action", group);

  const [t, result, staff] = await Promise.all([
    getTranslations("Audit"),
    api<Paged<AuditLog>>(`/audit-logs?${query}`),
    api<Paged<Staff>>("/staff?pageSize=100"),
  ]);
  const link = (p: number) => {
    const q = new URLSearchParams({ from, to, page: String(p) });
    if (fromHour !== null) q.set("fromHour", String(fromHour));
    if (toHour !== null) q.set("toHour", String(toHour));
    if (userId) q.set("userId", userId);
    if (group) q.set("action", group);
    return `/owner/audit?${q}`;
  };
  const totalPages = result.ok ? Math.max(1, result.data.totalPages) : 1;

  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} />
      <form method="get" className="flex flex-wrap items-end gap-3">
        <div className="grid gap-1.5">
          <span className="text-sm font-medium">{t("period")}</span>
          <DateRangePicker key={`${from}-${to}`} mode="period" startName="from" endName="to" defaultStart={from} defaultEnd={to} today={today} startLabel="С" endLabel="По" size="compact" />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="fromHour">{t("fromHour")}</Label>
          <NativeSelect id="fromHour" name="fromHour" defaultValue={fromHour === null ? "" : String(fromHour)} className="w-28">
            <option value="">00:00</option>
            {Array.from({ length: 23 }, (_, i) => i + 1).map((h) => (
              <option key={h} value={h}>
                {pad(h)}:00
              </option>
            ))}
          </NativeSelect>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="toHour">{t("toHour")}</Label>
          <NativeSelect id="toHour" name="toHour" defaultValue={toHour === null ? "" : String(toHour)} className="w-28">
            {Array.from({ length: 23 }, (_, i) => i + 1).map((h) => (
              <option key={h} value={h}>
                {pad(h)}:00
              </option>
            ))}
            <option value="">24:00</option>
          </NativeSelect>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="userId">{t("who")}</Label>
          <NativeSelect id="userId" name="userId" defaultValue={userId} className="w-52">
            <option value="">{t("anyone")}</option>
            {staff.ok &&
              staff.data.items.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName}
                </option>
              ))}
          </NativeSelect>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="action">{t("what")}</Label>
          <NativeSelect id="action" name="action" defaultValue={group} className="w-52">
            <option value="">{t("anything")}</option>
            {auditGroups.map((g) => (
              <option key={g.prefix} value={g.prefix}>
                {g.label}
              </option>
            ))}
          </NativeSelect>
        </div>
        <Button type="submit" variant="outline">
          {t("show")}
        </Button>
      </form>

      {!result.ok ? (
        <ErrorState title={t("loadError")} />
      ) : result.data.items.length === 0 ? (
        <EmptyState icon={ScrollText} title={t("empty")} description={t("emptyHint")} />
      ) : (
        <>
          <p className="text-sm text-muted-foreground">{t("found", { n: result.data.totalCount })}</p>
          <div className="overflow-x-auto rounded-2xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("colAt")}</TableHead>
                  <TableHead>{t("colWho")}</TableHead>
                  <TableHead>{t("colWhat")}</TableHead>
                  <TableHead>{t("colObject")}</TableHead>
                  <TableHead>{t("colDetails")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.data.items.map((x) => {
                  const href = auditEntityHref(x.entityType, x.entityId);
                  const details = [x.before ? `${t("before")}: ${JSON.stringify(x.before, null, 1)}` : null, x.after ? `${t("after")}: ${JSON.stringify(x.after, null, 1)}` : null]
                    .filter(Boolean)
                    .join("\n");
                  return (
                    <TableRow key={x.id} className="align-top">
                      <TableCell className="tabular text-sm whitespace-nowrap">{formatHotelDateTime(x.at)}</TableCell>
                      <TableCell className="text-sm">
                        {x.userName ?? (x.userId ? <span className="text-muted-foreground">{t("deletedUser")}</span> : <span className="text-muted-foreground">{t("system")}</span>)}
                      </TableCell>
                      <TableCell className={cn("text-sm", x.action.includes("failed") || x.action.includes("reuse") ? "text-destructive" : "")}>{auditLabel(x.action)}</TableCell>
                      <TableCell className="text-sm">
                        {href ? (
                          <Link href={href} className="text-primary underline-offset-4 hover:underline">
                            {t("open")}
                          </Link>
                        ) : (
                          <span className="text-muted-foreground">{x.entityType ?? "—"}</span>
                        )}
                      </TableCell>
                      <TableCell className="max-w-md text-xs">
                        {details ? (
                          <details>
                            <summary className="cursor-pointer text-muted-foreground">{t("changes")}</summary>
                            <pre className="mt-1 max-h-60 overflow-auto rounded-lg bg-secondary/60 p-2 whitespace-pre-wrap">{details}</pre>
                          </details>
                        ) : (
                          <span className="text-muted-foreground">—</span>
                        )}
                        {x.ip && <span className="mt-1 block text-muted-foreground">IP {x.ip}</span>}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
          {totalPages > 1 && (
            <nav aria-label={t("pages")} className="flex items-center justify-between gap-3">
              {page > 1 ? (
                <Link href={link(page - 1)} className={buttonVariants({ variant: "outline", size: "sm" })}>
                  <ChevronLeft aria-hidden />
                  {t("newer")}
                </Link>
              ) : (
                <span />
              )}
              <span className="text-sm text-muted-foreground">{t("pageOf", { page, total: totalPages })}</span>
              {page < totalPages ? (
                <Link href={link(page + 1)} className={buttonVariants({ variant: "outline", size: "sm" })}>
                  {t("older")}
                  <ChevronRight aria-hidden />
                </Link>
              ) : (
                <span />
              )}
            </nav>
          )}
        </>
      )}
    </div>
  );
}
