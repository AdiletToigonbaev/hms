import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { hotelTimeZone } from "@/i18n/config";
import { api } from "@/lib/api/server";
import type { Expense, ExpenseCategory } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { hotelToday } from "@/lib/bookings/status";
import { paymentMethodLabel } from "@/lib/finance/labels";
import { formatHotelDateTime, formatMoney, formatStayDate } from "@/lib/format";
import { ExpenseForm, VoidExpenseForm } from "../../payments/finance-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Finance"))("expenseTitle") };
}

export default async function ExpensePage({ params }: PageProps<"/admin/expenses/[id]">) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();
  const [t, user, result, categories] = await Promise.all([
    getTranslations("Finance"),
    getCurrentUser(),
    api<Expense>(`/expenses/${id}`),
    api<ExpenseCategory[]>("/expense-categories"),
  ]);
  if (!result.ok) notFound();
  const e = result.data;
  const canManage = user?.permissions.includes("expense.manage") ?? false;

  return (
    <div className="grid max-w-4xl gap-6">
      <div className="grid gap-3">
        <Link href="/admin/expenses" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("expensesTitle")}
        </Link>
        <h1 className="tabular text-3xl">{formatMoney(e.amount)}</h1>
        <p className="text-sm text-muted-foreground">
          {e.categoryName} · {formatStayDate(e.date)} · {paymentMethodLabel[e.method]} · {t("createdBy", { name: e.createdByName ?? "—", at: formatHotelDateTime(e.createdAt) })}
        </p>
        <p>{e.description}</p>
        {e.isVoided && (
          <p className="rounded-xl bg-secondary px-4 py-3 text-sm text-muted-foreground">
            {t("voidedNote", { reason: e.voidReason ?? "" })}
            {e.voidedByName ? ` · ${e.voidedByName}` : ""}
          </p>
        )}
      </div>

      {canManage && !e.isVoided && (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("editExpense")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ExpenseForm categories={categories.ok ? categories.data : []} today={hotelToday(hotelTimeZone)} expense={e} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("voidTitle")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="mb-3 text-sm text-muted-foreground">{t("voidHint")}</p>
              <VoidExpenseForm expense={e} />
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
