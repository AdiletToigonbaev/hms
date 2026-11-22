import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState, LoadingState } from "@/components/states/states";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { RolePermissions } from "@/lib/api/types";
import { RolePermissionsEditor } from "./role-editor";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("Staff");
  return { title: t("rolesTitle") };
}

/** Права ролей «Администратор» и «Уборщик» (владелец — всегда все права). */
export default async function RolesPage() {
  const t = await getTranslations("Staff");
  return (
    <section className="grid gap-6">
      <Link href="/owner/staff" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden />
        {t("title")}
      </Link>
      <PageHeader title={t("rolesTitle")} description={t("rolesDescription")} />
      <Suspense fallback={<LoadingState rows={8} />}>
        <Roles />
      </Suspense>
    </section>
  );
}

async function Roles() {
  const t = await getTranslations("Staff");
  const [admin, housekeeper] = await Promise.all([api<RolePermissions>("/roles/Administrator/settings"), api<RolePermissions>("/roles/Housekeeper/settings")]);
  if (!admin.ok || !housekeeper.ok) return <ErrorState title={t("rolesLoadError")} />;
  return (
    <div className="grid gap-6">
      {[admin.data, housekeeper.data].map((r) => (
        <Card key={r.role} id={r.role} className="scroll-mt-20">
          <CardHeader>
            <CardTitle>
              <h2 className="text-xl">{r.displayName}</h2>
            </CardTitle>
            <CardDescription>
              {t("roleAppliesTo", { count: r.staffCount })} {r.role === "Housekeeper" ? t("roleHousekeeperNote") : t("roleAdminNote")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <RolePermissionsEditor settings={r} />
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
