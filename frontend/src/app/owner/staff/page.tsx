import { Plus } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/layout/page-header";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { Paged, Staff } from "@/lib/api/types";
import { formatHotelDateTime } from "@/lib/format";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Staff"))("title") };
}

export default async function StaffListPage({ searchParams }: PageProps<"/owner/staff">) {
  const params = await searchParams;
  const search = typeof params.search === "string" ? params.search : "";
  const role = params.role === "Administrator" || params.role === "Housekeeper" ? params.role : "";
  const page = Math.max(1, Number(params.page) || 1);

  const query = new URLSearchParams({ page: String(page), pageSize: "20" });
  if (search) query.set("search", search);
  if (role) query.set("role", role);

  const [t, errors, result] = await Promise.all([
    getTranslations("Staff"),
    getTranslations("Errors"),
    api<Paged<Staff>>(`/staff?${query}`),
  ]);

  const pageLink = (p: number) => {
    const next = new URLSearchParams(query);
    next.set("page", String(p));
    return `/owner/staff?${next}`;
  };

  return (
    <section className="grid gap-4">
      <PageHeader
        title={t("title")}
        actions={
          <Link href="/owner/staff/new" className={buttonVariants()}>
          <Plus aria-hidden />
          {t("add")}
        </Link>
        }
      />

      <form method="get" className="flex flex-wrap gap-2" role="search">
        <Input name="search" defaultValue={search} placeholder={t("search")} aria-label={t("search")} className="max-w-xs" />
        <select
          name="role"
          defaultValue={role}
          aria-label={t("role")}
          className="h-8 rounded-lg border border-input bg-transparent px-2.5 text-sm"
        >
          <option value="">{t("allRoles")}</option>
          <option value="Administrator">{t("roleAdministrator")}</option>
          <option value="Housekeeper">{t("roleHousekeeper")}</option>
        </select>
        <Button type="submit" variant="outline">
          {t("searchSubmit")}
        </Button>
      </form>

      {!result.ok ? (
        <p role="alert" className="text-destructive">
          {errors("unknown")}
        </p>
      ) : result.data.items.length === 0 ? (
        <p className="rounded-lg border border-dashed p-8 text-center text-muted-foreground">{t("empty")}</p>
      ) : (
        <>
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("fullName")}</TableHead>
                  <TableHead>{t("contacts")}</TableHead>
                  <TableHead>{t("role")}</TableHead>
                  <TableHead>{t("status")}</TableHead>
                  <TableHead>{t("lastLogin")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.data.items.map((s) => (
                  <TableRow key={s.id}>
                    <TableCell>
                      {s.role === "Owner" ? (
                        s.fullName
                      ) : (
                        <Link href={`/owner/staff/${s.id}`} className="font-medium underline-offset-4 hover:underline">
                          {s.fullName}
                        </Link>
                      )}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {[s.email, s.phone].filter(Boolean).join(" · ")}
                    </TableCell>
                    <TableCell>{s.roleDisplayName}</TableCell>
                    <TableCell className="space-x-1">
                      <Badge variant={s.isActive ? "secondary" : "destructive"}>{s.isActive ? t("active") : t("inactive")}</Badge>
                      {s.mustChangePassword && <Badge variant="outline">{t("tempPassword")}</Badge>}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {s.lastLoginAt ? formatHotelDateTime(s.lastLoginAt) : t("never")}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {result.data.totalPages > 1 && (
            <nav className="flex items-center gap-3 text-sm" aria-label={t("title")}>
              {page > 1 && <Link href={pageLink(page - 1)}>{t("prev")}</Link>}
              <span>{t("page", { page, total: result.data.totalPages })}</span>
              {page < result.data.totalPages && <Link href={pageLink(page + 1)}>{t("next")}</Link>}
            </nav>
          )}
        </>
      )}
    </section>
  );
}
