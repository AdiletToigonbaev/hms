import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { ChangePasswordForm } from "@/app/(auth)/change-password/change-form";
import { PageHeader } from "@/components/layout/page-header";
import { LoadingState } from "@/components/states/states";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { PortalConsents } from "@/lib/api/types";
import { requireUser } from "@/lib/auth/session";
import { ConsentForm, DeleteAccountForm, ProfileForm } from "../account-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Account"))("profileTitle") };
}

/** Профиль гостя: имя и телефон (попадают в карточку гостя), согласия, пароль, удаление аккаунта. */
export default function ProfilePage() {
  return (
    <Suspense fallback={<LoadingState />}>
      <Profile />
    </Suspense>
  );
}

async function Profile() {
  const [t, me, consents] = await Promise.all([getTranslations("Account"), requireUser(undefined, "/account/profile"), api<PortalConsents>("/me/consents")]);
  const isGuest = me.roles.includes("Guest");
  return (
    <div className="grid gap-6">
      <PageHeader title={t("profileTitle")} />
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-xl">{t("contacts")}</h2>
          </CardTitle>
          <CardDescription>{t("contactsHint")}</CardDescription>
        </CardHeader>
        <CardContent>
          <ProfileForm me={me} />
        </CardContent>
      </Card>
      {consents.ok && (
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-xl">{t("consents")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ConsentForm consents={consents.data} />
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-xl">{t("passwordTitle")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent className="max-w-md">
          <ChangePasswordForm />
        </CardContent>
      </Card>
      {isGuest && (
        <Card className="border-destructive/30">
          <CardHeader>
            <CardTitle>
              <h2 className="text-xl">{t("deleteTitle")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <DeleteAccountForm />
          </CardContent>
        </Card>
      )}
    </div>
  );
}
