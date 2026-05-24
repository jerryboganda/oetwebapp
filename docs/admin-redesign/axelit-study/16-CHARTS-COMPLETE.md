# 16 — CHARTS: Complete spec + May 2026 industry guidelines

**Gap closed**: Chart configurations, palette, tooltip, legend, axis formatting
**Method**: Inspection of Axelit's chart usage (ApexCharts SVG-based, evidence: SVG containers + "Loading Chart..." placeholders on `/dashboard/ecommerce`) + 2026 chart-design synthesis.
**Confidence**: **HIGH** ✅

---

## 1 · What Axelit ships

- **Library**: **ApexCharts** (inferred from SVG structure on `/dashboard/ecommerce` — 10+ SVG chart containers detected)
- **Render strategy**: Client-side, lazy-loaded ("Loading Chart..." text placeholder occupies the slot until JS mounts)
- **Series colors**: pulled from `--primary`, `--success`, `--danger`, `--warning`, `--info` role tokens
- **Chart types observed**: KPI sparklines (mini area), bar charts, donut, line charts, area charts

ApexCharts is jQuery-free (good) but ships ~150KB. For an OET admin already on motion v12 + Recharts, **migrating away from Apex is the right call**.

---

## 2 · MAY 2026 INDUSTRY STANDARD — Chart Design Guidelines

### 2.1 · Library decision matrix

| Need | Best 2026 choice |
| ---- | ---------------- |
| React-native, declarative, lightweight | **Recharts** (already in OET) |
| Bigger feature set, more chart types | **Apache ECharts** via `echarts-for-react` |
| Tiny KPI sparklines | `react-sparklines` or inline SVG |
| Real-time streaming (>10Hz updates) | **uPlot** (canvas, 100k+ points) |
| Time-series analytics | **Apache ECharts** or **Visx** |
| Geospatial maps | **react-leaflet** or **deck.gl** |
| Custom domain charts (e.g. score progressions) | **Visx** (composable d3 primitives) |

**OET recommendation**: **Recharts** for 90% of admin charts (already standardized per memory notes). Reach for ECharts only when Recharts can't render the type.

### 2.2 · The 7 chart types every admin dashboard needs

1. **Line chart** — trend over time (signups/day, revenue/week)
2. **Bar chart** (vertical) — categorical comparison (revenue by plan)
3. **Stacked bar** — composition over time (revenue by source per month)
4. **Donut / pie** — share of total (audience by country) — USE SPARINGLY, donut ≤ 5 segments
5. **Area chart** (filled line) — cumulative trends
6. **Sparkline** (mini line/bar) — KPI tile inline visualization
7. **Heatmap / calendar** — activity density (commits per day, sessions per hour)

Beyond these 7, evaluate per case. Avoid 3D charts (always), bubble charts (rarely useful), gauges (replaced by ProgressBar + status badge).

### 2.3 · Color palette — the 2026 chromatic-set rules

**Sequential** (single hue, value ramp) — for ordered data (heat, low→high):
```
--seq-1: hsl(220 60% 95%);  /* lightest */
--seq-2: hsl(220 60% 80%);
--seq-3: hsl(220 60% 65%);
--seq-4: hsl(220 60% 50%);  /* mid */
--seq-5: hsl(220 60% 35%);
--seq-6: hsl(220 60% 22%);  /* darkest */
```

**Diverging** (two hues from a midpoint) — for +/- data:
```
--div-neg-3: hsl(0 70% 30%);
--div-neg-2: hsl(0 70% 50%);
--div-neg-1: hsl(0 70% 70%);
--div-mid:   hsl(50 5% 90%);
--div-pos-1: hsl(220 70% 70%);
--div-pos-2: hsl(220 70% 50%);
--div-pos-3: hsl(220 70% 30%);
```

**Categorical** (qualitative, max 8 hues) — for series differentiation:
```
--cat-1: hsl(220 70% 50%);  /* primary */
--cat-2: hsl(145 60% 45%);  /* success-green */
--cat-3: hsl(35 90% 55%);   /* amber */
--cat-4: hsl(270 60% 60%);  /* violet */
--cat-5: hsl(195 75% 50%);  /* cyan */
--cat-6: hsl(15 80% 55%);   /* orange-red */
--cat-7: hsl(330 60% 60%);  /* pink */
--cat-8: hsl(180 50% 40%);  /* teal */
```

**OkLab/OkLCH alternative** (perceptually uniform — preferred for 2026):
```
--cat-1: oklch(60% 0.18 250);
--cat-2: oklch(60% 0.18 145);
/* etc. — same lightness ensures equal visual weight */
```

**Critical**: NEVER use raw Bootstrap palette (`--bs-primary`, `--bs-success` etc.) directly in charts. Brand colors are for UI chrome; chart palettes need higher saturation and value uniformity.

### 2.4 · Dark mode chart palette

Re-derive series colors per theme:
```ts
const LIGHT_PALETTE = ['#3b82f6', '#10b981', '#f59e0b', '#8b5cf6', '#06b6d4', '#ef4444', '#ec4899', '#14b8a6'];
const DARK_PALETTE  = ['#60a5fa', '#34d399', '#fbbf24', '#a78bfa', '#22d3ee', '#f87171', '#f472b6', '#2dd4bf'];

const { theme } = useTheme();
const colors = theme === 'dark' ? DARK_PALETTE : LIGHT_PALETTE;
```

Dark mode versions are **lighter and more saturated** to maintain visibility on dark backgrounds.

### 2.5 · Axis design

- **Y axis**: light grey gridlines (10-15% black), no axis line, no tick marks. Labels right-aligned, 12px muted.
- **X axis**: only one axis line, no gridlines (or very sparse), labels rotated max 45° if collision.
- **Number formatting**: K/M/B suffixes (`1.2k`, `4.5M`) — use `Intl.NumberFormat`.
- **Date formatting**: contextual (`Mon`, `Jun 12`, `2025`) — adapt to date range zoom level.

### 2.6 · Legend placement

| Chart type | Legend position |
| ---------- | --------------- |
| Line / area (multi-series) | Top-right (with toggleable series) |
| Bar (single series) | None (chart is self-explanatory) |
| Stacked bar | Top |
| Donut / pie | Right (with values) |
| KPI sparkline | None |
| Heatmap | Below (gradient legend) |

Legend items must be **clickable to toggle** the series. Use Recharts' `onClick` on `Legend` payload.

### 2.7 · Tooltip design

```tsx
<Tooltip
  cursor={{ fill: 'rgba(0,0,0,0.04)' }}  // subtle highlight
  contentStyle={{
    backgroundColor: 'var(--admin-bg-elevated)',
    border: '1px solid var(--admin-border-default)',
    borderRadius: 'var(--admin-radius-md)',
    boxShadow: 'var(--admin-shadow-md)',
    padding: '12px 16px',
    fontSize: '12px',
  }}
  labelStyle={{ color: 'var(--admin-fg-strong)', fontWeight: 600, marginBottom: 4 }}
  itemStyle={{ color: 'var(--admin-fg-default)', padding: '2px 0' }}
/>
```

Tooltip should:
- Appear instantly on hover (no delay — different from button tooltips)
- Show ALL series values at the X position (not just hovered series)
- Format values consistently with axis labels
- Position above-left if cursor is bottom-right of chart area (avoid clipping)

### 2.8 · Loading + empty + error states

```tsx
{loading && <ChartSkeleton />}                              // skeleton with axis stubs
{!loading && !data?.length && <ChartEmptyState />}          // illustration + "No data yet"
{error && <ChartErrorState onRetry={refetch} />}            // ⚠ + retry button
{!loading && !error && data?.length && <ResponsiveContainer>{...}</ResponsiveContainer>}
```

Skeleton dimensions match the rendered chart's container (no CLS).

### 2.9 · Animation policy

- **Initial mount**: animate in (250-400ms ease-out)
- **Data update**: animate from old → new (300ms ease-in-out)
- **Hover**: instant (no transition on highlight)
- **`prefers-reduced-motion`**: disable all animations

Recharts: `<Line isAnimationActive={!prefersReducedMotion} />`

### 2.10 · Accessibility

Charts are notoriously bad for a11y. Mitigations:
- Wrap in `<figure>` with `<figcaption>` summarizing the chart
- Add a hidden `<table>` after the chart with the raw data (`aria-label` + `sr-only` wrapper)
- Use `role="img"` with `aria-label` on the chart SVG
- For interactive charts, expose keyboard nav to series points (`<button>` overlays)
- Color must NOT be the sole differentiator (use pattern, dashed line, or labels)

Recharts has `accessibilityLayer` prop (Recharts 2.10+) that adds keyboard navigation automatically.

### 2.11 · Responsive scaling

Use `ResponsiveContainer`:
```tsx
<ResponsiveContainer width="100%" height={300} minHeight={200}>
  <LineChart data={data}>...</LineChart>
</ResponsiveContainer>
```

For small viewports (< 640px):
- Reduce padding
- Hide axis labels (use tooltip only)
- Convert grouped bars to stacked or single-series
- Tighten font sizes

### 2.12 · Sparklines in KPI tiles

Tiny inline charts (60×30px or 80×40px) showing the trend behind the number:

```tsx
<KpiTile
  label="Active Users"
  value={1247}
  trend="+8.4%"
  trendDirection="up"
  sparkline={<Sparkline data={last30Days} stroke="var(--admin-success)" fill="rgba(34, 197, 94, 0.12)" />}
/>
```

Sparkline rules:
- No axes
- No tooltips
- Single-color fill at 12% alpha
- Stroke matches trend direction (green up, red down, grey flat)

### 2.13 · The "chart-card" pattern

Wrap every chart in a Panel with:
- Title (H5, 18px)
- Optional subtitle (small, muted)
- Optional time-range selector (Day / Week / Month / Year tabs or dropdown)
- Optional kebab menu (Export / Refresh / Hide / Configure)
- Chart container with min-height
- Optional footer (legend if not inline, "View detailed report" link)

### 2.14 · Performance budget

- Time to first paint of chart: < 200ms after data ready
- Animation FPS: ≥ 60fps (use `transform`/`opacity` only)
- For > 1000 data points: virtualize via canvas (uPlot) or pre-aggregate server-side
- Avoid re-rendering on theme switch — pass colors as props (React reconciliation handles the diff)

### 2.15 · Real-time updates

For live-updating dashboards (job queue depth, active users):
- Update interval ≥ 1s (faster wastes cycles)
- Use server-sent events (SSE) or WebSocket, not polling
- Show "Live" indicator (pulsing green dot)
- Animate value changes (number counts up via `framer-motion` or react-spring)

---

## 3 · OET CHART COMPONENT API (production-ready)

```tsx
// components/admin/charts/chart-card.tsx
type ChartCardProps = {
  title: string;
  subtitle?: string;
  timeRanges?: Array<'day' | 'week' | 'month' | 'year'>;
  defaultRange?: 'day' | 'week' | 'month' | 'year';
  onRangeChange?: (range: string) => void;
  actions?: React.ReactNode;  // dropdown menu
  loading?: boolean;
  error?: Error;
  empty?: boolean;
  onRetry?: () => void;
  children: React.ReactNode;   // the Recharts component
};

// components/admin/charts/line-chart.tsx
type LineChartProps<T> = {
  data: T[];
  series: Array<{ key: keyof T; label: string; color?: string }>;
  xAxisKey: keyof T;
  height?: number;
  legendPosition?: 'top' | 'bottom' | 'right' | 'none';
  yAxisFormatter?: (value: number) => string;
  tooltipFormatter?: (value: number, name: string) => string;
};

// components/admin/charts/bar-chart.tsx — similar
// components/admin/charts/donut-chart.tsx
// components/admin/charts/sparkline.tsx
// components/admin/charts/area-chart.tsx
// components/admin/charts/heatmap.tsx (custom — Recharts doesn't have one)

// components/admin/charts/use-chart-palette.tsx
export function useChartPalette(type: 'sequential' | 'diverging' | 'categorical') {
  const { theme } = useTheme();
  return theme === 'dark' ? DARK[type] : LIGHT[type];
}
```

## 4 · Migration from ApexCharts → Recharts (Axelit → OET)

| Apex feature | Recharts equivalent |
| ------------ | ------------------- |
| `Apex.Chart` | `LineChart`, `BarChart`, `PieChart`, etc. |
| `series: [{ name, data }]` | `<Line dataKey="value" name="…" />` |
| `xaxis: { categories }` | `<XAxis dataKey="x" />` |
| `tooltip: { custom }` | `<Tooltip content={CustomTooltip} />` |
| `legend: { position }` | `<Legend verticalAlign="top" />` |
| `plotOptions.bar.distributed` | Map data to assign colors per cell |
| `dataLabels` | Custom Label component or BarLabel |
| `annotations` | `<ReferenceLine />`, `<ReferenceArea />` |
| `responsive` | `<ResponsiveContainer />` |

## 5 · QA checklist

- [ ] Every chart wrapped in ChartCard
- [ ] Loading / empty / error states implemented
- [ ] Tooltip styled to match design system
- [ ] Palette comes from useChartPalette hook (theme-aware)
- [ ] Animations respect `prefers-reduced-motion`
- [ ] Accessibility: figure + figcaption + hidden data table
- [ ] Responsive at 320 / 768 / 1024 / 1920 viewports
- [ ] Sparklines under 80×40px have no axes/tooltips
- [ ] Real-time charts cap at 1s update interval
- [ ] No 3D charts. Ever.

**Confidence upgrade**: LOW → **HIGH** ✅
