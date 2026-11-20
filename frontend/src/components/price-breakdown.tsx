import { getTranslations } from "next-intl/server";
import type { PriceQuote } from "@/lib/api/types";
import { formatMoney, formatStayDate } from "@/lib/format";

/** Расшифровка стоимости, рассчитанной backend: цена каждой ночи, скидка, услуги, итог. */
export async function PriceBreakdown({ quote }: { quote: PriceQuote }) {
  const t = await getTranslations("Bookings");
  return (
    <div className="grid gap-3 text-sm">
      <table className="w-full">
        <caption className="sr-only">{t("price")}</caption>
        <thead>
          <tr className="text-left text-xs text-muted-foreground">
            <th className="pb-1 font-normal">{t("nightsCol")}</th>
            <th className="pb-1 font-normal">{t("source")}</th>
            <th className="pb-1 text-right font-normal">{t("colTotal")}</th>
          </tr>
        </thead>
        <tbody>
          {quote.nights.map((n) => (
            <tr key={n.date} className="border-t border-border/60">
              <td className="tabular py-1.5">{formatStayDate(n.date)}</td>
              <td className="py-1.5 text-muted-foreground">{n.source}</td>
              <td className="tabular py-1.5 text-right">{formatMoney(n.price)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <dl className="grid gap-1 border-t pt-3">
        <div className="flex justify-between gap-4">
          <dt>{t("accommodation")}</dt>
          <dd className="tabular">{formatMoney(quote.accommodationTotal)}</dd>
        </div>
        {quote.discountAmount > 0 && (
          <div className="flex justify-between gap-4 text-status-available">
            <dt>
              {t("discount")}
              {quote.discountName ? ` · ${quote.discountName}` : ""}
            </dt>
            <dd className="tabular">−{formatMoney(quote.discountAmount)}</dd>
          </div>
        )}
        {quote.services.map((s) => (
          <div key={s.serviceId} className="flex justify-between gap-4 text-muted-foreground">
            <dt>
              {s.name} × {s.quantity}
              {s.perNight ? ` · ${t("perNight")}` : ""}
            </dt>
            <dd className="tabular">{formatMoney(s.total)}</dd>
          </div>
        ))}
        <div className="mt-1 flex justify-between gap-4 border-t pt-2 font-heading text-lg">
          <dt>{t("total")}</dt>
          <dd className="tabular">{formatMoney(quote.total)}</dd>
        </div>
      </dl>
    </div>
  );
}
