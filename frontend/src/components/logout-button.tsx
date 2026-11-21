"use client";

import { LogOut } from "lucide-react";
import { useTranslations } from "next-intl";
import { useRef, useState } from "react";
import { logoutAction } from "@/app/(auth)/actions";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";

/** Выход из аккаунта — через окно подтверждения, чтобы не выйти случайным нажатием. */
export function LogoutButton() {
  const t = useTranslations("Common");
  const formRef = useRef<HTMLFormElement>(null);
  const [asking, setAsking] = useState(false);
  return (
    <form ref={formRef} action={logoutAction}>
      <Button type="button" variant="ghost" size="icon" className="size-10 md:size-8" aria-label={t("logout")} title={t("logout")} onClick={() => setAsking(true)}>
        <LogOut aria-hidden />
      </Button>
      <ConfirmDialog
        open={asking}
        onOpenChange={setAsking}
        title={t("logoutConfirmTitle")}
        description={t("logoutConfirmText")}
        confirmLabel={t("logoutConfirmYes")}
        cancelLabel={t("logoutConfirmNo")}
        onConfirm={() => formRef.current?.requestSubmit()}
      />
    </form>
  );
}
