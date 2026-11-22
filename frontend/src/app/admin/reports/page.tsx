import { Download, FileBarChart } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PeriodFilter, readHour } from "@/components/dashboard/period-filter";
import { StatTile } from "@/components/dashboard/stat-tile";
import { HelpTip } from "@/components/forms/help-tip";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState, LoadingState } from "@/components/states/states";
import { buttonVariants } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { BookingStatus, Paged, PaymentMethod, ReportColumnType, ReportTable, RoomType, Staff } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { bookingStatusLabel, bookingStatuses, hotelToday } from "@/lib/bookings/status";
import { paymentMethodLabel, paymentMethods } from "@/lib/finance/labels";
import { formatHotelLocal, formatMoney, formatStayDate } from "@/lib/format";
import { cn } from "@/lib/utils";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Reports"))("title") };
}

/** Отчёты ТЗ §12: финансовые — владельцу и сотруднику с правом «Финансовые отчёты», остальные — владельцу. */
const reports = [
  { type: "financial", finance: true },
  { type: "bookings", finance: true },
  { type: "payments", finance: true },
  { type: "refunds", finance: true },
  { type: "expenses", finance: false },
  { type: "guests", finance: false },
  { type: "rooms", finance: false },
  { type: "staff", finance: false },
] as const;
type ReportType = (typeof reports)[number]["type"];

const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);
const iso = /^\d{4}-\d{2}-\d{2}$/;
const uuid = /^[0-9a-f-]{36}$/i;

/** Значение ячейки по типу колонки — так же, как в выгрузке. */
function cell(value: string | number | null, type: ReportColumnType): string {
  if (value === null || value === "") return "";
  if (typeof value === "number") {
    if (type === "money") return formatMoney(value);
    if (type === "percent") return `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 1 }).format(value * 100)} %`;
    return new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 2 }).format(value);
  }
  if (type === "date" && iso.test(value)) return formatStayDate(value);
  if (type === "datetime" && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)) return formatHotelLocal(value);
  return value;
}

/** «22.11.2026», «22.11.2026, 06:00–22:00», «20.11.2026 06:00 — 22.11.2026 22:00». */
function periodText(r: ReportTable): string {
  const from = formatStayDate(r.from);
  const to = formatStayDate(r.to);
  if (r.fromHour === null && r.toHour === null) return r.from === r.to ? from : `${from} — ${to}`;
  const fh = `${String(r.fromHour ?? 0).padStart(2, "0")}:00`;
  const th = `${String(r.toHour ?? 24).padStart(2, "0")}:00`;
  return r.from === r.to ? `${from}, ${fh}–${th}` : `${from} ${fh} — ${to} ${th}`;
}

const numeric = (type: ReportColumnType) => type === "money" || type === "int" || type === "number" || type === "percent";

export default function ReportsPage({ searchParams }: PageProps<"/admin/reports">) {
  return (
    <Suspense fallback={<LoadingState rows={6} />}>
      <Reports searchParams={searchParams} />
    </Suspense>
  );
}

async function Reports({ searchParams }: Pick<PageProps<"/admin/reports">, "searchParams">) {
  const params = await searchParams;
  const user = await getCurrentUser();
  const perms = user?.permissions ?? [];
  const isOwnerView = perms.includes("dashboard.owner.view");
  const available = reports.filter((r) => isOwnerView || (r.finance && perms.includes("finance.report.view")));
  const type: ReportType = available.find((r) => r.type === str(params.type))?.type ?? available[0]?.type ?? "financial";
  const canExport = perms.includes("report.export");
  const today = hotelToday(hotelTimeZone);

  const period = ["day", "week", "month", "year"].includes(str(params.period) ?? "") ? str(params.period)! : null;
  const from = iso.test(str(params.from) ?? "") ? str(params.from)! : null;
  const to = iso.test(str(params.to) ?? "") ? str(params.to)! : null;
  const roomTypeId = uuid.test(str(params.roomTypeId) ?? "") ? str(params.roomTypeId)! : null;
  const method = paymentMethods.includes(str(params.paymentMethod) as PaymentMethod) ? str(params.paymentMethod)! : null;
  const status = bookingStatuses.includes(str(params.bookingStatus) as BookingStatus) ? str(params.bookingStatus)! : null;
  const staffId = uuid.test(str(params.staffId) ?? "") ? str(params.staffId)! : null;

  const query = new URLSearchParams();
  if (period) query.set("period", period);
  else if (from || to) {
    if (from) query.set("from", from);
    if (to) query.set("to", to);
  } else query.set("period", "month");
  if (roomTypeId) query.set("roomTypeId", roomTypeId);
  if (method) query.set("paymentMethod", method);
  if (status && type === "bookings") query.set("bookingStatus", status);
  if (staffId) query.set("staffId", staffId);
  const fromHour = readHour(params.fromHour);
  const toHour = readHour(params.toHour);
  if (fromHour !== null) query.set("fromHour", String(fromHour));
  if (toHour !== null) query.set("toHour", String(toHour));

  const [t, result, types, staff] = await Promise.all([
    getTranslations("Reports"),
    available.length ? api<ReportTable>(`/reports/${type}?${query}`) : null,
    api<RoomType[]>("/room-types"),
    perms.includes("staff.manage") ? api<Paged<Staff>>("/staff?pageSize=100") : null,
  ]);
  if (!available.length) return <ErrorState title={t("forbidden")} />;
  const roomTypes = types.ok ? types.data.filter((x) => !x.isArchived).map((x) => ({ id: x.id, name: x.name })) : [];
  const typeLink = (r: ReportType) => {
    const q = new URLSearchParams(query);
    q.delete("bookingStatus");
    q.set("type", r);
    return `/admin/reports?${q}`;
  };
  const exportLink = (format: "xlsx" | "pdf") => {
    const q = new URLSearchParams(query);
    q.set("type", type);
    q.set("format", format);
    return `/admin/reports/export?${q}`;
  };

  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          canExport && result?.ok ? (
            <>
              <a href={exportLink("xlsx")} className={buttonVariants({ variant: "outline" })} download>
                <Download aria-hidden />
                Excel
              </a>
              <a href={exportLink("pdf")} className={buttonVariants({ variant: "outline" })} download>
                <Download aria-hidden />
                PDF
              </a>
            </>
          ) : undefined
        }
      />
      {/* Вид отчёта — вкладки с подчёркиванием (выбор «что смотреть»); период и фильтры — ниже, отдельной панелью. */}
      <nav aria-label={t("title")} className="-mb-2 overflow-x-auto overflow-y-hidden shadow-[inset_0_-1px_0_var(--border)]">
        <ul className="flex min-w-max gap-1">
          {available.map((r) => (
            <li key={r.type}>
              <Link
                href={typeLink(r.type)}
                aria-current={r.type === type ? "page" : undefined}
                className={cn(
                  "inline-flex min-h-11 items-center border-b-2 px-3 text-sm font-medium transition",
                  r.type === type ? "border-primary text-primary" : "border-transparent text-muted-foreground hover:border-border hover:text-foreground",
                )}
              >
                {t(`type_${r.type}`)}
              </Link>
            </li>
          ))}
        </ul>
      </nav>

      <PeriodFilter
        basePath="/admin/reports"
        activePeriod={period ?? (from || to ? null : "month")}
        from={result?.ok ? result.data.from : (from ?? today)}
        to={result?.ok ? result.data.to : (to ?? today)}
        today={today}
        roomTypes={roomTypes}
        roomTypeId={roomTypeId}
        fromHour={fromHour}
        toHour={toHour}
        hidden={{ type }}
        keep={Object.fromEntries(
          [
            ["paymentMethod", method],
            ["bookingStatus", type === "bookings" ? status : null],
            ["staffId", staffId],
          ].filter((e): e is [string, string] => e[1] !== null),
        )}
      >
        {["financial", "payments", "refunds", "expenses"].includes(type) && (
          <div className="grid gap-1.5">
            <Label htmlFor="paymentMethod">{t("method")}</Label>
            <NativeSelect id="paymentMethod" name="paymentMethod" defaultValue={method ?? ""} className="w-44">
              <option value="">{t("any")}</option>
              {paymentMethods.map((m) => (
                <option key={m} value={m}>
                  {paymentMethodLabel[m]}
                </option>
              ))}
            </NativeSelect>
          </div>
        )}
        {type === "bookings" && (
          <div className="grid gap-1.5">
            <Label htmlFor="bookingStatus">{t("status")}</Label>
            <NativeSelect id="bookingStatus" name="bookingStatus" defaultValue={status ?? ""} className="w-48">
              <option value="">{t("any")}</option>
              {bookingStatuses
                .filter((s) => s !== "Expired")
                .map((s) => (
                  <option key={s} value={s}>
                    {bookingStatusLabel[s]}
                  </option>
                ))}
            </NativeSelect>
          </div>
        )}
        {staff?.ok && type !== "financial" && type !== "guests" && type !== "rooms" && (
          <div className="grid gap-1.5">
            <Label htmlFor="staffId">{t("staff")}</Label>
            <NativeSelect id="staffId" name="staffId" defaultValue={staffId ?? ""} className="w-48">
              <option value="">{t("anyone")}</option>
              {staff.data.items.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName}
                </option>
              ))}
            </NativeSelect>
          </div>
        )}
      </PeriodFilter>

      {!result?.ok ? <ErrorState title={t("loadError")} description={result?.problem.title} /> : <ReportView report={result.data} />}
    </div>
  );
}

async function ReportView({ report }: { report: ReportTable }) {
  const t = await getTranslations("Reports");
  return (
    <section aria-labelledby="report-title" className="grid gap-4">
      <div className="grid gap-1">
        <h2 id="report-title" className="font-heading text-2xl">
          {report.title}
        </h2>
        <p className="text-sm text-muted-foreground">
          {periodText(report)}
          {report.filters.length > 0 && ` · ${report.filters.join(" · ")}`}
        </p>
        {report.note && <p className="text-sm text-muted-foreground italic">{report.note}</p>}
      </div>
      {report.summary.length > 0 && (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {report.summary.map((s) => (
            <StatTile
              key={s.label}
              label={s.label}
              value={s.value === null ? "—" : cell(s.value, s.type)}
              help={s.hint ? <HelpTip title={s.label} text={s.hint} example={s.example ?? undefined} /> : undefined}
            />
          ))}
        </div>
      )}
      {report.rows.length === 0 ? (
        <EmptyState icon={FileBarChart} title={t("empty")} description={t("emptyHint")} />
      ) : (
        <div className="overflow-x-auto rounded-2xl border bg-card">
          <Table>
            <TableHeader>
              <TableRow>
                {report.columns.map((c) => (
                  <TableHead key={c.key} className={cn(numeric(c.type) && "text-right")}>
                    {c.title}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {report.rows.map((row, i) => (
                <TableRow key={i}>
                  {report.columns.map((c, j) => (
                    <TableCell
                      key={c.key}
                      className={cn("text-sm", numeric(c.type) && "tabular text-right whitespace-nowrap", c.type === "datetime" && "tabular whitespace-nowrap")}
                    >
                      {cell(row[j], c.type)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
            {report.totals && (
              <TableFooter>
                <TableRow>
                  {report.columns.map((c, j) => (
                    <TableCell key={c.key} className={cn("font-semibold", numeric(c.type) && "tabular text-right whitespace-nowrap")}>
                      {cell(report.totals![j], c.type)}
                    </TableCell>
                  ))}
                </TableRow>
              </TableFooter>
            )}
          </Table>
        </div>
      )}
    </section>
  );
}
