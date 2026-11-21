import { Plus, UserRound } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { LiveSearchInput } from "@/components/forms/live-search-input";
import { PageHeader } from "@/components/layout/page-header";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { GuestListItem, Paged } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatHotelDateTime, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Guests"))("title") };
}

const filters = ["", "withBookings", "whatsApp", "noContact"] as const;
const filterKey = { "": "filterAll", withBookings: "filterWithBookings", whatsApp: "filterWhatsApp", noContact: "filterNoContact" } as const;

export default async function GuestsPage({ searchParams }: PageProps<"/admin/guests">) {
  const params = await searchParams;
  const search = typeof params.search === "string" ? params.search.slice(0, 100) : "";
  const filter = (filters as readonly string[]).includes(String(params.filter ?? "")) ? (String(params.filter ?? "") as (typeof filters)[number]) : "";
  const page = Math.max(1, Number(params.page) || 1);

  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  if (search) query.set("search", search);
  if (filter) query.set("filter", filter);

  const [t, errors, user, result] = await Promise.all([
    getTranslations("Guests"),
    getTranslations("Errors"),
    getCurrentUser(),
    api<Paged<GuestListItem>>(`/guests?${query}`),
  ]);
  const canManage = user?.permissions.includes("guest.manage") ?? false;
  const link = (next: Record<string, string>) => {
    const q = new URLSearchParams({ ...(search ? { search } : {}), ...(filter ? { filter } : {}), ...next });
    for (const [k, v] of [...q.entries()]) if (!v) q.delete(k);
    return `/admin/guests?${q}`;
  };

  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          canManage && (
            <Link href="/admin/guests/new" className={buttonVariants()}>
              <Plus aria-hidden />
              {t("add")}
            </Link>
          )
        }
      />

      <div className="grid gap-3">
        <form method="get" className="flex flex-wrap gap-2" role="search">
          {filter && <input type="hidden" name="filter" value={filter} />}
          <LiveSearchInput defaultValue={search} placeholder={t("searchPlaceholder")} label={t("search")} className="w-full sm:max-w-sm" />
        </form>
        <nav aria-label={t("search")} className="flex flex-wrap gap-1.5">
          {filters.map((f) => (
            <Link
              key={f || "all"}
              href={link({ filter: f, page: "" })}
              aria-current={f === filter ? "page" : undefined}
              className={cn(
                "rounded-full border px-3 py-1 text-sm font-medium transition",
                f === filter ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
              )}
            >
              {t(filterKey[f])}
            </Link>
          ))}
        </nav>
      </div>

      {!result.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : result.data.items.length === 0 ? (
        search || filter ? (
          <EmptyState icon={UserRound} title={t("notFound")} description={t("notFoundHint")} />
        ) : (
          <EmptyState
            icon={UserRound}
            title={t("empty")}
            description={t("emptyHint")}
            action={
              canManage && (
                <Link href="/admin/guests/new" className={buttonVariants()}>
                  {t("add")}
                </Link>
              )
            }
          />
        )
      ) : (
        <>
          <div className="overflow-x-auto rounded-2xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("colGuest")}</TableHead>
                  <TableHead>{t("colContacts")}</TableHead>
                  <TableHead className="text-right">{t("colBookings")}</TableHead>
                  <TableHead className="text-right">{t("colAmount")}</TableHead>
                  <TableHead>{t("colLastStay")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.data.items.map((g) => (
                  <TableRow key={g.id}>
                    <TableCell>
                      <Link href={`/admin/guests/${g.id}`} className="font-medium underline-offset-4 hover:underline">
                        {g.fullName}
                      </Link>
                      <span className="mt-1 flex flex-wrap gap-1">
                        {g.hasAccount && <Badge variant="outline">{t("account")}</Badge>}
                        {g.whatsAppConsent && <Badge variant="secondary">{t("whatsApp")}</Badge>}
                      </span>
                    </TableCell>
                    <TableCell className="text-sm">
                      {g.phone && <span className="tabular block">{g.phone}</span>}
                      {g.email && <span className="block text-muted-foreground">{g.email}</span>}
                    </TableCell>
                    <TableCell className="tabular text-right">{g.stats.bookingsCount}</TableCell>
                    <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(g.stats.bookedAmount)}</TableCell>
                    <TableCell className="text-sm whitespace-nowrap text-muted-foreground">
                      {g.stats.lastStayAt ? formatHotelDateTime(g.stats.lastStayAt) : t("never")}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {result.data.totalPages > 1 && (
            <nav className="flex items-center gap-3 text-sm" aria-label={t("title")}>
              {page > 1 && <Link href={link({ page: String(page - 1) })}>{t("prev")}</Link>}
              <span>{t("pageOf", { page, pages: result.data.totalPages })}</span>
              {page < result.data.totalPages && <Link href={link({ page: String(page + 1) })}>{t("next")}</Link>}
            </nav>
          )}
        </>
      )}
    </div>
  );
}
