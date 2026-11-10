import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'

/**
 * Chart colours. Validated with the dataviz palette checker against the white card surface:
 * lightness band, chroma, CVD separation (ΔE 27.9) and ≥3:1 contrast all pass. Violet is the brand; the aqua
 * step is deliberately NOT the app's success green (status colours are reserved for status).
 */
export const SERIES = { primary: '#5a3cf0', secondary: '#199e70' } as const
const GRID = '#e3e5ee'
const BASELINE = '#c9ccd9'
const AXIS_TEXT = '#626783'

/** 0 / 2,500 / 5,000 …: 1, 2, 2.5 or 5 × a power of ten, so axis ticks are always round. */
function niceStep(max: number, ticks: number) {
  const raw = max / ticks
  const power = 10 ** Math.floor(Math.log10(raw))
  return [1, 2, 2.5, 5, 10].map((m) => m * power).find((s) => s >= raw) ?? 10 * power
}

export function compactMoney(value: number, currency: string) {
  return new Intl.NumberFormat('en-PH', { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 }).format(value)
}

function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setWidth(Math.floor(el.getBoundingClientRect().width))
    measure()
    // Older browsers (and jsdom) have no ResizeObserver: fall back to window resizes instead of crashing.
    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', measure)
      return () => window.removeEventListener('resize', measure)
    }
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])
  return [ref, width] as const
}

export type ChartSeries = { key: string; label: string; color: string; values: number[] }
export type ChartCategory = { key: string; axisLabel: string; title: string }

/** A column with a 4px rounded data end and a square end on the baseline (works for negative values too). */
function columnPath(x: number, w: number, y0: number, y1: number) {
  const h = Math.abs(y1 - y0)
  if (h < 0.5) return ''
  const r = Math.min(4, w / 2, h)
  if (y1 < y0) {
    return `M${x},${y0}V${y1 + r}Q${x},${y1} ${x + r},${y1}H${x + w - r}Q${x + w},${y1} ${x + w},${y1 + r}V${y0}Z`
  }
  return `M${x},${y0}V${y1 - r}Q${x},${y1} ${x + r},${y1}H${x + w - r}Q${x + w},${y1} ${x + w},${y1 - r}V${y0}Z`
}

/**
 * Grouped columns on ONE money axis: right for "how much per day/week", where many periods are zero (a line would
 * draw slopes between days that never happened). Hover or arrow keys highlight a period and show a tooltip; a
 * visually hidden table carries the same numbers for screen readers.
 */
export function ColumnChart({
  categories,
  series,
  format,
  formatAxis,
  height = 240,
  label,
}: {
  categories: ChartCategory[]
  series: ChartSeries[]
  format: (value: number) => string
  formatAxis: (value: number) => string
  height?: number
  label: string
}) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [active, setActive] = useState<number | null>(null)

  const pad = { top: 12, right: 8, bottom: 28, left: 60 }
  const innerW = Math.max(0, width - pad.left - pad.right)
  const innerH = height - pad.top - pad.bottom
  const n = categories.length
  const all = series.flatMap((s) => s.values)
  const max = Math.max(0, ...all)
  const min = Math.min(0, ...all)
  const step = niceStep(Math.max(max, -min, 1), 4)
  const top = Math.max(step, Math.ceil(max / step) * step)
  const bottom = min < 0 ? -Math.ceil(-min / step) * step : 0
  const ticks = Array.from({ length: Math.round((top - bottom) / step) + 1 }, (_, i) => bottom + i * step)
  const y = (v: number) => pad.top + innerH - ((v - bottom) / (top - bottom)) * innerH

  // Band per period; columns ≤ 24px, 2px surface gap between the group's columns, the rest is air.
  const band = n > 0 ? innerW / n : 0
  const gap = 2
  const colW = Math.max(1.5, Math.min(24, (band * 0.72 - gap * (series.length - 1)) / Math.max(1, series.length)))
  const groupW = colW * series.length + gap * (series.length - 1)
  const bandX = (i: number) => pad.left + i * band
  const labelEvery = Math.max(1, Math.ceil(n / Math.max(2, Math.floor(innerW / 64))))

  const pick = (e: PointerEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const i = Math.floor((e.clientX - rect.left - pad.left) / Math.max(1, band))
    setActive(i >= 0 && i < n ? i : null)
  }

  const onKey = (e: KeyboardEvent<SVGSVGElement>) => {
    if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
      e.preventDefault()
      const dir = e.key === 'ArrowRight' ? 1 : -1
      setActive((i) => Math.max(0, Math.min(n - 1, i === null ? (dir > 0 ? 0 : n - 1) : i + dir)))
    } else if (e.key === 'Escape') setActive(null)
  }

  const tooltipW = 196
  const tooltipLeft = active === null ? 0 : Math.min(Math.max(bandX(active) + band / 2 - tooltipW / 2, 0), Math.max(0, width - tooltipW))

  return (
    <div ref={ref} className="relative">
      {series.length > 1 && (
        <ul className="mb-3 flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground" aria-hidden="true">
          {series.map((s) => (
            <li key={s.key} className="inline-flex items-center gap-2">
              <span className="size-2.5 rounded-[3px]" style={{ background: s.color }} />
              {s.label}
            </li>
          ))}
        </ul>
      )}
      {width > 0 && (
        <svg
          width={width}
          height={height}
          role="img"
          aria-label={`${label}. Use the left and right arrow keys to read each period.`}
          tabIndex={0}
          className="block touch-pan-y rounded-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          onPointerMove={pick}
          onPointerDown={pick}
          onPointerLeave={() => setActive(null)}
          onKeyDown={onKey}
          onBlur={() => setActive(null)}
        >
          {ticks.map((t) => (
            <g key={t}>
              <line x1={pad.left} x2={width - pad.right} y1={y(t)} y2={y(t)} stroke={t === 0 ? BASELINE : GRID} strokeWidth={1} />
              <text x={pad.left - 8} y={y(t)} dy="0.32em" textAnchor="end" fontSize={11} fill={AXIS_TEXT} className="tabular">
                {formatAxis(t)}
              </text>
            </g>
          ))}
          {active !== null && <rect x={bandX(active)} y={pad.top} width={band} height={innerH} fill="#5a3cf0" opacity={0.06} pointerEvents="none" />}
          {categories.map((c, i) => {
            const x0 = bandX(i) + (band - groupW) / 2
            return (
              <g key={c.key} opacity={active === null || active === i ? 1 : 0.45}>
                {series.map((s, k) => (
                  <path key={s.key} d={columnPath(x0 + k * (colW + gap), colW, y(0), y(s.values[i] ?? 0))} fill={s.color} />
                ))}
              </g>
            )
          })}
          {categories.map((c, i) =>
            i % labelEvery === 0 ? (
              <text key={c.key} x={bandX(i) + band / 2} y={height - 8} textAnchor="middle" fontSize={11} fill={AXIS_TEXT}>
                {c.axisLabel}
              </text>
            ) : null,
          )}
        </svg>
      )}
      {active !== null && categories[active] && (
        <div
          role="status"
          className="pointer-events-none absolute z-10 rounded-lg border bg-popover px-3 py-2 text-sm shadow-lg"
          style={{ left: tooltipLeft, top: series.length > 1 ? 32 : 0, width: tooltipW }}
        >
          <p className="font-medium">{categories[active].title}</p>
          {series.map((s) => (
            <p key={s.key} className="mt-1 flex items-center justify-between gap-3 text-muted-foreground">
              <span className="inline-flex items-center gap-1.5">
                <span className="size-2 rounded-[2px]" style={{ background: s.color }} />
                {s.label}
              </span>
              <span className="font-medium text-foreground tabular">{format(s.values[active] ?? 0)}</span>
            </p>
          ))}
        </div>
      )}
      <table className="sr-only">
        <caption>{label}</caption>
        <thead>
          <tr>
            <th scope="col">Period</th>
            {series.map((s) => (
              <th key={s.key} scope="col">
                {s.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {categories.map((c, i) => (
            <tr key={c.key}>
              <th scope="row">{c.title}</th>
              {series.map((s) => (
                <td key={s.key}>{format(s.values[i] ?? 0)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * Ranked horizontal bars (one series, so no legend: the card title names it). The value sits at the bar's tip in
 * text ink; the bar carries the colour. 10px thick, rounded data end, square at the baseline.
 */
export function BarList({
  items,
  format,
  color = SERIES.primary,
  empty,
}: {
  items: { key: string; label: string; sub?: string; value: number }[]
  format: (value: number) => string
  color?: string
  empty: string
}) {
  const max = Math.max(0, ...items.map((i) => i.value))
  if (items.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">{empty}</p>
  return (
    <ul className="grid gap-4">
      {items.map((item) => {
        const pct = max > 0 ? Math.max(1, (item.value / max) * 100) : 0
        return (
          <li key={item.key} title={`${item.label}: ${format(item.value)}`}>
            <div className="mb-1.5 flex items-baseline justify-between gap-3 text-sm">
              <span className="min-w-0 truncate">
                <span className="font-medium">{item.label}</span>
                {item.sub && <span className="ml-1.5 text-xs text-muted-foreground">{item.sub}</span>}
              </span>
              <span className="shrink-0 font-semibold tabular">{format(item.value)}</span>
            </div>
            <div className="h-2.5 w-full" aria-hidden="true">
              <div className="h-full rounded-r-[4px]" style={{ width: `${pct}%`, background: color }} />
            </div>
          </li>
        )
      })}
    </ul>
  )
}
