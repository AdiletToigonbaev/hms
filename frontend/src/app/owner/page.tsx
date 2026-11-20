import { getTranslations } from "next-intl/server";

export default async function OwnerHomePage() {
  const t = await getTranslations("Areas");
  return (
    <section className="grid gap-3">
      <h1 className="text-2xl font-semibold tracking-tight">{t("ownerTitle")}</h1>
      <p className="text-muted-foreground">{t("ownerText")}</p>
    </section>
  );
}
