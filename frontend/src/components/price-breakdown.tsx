import { getTranslations } from "next-intl/server";
import type { PriceQuote } from "@/lib/api/types";
import { formatHotelLocal, formatHotelTime, formatMoney, formatStayDate } from "@/lib/format";

/** Расшифровка стоимости, рассчитанной backend: цена каждой ночи, скидка, услуги, итог. */
/** extras — строки сверх расчёта (продления), total — итог с ними. */
export async function PriceBreakdown({ quote, extras = [], total }: { quote: PriceQuote; extras?: { label: string; amount: number }[]; total?: number }) {
  const t = await getTranslations("Bookings");
  return (
    <div className="grid gap-3 text-sm">
      {quote.hours && quote.hours.length > 0 ? (
        <table className="w-full">
          <caption className="sr-only">{t("price")}</caption>
          <thead>
            <tr className="text-left text-xs text-muted-foreground">
              <th className="pb-1 font-normal">{t("hoursCol")}</th>
              <th className="pb-1 font-normal">{t("source")}</th>
              <th className="pb-1 text-right font-normal">{t("colTotal")}</th>
            </tr>
          </thead>
          <tbody>
            {quote.hours.map((h) => (
              <tr key={h.from} className="border-t border-border/60">
                <td className="tabular py-1.5">
                  {formatHotelLocal(h.from)}–{formatHotelTime(h.to)}
                  <span className="block text-xs text-muted-foreground">
                    {h.hours} ч × {formatMoney(h.unitPrice)}
                  </span>
                </td>
                <td className="py-1.5 text-muted-foreground">{h.source}</td>
                <td className="tabular py-1.5 text-right">{formatMoney(h.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
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
      )}
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
        {extras.map((x) => (
          <div key={x.label} className="flex justify-between gap-4">
            <dt>{x.label}</dt>
            <dd className="tabular">+{formatMoney(x.amount)}</dd>
          </div>
        ))}
        <div className="mt-1 flex justify-between gap-4 border-t pt-2 font-heading text-lg">
          <dt>{t("total")}</dt>
          <dd className="tabular">{formatMoney(total ?? quote.total)}</dd>
        </div>
      </dl>
    </div>
  );
}
