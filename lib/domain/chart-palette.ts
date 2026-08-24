/**
 * Chart + brand color tokens — FE-009 / FE-039.
 *
 * Keep hex values here for canvas/SVG (recharts) that cannot read CSS
 * custom properties reliably. Tailwind surfaces should use `bg-gold`,
 * `text-gold-fg`, `bg-oet-navy`, `bg-oet-teal` instead of these hexes.
 */

export const chartPalette = {
  gold: '#d4a44f',
  goldDark: '#bf8e3d',
  goldFg: '#996f1f',
  oetNavy: '#0e2841',
  oetTeal: '#156082',
  overall: '#0e2841',
  writing: '#e11d48',
  speaking: '#7c3aed',
  reading: '#2563eb',
  listening: '#156082',
  vocabulary: '#0d9488',
  indigo: '#4f46e5',
} as const;

export type ChartSeriesKey = 'overall' | 'writing' | 'speaking' | 'reading' | 'listening' | 'vocabulary';

export function seriesColor(series: ChartSeriesKey): string {
  return chartPalette[series];
}
