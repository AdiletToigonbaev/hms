import { getTranslations } from "next-intl/server";

export default async function Page() {
  const [t, common] = await Promise.all([getTranslations("Areas"), getTranslations("Common")]);
  return (
    <section className="grid gap-3">
      <h1 className="text-2xl font-semibold tracking-tight">{t("accountTitle")}</h1>
      <p className="text-muted-foreground">{common("inDevelopment")}</p>
    </section>
  );
}
