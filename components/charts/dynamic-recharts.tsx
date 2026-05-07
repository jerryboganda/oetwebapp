'use client';

/**
 * Dynamic-imported recharts re-export.
 *
 * Recharts ships ~120kB of client JS. Re-exporting via `next/dynamic` keeps it
 * out of the parent page bundle and loads it as a separate chunk after first
 * paint. SSR is disabled because recharts depends on browser APIs.
 *
 * In tests, recharts is webpack-aliased to `tests/mocks/recharts.tsx`; the
 * dynamic wrapper still resolves to that mock through the alias.
 */

import dynamic from 'next/dynamic';

export const ResponsiveContainer = dynamic(
  () => import('recharts').then((m) => m.ResponsiveContainer),
  { ssr: false },
);
export const LineChart = dynamic(
  () => import('recharts').then((m) => m.LineChart),
  { ssr: false },
);
export const BarChart = dynamic(
  () => import('recharts').then((m) => m.BarChart),
  { ssr: false },
);
export const AreaChart = dynamic(
  () => import('recharts').then((m) => m.AreaChart),
  { ssr: false },
);
export const Line = dynamic(
  () => import('recharts').then((m) => m.Line),
  { ssr: false },
);
export const Bar = dynamic(
  () => import('recharts').then((m) => m.Bar),
  { ssr: false },
);
export const Area = dynamic(
  () => import('recharts').then((m) => m.Area),
  { ssr: false },
);
export const CartesianGrid = dynamic(
  () => import('recharts').then((m) => m.CartesianGrid),
  { ssr: false },
);
export const Tooltip = dynamic(
  () => import('recharts').then((m) => m.Tooltip),
  { ssr: false },
);
export const Legend = dynamic(
  () => import('recharts').then((m) => m.Legend),
  { ssr: false },
);
export const XAxis = dynamic(
  () => import('recharts').then((m) => m.XAxis),
  { ssr: false },
);
export const YAxis = dynamic(
  () => import('recharts').then((m) => m.YAxis),
  { ssr: false },
);
export const PieChart = dynamic(
  () => import('recharts').then((m) => m.PieChart),
  { ssr: false },
);
export const Pie = dynamic(
  () => import('recharts').then((m) => m.Pie),
  { ssr: false },
);
export const Cell = dynamic(
  () => import('recharts').then((m) => m.Cell),
  { ssr: false },
);
