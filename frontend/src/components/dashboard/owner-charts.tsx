"use client";

import { BarList, ColumnChart, LineChart } from "@/components/charts/charts";
import type { OwnerDashboard } from "@/lib/api/types";
import { bookingStatusLabel } from "@/lib/bookings/status";
import { paymentMethodLabel } from "@/lib/finance/labels";
import { formatMoney, formatStayDate } from "@/lib/format";
import type { BookingStatus, PaymentMethod } from "@/lib/api/types";

const percent = (v: number) => `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 1 }).format(v * 100)} %`;
const count = (v: number) => new Intl.NumberFormat("ru-RU").format(v);

/** Графики дашборда владельца: деньги по дням, загрузка по дням, разрезы по категориям, способам оплаты, статусам и расходам. */
export function OwnerCharts({ data }: { data: OwnerDashboard }) {
  const labels = data.daily.map((d) => formatStayDate(d.date));
  const short = data.daily.map((d) => `${d.date.slice(8, 10)}.${d.date.slice(5, 7)}`);
  return (
    <div className="grid gap-4">
      <div className="grid gap-4 xl:grid-cols-2">
        <LineChart
          caption="Начислено и поступило по дням"
          labels={labels}
          ticksLabels={short}
          format={formatMoney}
          series={[
            { key: "accrued", label: "Начислено", color: "var(--chart-1)", values: data.daily.map((d) => d.accruedRevenue) },
            { key: "cash", label: "Поступило", color: "var(--chart-2)", values: data.daily.map((d) => d.cashIn) },
          ]}
        />
        <ColumnChart
          caption="Загрузка номеров по дням"
          labels={labels}
          ticksLabels={short}
          values={data.daily.map((d) => d.occupancy)}
          color="var(--chart-1)"
          format={percent}
          percent
        />
      </div>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <BarList
          caption="Начислено по категориям"
          items={data.revenueByRoomType.map((x) => ({ name: x.name, value: x.amount }))}
          color="var(--chart-1)"
          format={formatMoney}
          empty="Нет начислений за период."
        />
        <BarList
          caption="Поступления по способам"
          items={Object.entries(data.cashByMethod)
            .map(([k, v]) => ({ name: paymentMethodLabel[k as PaymentMethod] ?? k, value: v }))
            .sort((a, b) => b.value - a.value)}
          color="var(--chart-2)"
          format={formatMoney}
          empty="Платежей за период нет."
        />
        <BarList
          caption="Брони по статусам"
          subtitle="С началом в периоде"
          items={Object.entries(data.bookingsByStatus)
            .map(([k, v]) => ({ name: bookingStatusLabel[k as BookingStatus] ?? k, value: v }))
            .sort((a, b) => b.value - a.value)}
          color="var(--chart-1)"
          format={count}
          empty="Броней за период нет."
        />
        <BarList
          caption="Расходы по категориям"
          items={data.expensesByCategory.map((x) => ({ name: x.name, value: x.amount }))}
          color="var(--chart-2)"
          format={formatMoney}
          empty="Расходов за период нет."
        />
      </div>
    </div>
  );
}
