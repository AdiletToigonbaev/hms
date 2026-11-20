import { CircleCheck } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/states/states";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { Discount, ExtraService, Rate, RoomType } from "@/lib/api/types";
import { describeDays } from "@/lib/bookings/status";
import { formatMoney, formatStayDate } from "@/lib/format";
import { DeleteRateButton, DiscountForm, RateForm, ServiceForm, ToggleActiveButton } from "./pricing-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Pricing"))("title") };
}

/** Редактирование — через ?edit=rate:<id> | discount:<id> | service:<id>: форма раздела заполняется выбранной записью. */
function parseEdit(value: unknown): { kind: string; id: string } | null {
  if (typeof value !== "string") return null;
  const match = /^(rate|discount|service):([0-9a-f-]{36})$/i.exec(value);
  return match ? { kind: match[1], id: match[2] } : null;
}

export default async function PricingPage({ searchParams }: PageProps<"/owner/pricing">) {
  const params = await searchParams;
  const edit = parseEdit(params.edit);
  const saved = params.saved === "1";

  const [t, errors, types, rates, discounts, services] = await Promise.all([
    getTranslations("Pricing"),
    getTranslations("Errors"),
    api<RoomType[]>("/room-types"),
    api<Rate[]>("/rates"),
    api<Discount[]>("/discounts"),
    api<ExtraService[]>("/services"),
  ]);
  if (!types.ok || !rates.ok || !discounts.ok || !services.ok) return <ErrorState title={errors("unknown")} />;

  const typeName = new Map(types.data.map((type) => [type.id, type.name]));
  const editRate = edit?.kind === "rate" ? rates.data.find((r) => r.id === edit.id) : undefined;
  const editDiscount = edit?.kind === "discount" ? discounts.data.find((d) => d.id === edit.id) : undefined;
  const editService = edit?.kind === "service" ? services.data.find((s) => s.id === edit.id) : undefined;
  const editLink = (kind: string, id: string) => `/owner/pricing?edit=${kind}:${id}#${kind}-form`;
  const status = (active: boolean) => <Badge variant={active ? "secondary" : "outline"}>{active ? t("active") : t("inactive")}</Badge>;
  const editButton = (kind: string, id: string, name: string) => (
    <Link href={editLink(kind, id)} className={buttonVariants({ variant: "ghost", size: "sm" })} aria-label={`${t("edit")}: ${name}`}>
      {t("edit")}
    </Link>
  );

  return (
    <div className="grid gap-6">
      <PageHeader title={t("title")} description={t("description")} />
      {saved && (
        <Alert role="status">
          <CircleCheck aria-hidden />
          <AlertDescription>{t("saved")}</AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-xl">{t("rates")}</h2>
          </CardTitle>
          <CardDescription>{t("ratesHint")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-6">
          {rates.data.length > 0 && (
            <div className="overflow-x-auto rounded-xl border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("colName")}</TableHead>
                    <TableHead>{t("colType")}</TableHead>
                    <TableHead>{t("colPeriod")}</TableHead>
                    <TableHead>{t("colDays")}</TableHead>
                    <TableHead className="text-right">{t("colPrice")}</TableHead>
                    <TableHead className="text-right">{t("colPriority")}</TableHead>
                    <TableHead>{t("colStatus")}</TableHead>
                    <TableHead />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rates.data.map((r) => (
                    <TableRow key={r.id}>
                      <TableCell className="font-medium">{r.name}</TableCell>
                      <TableCell>{typeName.get(r.roomTypeId) ?? "—"}</TableCell>
                      <TableCell className="tabular whitespace-nowrap">
                        {formatStayDate(r.dateFrom)} — {formatStayDate(r.dateTo)}
                      </TableCell>
                      <TableCell>{describeDays(r.daysOfWeek)}</TableCell>
                      <TableCell className="tabular text-right whitespace-nowrap">
                        {formatMoney(r.price)} <span className="text-xs text-muted-foreground">{r.unit === "Hour" ? t("perHourShort") : t("perNightShort")}</span>
                      </TableCell>
                      <TableCell className="tabular text-right">{r.priority}</TableCell>
                      <TableCell>{status(r.isActive)}</TableCell>
                      <TableCell className="text-right whitespace-nowrap">
                        {editButton("rate", r.id, r.name)}
                        <DeleteRateButton id={r.id} name={r.name} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          <section id="rate-form" className="grid scroll-mt-6 gap-3">
            <h3 className="font-heading text-lg">{editRate ? t("editRate") : t("newRate")}</h3>
            {types.data.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("noTypes")}</p>
            ) : (
              <RateForm key={editRate?.id ?? "new"} rate={editRate} types={types.data.filter((type) => !type.isArchived || type.id === editRate?.roomTypeId)} />
            )}
          </section>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-xl">{t("discounts")}</h2>
          </CardTitle>
          <CardDescription>{t("discountsHint")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-6">
          {discounts.data.length > 0 && (
            <div className="overflow-x-auto rounded-xl border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("colName")}</TableHead>
                    <TableHead>{t("colCode")}</TableHead>
                    <TableHead className="text-right">{t("colValue")}</TableHead>
                    <TableHead>{t("colConditions")}</TableHead>
                    <TableHead>{t("colStatus")}</TableHead>
                    <TableHead />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {discounts.data.map((d) => (
                    <TableRow key={d.id}>
                      <TableCell className="font-medium">{d.name}</TableCell>
                      <TableCell className="tabular">{d.code ?? "—"}</TableCell>
                      <TableCell className="tabular text-right whitespace-nowrap">{d.type === "Percent" ? `${d.value} %` : formatMoney(d.value)}</TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {[
                          d.validFrom || d.validTo
                            ? `${d.validFrom ? formatStayDate(d.validFrom) : "…"} — ${d.validTo ? formatStayDate(d.validTo) : "…"}`
                            : null,
                          d.minNights > 0 ? t("nights", { n: d.minNights }) : null,
                          d.minHours > 0 ? t("hoursMin", { n: d.minHours }) : null,
                        ]
                          .filter(Boolean)
                          .join(" · ") || t("any")}
                      </TableCell>
                      <TableCell>{status(d.isActive)}</TableCell>
                      <TableCell className="text-right whitespace-nowrap">
                        {editButton("discount", d.id, d.name)}
                        <ToggleActiveButton id={d.id} kind="discounts" active={d.isActive} name={d.name} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          <section id="discount-form" className="grid scroll-mt-6 gap-3">
            <h3 className="font-heading text-lg">{editDiscount ? t("editDiscount") : t("newDiscount")}</h3>
            <DiscountForm key={editDiscount?.id ?? "new"} discount={editDiscount} />
          </section>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-xl">{t("services")}</h2>
          </CardTitle>
          <CardDescription>{t("servicesHint")}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-6">
          {services.data.length > 0 && (
            <div className="overflow-x-auto rounded-xl border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("colName")}</TableHead>
                    <TableHead className="text-right">{t("colPrice")}</TableHead>
                    <TableHead>{t("colKind")}</TableHead>
                    <TableHead>{t("colStatus")}</TableHead>
                    <TableHead />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {services.data.map((s) => (
                    <TableRow key={s.id}>
                      <TableCell>
                        <span className="font-medium">{s.name}</span>
                        {s.description && <span className="block text-xs text-muted-foreground">{s.description}</span>}
                      </TableCell>
                      <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(s.price)}</TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {s.isPerNight ? t("perNightShort") : t("onceShort")} · {s.isPublic ? t("publicShort") : t("staffOnly")}
                      </TableCell>
                      <TableCell>{status(s.isActive)}</TableCell>
                      <TableCell className="text-right whitespace-nowrap">
                        {editButton("service", s.id, s.name)}
                        <ToggleActiveButton id={s.id} kind="services" active={s.isActive} name={s.name} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
          <section id="service-form" className="grid scroll-mt-6 gap-3">
            <h3 className="font-heading text-lg">{editService ? t("editService") : t("newService")}</h3>
            <ServiceForm key={editService?.id ?? "new"} service={editService} />
          </section>
        </CardContent>
      </Card>
    </div>
  );
}
