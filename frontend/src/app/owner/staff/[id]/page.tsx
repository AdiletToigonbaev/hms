import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Staff, StaffPermissions } from "@/lib/api/types";
import { PermissionsEditor, ProfileForm, ResetPasswordForm, StatusForm } from "./staff-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Staff"))("profile") };
}

export default async function StaffPage({ params }: PageProps<"/owner/staff/[id]">) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  const [t, common, staff, permissions] = await Promise.all([
    getTranslations("Staff"),
    getTranslations("Common"),
    api<Staff>(`/staff/${id}`),
    api<StaffPermissions>(`/staff/${id}/permissions`),
  ]);
  if (!staff.ok || !permissions.ok || staff.data.role === "Owner") notFound();
  const s = staff.data;

  return (
    <div className="grid gap-6">
      <div className="grid gap-2">
        <Link href="/owner/staff" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {common("back")}
        </Link>
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{s.fullName}</h1>
          <Badge variant="outline">{s.roleDisplayName}</Badge>
          <Badge variant={s.isActive ? "secondary" : "destructive"}>{s.isActive ? t("active") : t("inactive")}</Badge>
          {s.mustChangePassword && <Badge variant="outline">{t("tempPassword")}</Badge>}
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>
              <h2>{t("profile")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ProfileForm staff={s} />
          </CardContent>
        </Card>

        <div className="grid gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2>{s.isActive ? t("deactivate") : t("activate")}</h2>
              </CardTitle>
              {s.isActive && <CardDescription>{t("deactivateHint")}</CardDescription>}
            </CardHeader>
            <CardContent>
              <StatusForm id={s.id} isActive={s.isActive} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>
                <h2>{t("resetPassword")}</h2>
              </CardTitle>
              <CardDescription>{t("resetPasswordHint")}</CardDescription>
            </CardHeader>
            <CardContent>
              <ResetPasswordForm id={s.id} />
            </CardContent>
          </Card>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>
            <h2>{t("permissions")}</h2>
          </CardTitle>
          <CardDescription>{s.role === "Administrator" ? t("permissionsHint") : t("permissionsOnlyAdmin")}</CardDescription>
        </CardHeader>
        {s.role === "Administrator" && (
          <CardContent>
            <PermissionsEditor id={s.id} permissions={permissions.data} />
          </CardContent>
        )}
      </Card>
    </div>
  );
}
