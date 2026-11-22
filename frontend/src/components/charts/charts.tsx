"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Графики дашборда без сторонних библиотек (docs: dataviz-метод): тонкие метки, 2px линии, 4px скругление концов столбцов,
 * сетка — волосяная линия, одна ось, легенда при 2+ рядах, подсказка при наведении и с клавиатуры, табличное представление.
 * Цвета рядов — токены темы `--chart-1`, `--chart-2` (проверены валидатором палитры), текст — только цвета текста.
 */

export interface Series {
  key: string;
  label: string;
  /** CSS-цвет ряда (var(--chart-1)). */
  color: string;
  values: number[];
}

function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    if (!ref.current) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry.contentRect.width)));
    observer.observe(ref.current);
    return () => observer.disconnect();
  }, []);
  return [ref, width] as const;
}

/** «Красивый» шаг шкалы: 1, 2, 5 × 10^n. */
export function niceTicks(max: number, count = 4): number[] {
  if (max <= 0) return [0];
  const raw = max / count;
  const power = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map((m) => m * power).find((s) => s >= raw) ?? raw;
  const ticks: number[] = [];
  for (let v = 0; v <= max + step * 0.001; v += step) ticks.push(v);
  if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
  return ticks;
}

/** Подпись оси: до 10 000 — целиком, дальше — компактно (10 тыс.). */
export function axisLabel(v: number, percent = false): string {
  if (percent) return `${Math.round(v * 100)}%`;
  if (v >= 1_000_000) return `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 1 }).format(v / 1_000_000)} млн`;
  if (v >= 10_000) return `${Math.round(v / 1000)} тыс.`;
  return new Intl.NumberFormat("ru-RU").format(v);
}

function Legend({ series, kind }: { series: Series[]; kind: "line" | "bar" }) {
  if (series.length < 2) return null;
  return (
    <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
      {series.map((s) => (
        <li key={s.key} className="inline-flex items-center gap-1.5">
          {kind === "line" ? (
            <span aria-hidden className="inline-block h-0.5 w-4 rounded-full" style={{ background: s.color }} />
          ) : (
            <span aria-hidden className="inline-block size-2.5 rounded-sm" style={{ background: s.color }} />
          )}
          {s.label}
        </li>
      ))}
    </ul>
  );
}

/** Таблица с данными графика — для чтения с экрана и тех, кто не различает цвета. */
function DataTable({ labels, series, format, caption }: { labels: string[]; series: Series[]; format: (v: number) => string; caption: string }) {
  return (
    <div className="max-h-72 overflow-auto rounded-lg border">
      <table className="w-full text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="sticky top-0 bg-secondary">
          <tr>
            <th scope="col" className="px-3 py-1.5 text-left font-medium">
              Дата
            </th>
            {series.map((s) => (
              <th key={s.key} scope="col" className="px-3 py-1.5 text-right font-medium">
                {s.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {labels.map((label, i) => (
            <tr key={label} className="border-t">
              <th scope="row" className="px-3 py-1 text-left font-normal">
                {label}
              </th>
              {series.map((s) => (
                <td key={s.key} className="tabular px-3 py-1 text-right">
                  {format(s.values[i] ?? 0)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Карточка графика: заголовок, переключатель «График / Таблица», легенда. */
export function ChartCard({ title, subtitle, children, table }: { title: string; subtitle?: string; children: ReactNode; table?: ReactNode }) {
  const [asTable, setAsTable] = useState(false);
  return (
    <section className="grid min-w-0 content-start gap-3 rounded-2xl border bg-card p-4" aria-label={title}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="font-heading text-base">{title}</h3>
          {subtitle && <p className="text-xs text-muted-foreground">{subtitle}</p>}
        </div>
        {table && (
          <button type="button" onClick={() => setAsTable((v) => !v)} className="rounded-lg px-2 py-1 text-xs text-muted-foreground hover:bg-secondary hover:text-foreground" aria-pressed={asTable}>
            {asTable ? "График" : "Таблица"}
          </button>
        )}
      </div>
      {asTable && table ? table : children}
    </section>
  );
}

const PAD = { top: 12, right: 12, bottom: 24, left: 56 };

/** Линии по дням (1–2 ряда одной величины): перекрестие привязывается к ближайшему дню, подсказка — все ряды. */
export function LineChart({ labels, ticksLabels, series, format, caption, height = 220 }: {
  labels: string[];
  /** Подписи оси X (короткие даты). */
  ticksLabels: string[];
  series: Series[];
  format: (v: number) => string;
  caption: string;
  height?: number;
}) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [active, setActive] = useState<number | null>(null);
  const max = Math.max(0, ...series.flatMap((s) => s.values));
  const ticks = niceTicks(max);
  const top = ticks[ticks.length - 1] || 1;
  const w = Math.max(width, 200);
  const plotW = w - PAD.left - PAD.right;
  const plotH = height - PAD.top - PAD.bottom;
  const n = labels.length;
  const x = (i: number) => PAD.left + (n <= 1 ? plotW / 2 : (i / (n - 1)) * plotW);
  const y = (v: number) => PAD.top + plotH - (v / top) * plotH;
  const every = Math.max(1, Math.ceil(n / Math.max(2, Math.floor(plotW / 64))));

  const pick = (clientX: number, rect: DOMRect) => {
    const rel = clientX - rect.left - PAD.left;
    const i = n <= 1 ? 0 : Math.round((rel / plotW) * (n - 1));
    setActive(Math.min(n - 1, Math.max(0, i)));
  };

  return (
    <ChartCard title={caption} table={<DataTable labels={labels} series={series} format={format} caption={caption} />}>
      <Legend series={series} kind="line" />
      <div ref={ref} className="relative">
        {width > 0 && (
          <svg
            width={w}
            height={height}
            role="img"
            aria-label={caption}
            tabIndex={0}
            className="block touch-pan-y outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
            onPointerMove={(e) => pick(e.clientX, e.currentTarget.getBoundingClientRect())}
            onPointerLeave={() => setActive(null)}
            onKeyDown={(e) => {
              if (e.key === "ArrowRight") setActive((a) => Math.min(n - 1, (a ?? -1) + 1));
              if (e.key === "ArrowLeft") setActive((a) => Math.max(0, (a ?? n) - 1));
              if (e.key === "Escape") setActive(null);
            }}
            onBlur={() => setActive(null)}
          >
            {ticks.map((t) => (
              <g key={t}>
                <line x1={PAD.left} x2={w - PAD.right} y1={y(t)} y2={y(t)} stroke="var(--chart-grid)" strokeWidth={1} />
                <text x={PAD.left - 8} y={y(t)} dy="0.32em" textAnchor="end" className="fill-muted-foreground text-[11px]">
                  {axisLabel(t)}
                </text>
              </g>
            ))}
            {ticksLabels.map((label, i) =>
              i % every === 0 ? (
                <text key={i} x={x(i)} y={height - 6} textAnchor="middle" className="fill-muted-foreground text-[11px]">
                  {label}
                </text>
              ) : null,
            )}
            {active !== null && <line x1={x(active)} x2={x(active)} y1={PAD.top} y2={PAD.top + plotH} stroke="var(--muted-foreground)" strokeWidth={1} opacity={0.5} />}
            {series.map((s) => (
              <g key={s.key}>
                <polyline
                  fill="none"
                  stroke={s.color}
                  strokeWidth={2}
                  strokeLinejoin="round"
                  strokeLinecap="round"
                  points={s.values.map((v, i) => `${x(i)},${y(v)}`).join(" ")}
                />
                {active !== null && <circle cx={x(active)} cy={y(s.values[active] ?? 0)} r={4} fill={s.color} stroke="var(--card)" strokeWidth={2} />}
              </g>
            ))}
          </svg>
        )}
        {active !== null && width > 0 && (
          <div
            role="status"
            className="pointer-events-none absolute top-1 z-10 grid min-w-36 gap-1 rounded-lg border bg-popover px-3 py-2 text-xs shadow-md"
            style={x(active) > w / 2 ? { right: w - x(active) + 12 } : { left: x(active) + 12 }}
          >
            <span className="text-muted-foreground">{labels[active]}</span>
            {series.map((s) => (
              <span key={s.key} className="flex items-center justify-between gap-3">
                <span className="inline-flex items-center gap-1.5 text-muted-foreground">
                  <span aria-hidden className="inline-block h-0.5 w-3 rounded-full" style={{ background: s.color }} />
                  {s.label}
                </span>
                <span className="tabular font-semibold text-foreground">{format(s.values[active] ?? 0)}</span>
              </span>
            ))}
          </div>
        )}
      </div>
    </ChartCard>
  );
}

/** Столбцы по дням (один ряд): подсказка на столбце, столбец не толще 24px, скругление 4px у конца. */
export function ColumnChart({ labels, ticksLabels, values, color, format, caption, percent, height = 200 }: {
  labels: string[];
  ticksLabels: string[];
  values: number[];
  color: string;
  format: (v: number) => string;
  caption: string;
  percent?: boolean;
  height?: number;
}) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [active, setActive] = useState<number | null>(null);
  const max = Math.max(0, ...values);
  // Проценты: шаг 25 %, верх — ближайшая четверть над максимумом (не выше 100 %).
  const ticks = percent ? [0, 0.25, 0.5, 0.75, 1].filter((t) => t <= Math.min(1, Math.max(0.25, Math.ceil(max * 4) / 4))) : niceTicks(max);
  const top = ticks[ticks.length - 1] || 1;
  const w = Math.max(width, 200);
  const plotW = w - PAD.left - PAD.right;
  const plotH = height - PAD.top - PAD.bottom;
  const n = values.length;
  const band = plotW / Math.max(n, 1);
  const bar = Math.max(2, Math.min(24, band - 2));
  const y = (v: number) => PAD.top + plotH - (Math.min(v, top) / top) * plotH;
  const every = Math.max(1, Math.ceil(n / Math.max(2, Math.floor(plotW / 64))));
  const series: Series[] = [{ key: "v", label: caption, color, values }];

  return (
    <ChartCard title={caption} table={<DataTable labels={labels} series={series} format={format} caption={caption} />}>
      <div ref={ref} className="relative">
        {width > 0 && (
          <svg width={w} height={height} role="img" aria-label={caption} className="block" onPointerLeave={() => setActive(null)}>
            {ticks.map((t) => (
              <g key={t}>
                <line x1={PAD.left} x2={w - PAD.right} y1={y(t)} y2={y(t)} stroke="var(--chart-grid)" strokeWidth={1} />
                <text x={PAD.left - 8} y={y(t)} dy="0.32em" textAnchor="end" className="fill-muted-foreground text-[11px]">
                  {axisLabel(t, percent)}
                </text>
              </g>
            ))}
            {values.map((v, i) => {
              const cx = PAD.left + band * i + band / 2;
              const h = Math.max(0, PAD.top + plotH - y(v));
              const r = Math.min(4, h, bar / 2);
              const x0 = cx - bar / 2;
              const yTop = PAD.top + plotH - h;
              // Скругление только у конца данных, у базовой линии — прямой угол.
              const d = h <= 0 ? "" : `M${x0},${PAD.top + plotH} V${yTop + r} Q${x0},${yTop} ${x0 + r},${yTop} H${x0 + bar - r} Q${x0 + bar},${yTop} ${x0 + bar},${yTop + r} V${PAD.top + plotH} Z`;
              return (
                <g key={i}>
                  {d && <path d={d} fill={color} opacity={active === null || active === i ? 1 : 0.55} />}
                  {/* Цель наведения — вся полоса дня, больше самого столбца. */}
                  <rect
                    x={PAD.left + band * i}
                    y={PAD.top}
                    width={band}
                    height={plotH}
                    fill="transparent"
                    tabIndex={0}
                    aria-label={`${labels[i]}: ${format(v)}`}
                    onPointerEnter={() => setActive(i)}
                    onFocus={() => setActive(i)}
                    onBlur={() => setActive(null)}
                    className="outline-none"
                  />
                  {i % every === 0 && (
                    <text x={cx} y={height - 6} textAnchor="middle" className="fill-muted-foreground text-[11px]">
                      {ticksLabels[i]}
                    </text>
                  )}
                </g>
              );
            })}
          </svg>
        )}
        {active !== null && width > 0 && (
          <div
            role="status"
            className="pointer-events-none absolute top-1 z-10 grid gap-0.5 rounded-lg border bg-popover px-3 py-2 text-xs shadow-md"
            style={PAD.left + band * active > w / 2 ? { right: w - (PAD.left + band * active) + 4 } : { left: PAD.left + band * (active + 1) + 4 }}
          >
            <span className="tabular text-sm font-semibold">{format(values[active])}</span>
            <span className="text-muted-foreground">{labels[active]}</span>
          </div>
        )}
      </div>
    </ChartCard>
  );
}

/** Горизонтальные полосы «название — значение» (одна величина): значение у конца полосы, отсортировано по убыванию. */
export function BarList({ items, color, format, caption, subtitle, empty }: {
  items: { name: string; value: number }[];
  color: string;
  format: (v: number) => string;
  caption: string;
  subtitle?: string;
  empty: string;
}) {
  const max = Math.max(0, ...items.map((i) => i.value));
  return (
    <ChartCard title={caption} subtitle={subtitle}>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <ul className="grid gap-2">
          {items.map((item) => (
            <li key={item.name} className="grid gap-1" title={`${item.name}: ${format(item.value)}`}>
              <div className="flex items-baseline justify-between gap-3 text-sm">
                <span className="min-w-0 truncate">{item.name}</span>
                <span className="tabular shrink-0 font-medium">{format(item.value)}</span>
              </div>
              <div className="h-2 rounded-r bg-transparent">
                <div className={cn("h-2 rounded-r")} style={{ width: `${max > 0 ? Math.max(1, (item.value / max) * 100) : 0}%`, background: color }} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </ChartCard>
  );
}
