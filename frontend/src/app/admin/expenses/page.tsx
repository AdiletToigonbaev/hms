import { Receipt } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { DateRangePicker } from "@/components/forms/date-range-picker";
import { NativeSelect } from "@/components/forms/native-select";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { ExpenseCategory, ExpensePage } from "@/lib/api/types";
import { hotelToday } from "@/lib/bookings/status";
import { isIsoDate } from "@/lib/calendar";
import { paymentMethodLabel } from "@/lib/finance/labels";
import { formatMoney, formatStayDate } from "@/lib/format";
import { CategoryManager, ExpenseForm } from "../payments/finance-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Finance"))("expensesTitle") };
}

const uuid = /^[0-9a-f-]{36}$/i;

/** Расходы: сотрудник вносит и видит свои; владелец — все, по категориям, правит и аннулирует. */
export default async function ExpensesPage({ searchParams }: PageProps<"/admin/expenses">) {
  const params = await searchParams;
  const today = hotelToday(hotelTimeZone);
  const from = isIsoDate(params.from) ? params.from : `${today.slice(0, 8)}01`;
  const to = isIsoDate(params.to) ? params.to : today;
  const categoryId = typeof params.category === "string" && uuid.test(params.category) ? params.category : "";
  const showVoided = params.voided === "1";
  const page = Math.max(1, Number(params.page) || 1);
  const query = new URLSearchParams({ from, to, page: String(page), pageSize: "50" });
  if (categoryId) query.set("categoryId", categoryId);
  if (showVoided) query.set("includeVoided", "true");

  const [t, result, categories] = await Promise.all([
    getTranslations("Finance"),
    api<ExpensePage>(`/expenses?${query}`),
    api<ExpenseCategory[]>("/expense-categories"),
  ]);
  const cats = categories.ok ? categories.data : [];
  const link = (p: number) => {
    const q = new URLSearchParams({ from, to, page: String(p) });
    if (categoryId) q.set("category", categoryId);
    if (showVoided) q.set("voided", "1");
    return `/admin/expenses?${q}`;
  };

  return (
    <div className="grid gap-6">
      <PageHeader title={t("expensesTitle")} description={result.ok && result.data.canManage ? t("expensesOwner") : t("expensesStaff")} />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div className="grid min-w-0 content-start gap-4">
          <form method="get" className="flex flex-wrap items-end gap-2">
            <DateRangePicker key={`${from}-${to}`} mode="period" size="compact" startName="from" endName="to" defaultStart={from} defaultEnd={to} today={today} startLabel={t("period")} />
            <NativeSelect name="category" defaultValue={categoryId} aria-label={t("category")} className="h-9 w-auto">
              <option value="">{t("allCategories")}</option>
              {cats.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </NativeSelect>
            {result.ok && result.data.canManage && (
              <label className="flex h-9 items-center gap-2 text-sm">
                <input type="checkbox" name="voided" value="1" defaultChecked={showVoided} className="size-4 accent-primary" />
                {t("showVoided")}
              </label>
            )}
            <Button type="submit" variant="outline">
              {t("show")}
            </Button>
          </form>

          {!result.ok ? (
            <ErrorState title={t("loadError")} />
          ) : (
            <>
              <section aria-label={t("expensesTotal")} className="grid gap-3 sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
                <div className="grid content-start gap-1 rounded-2xl border bg-card p-4 shadow-xs">
                  <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t("expensesTotal")}</span>
                  <span className="tabular font-heading text-2xl">{formatMoney(result.data.total)}</span>
                </div>
                {Object.keys(result.data.byCategory).length > 0 && (
                  <ul className="grid gap-1 rounded-2xl border bg-card p-4 text-sm shadow-xs">
                    {Object.entries(result.data.byCategory)
                      .sort((a, b) => b[1] - a[1])
                      .map(([name, sum]) => (
                        <li key={name} className="flex justify-between gap-3">
                          <span>{name}</span>
                          <span className="tabular">{formatMoney(sum)}</span>
                        </li>
                      ))}
                  </ul>
                )}
              </section>

              {result.data.page.items.length === 0 ? (
                <EmptyState icon={Receipt} title={t("noExpenses")} description={t("noExpensesHint")} />
              ) : (
                <div className="overflow-x-auto rounded-2xl border bg-card">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>{t("date")}</TableHead>
                        <TableHead>{t("category")}</TableHead>
                        <TableHead>{t("description")}</TableHead>
                        <TableHead>{t("method")}</TableHead>
                        <TableHead>{t("colBy")}</TableHead>
                        <TableHead className="text-right">{t("amount")}</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {result.data.page.items.map((e) => (
                        <TableRow key={e.id} className={e.isVoided ? "text-muted-foreground" : undefined}>
                          <TableCell className="tabular text-sm whitespace-nowrap">
                            <Link href={`/admin/expenses/${e.id}`} className="underline-offset-4 hover:underline">
                              {formatStayDate(e.date)}
                            </Link>
                          </TableCell>
                          <TableCell className="text-sm">{e.categoryName}</TableCell>
                          <TableCell className="max-w-72 min-w-48 text-sm whitespace-normal">
                            <span className={e.isVoided ? "line-through" : undefined}>{e.description}</span>
                            {e.supplier && <span className="block text-xs text-muted-foreground">{e.supplier}</span>}
                            {e.isVoided && <span className="block text-xs">{t("voidedNote", { reason: e.voidReason ?? "" })}</span>}
                          </TableCell>
                          <TableCell className="text-sm">{paymentMethodLabel[e.method]}</TableCell>
                          <TableCell className="text-sm text-muted-foreground">{e.createdByName ?? "—"}</TableCell>
                          <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(e.amount)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
              {result.data.page.totalPages > 1 && (
                <nav className="flex items-center gap-3 text-sm" aria-label={t("expensesTitle")}>
                  {page > 1 && <Link href={link(page - 1)}>{t("prev")}</Link>}
                  <span>{t("pageOf", { page, pages: result.data.page.totalPages })}</span>
                  {page < result.data.page.totalPages && <Link href={link(page + 1)}>{t("next")}</Link>}
                </nav>
              )}
            </>
          )}
        </div>

        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("addExpense")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ExpenseForm categories={cats} today={today} />
            </CardContent>
          </Card>
          {result.ok && result.data.canManage && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("categories")}</h2>
                </CardTitle>
                <CardDescription>{t("categoriesHint")}</CardDescription>
              </CardHeader>
              <CardContent>
                <CategoryManager categories={cats} />
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
