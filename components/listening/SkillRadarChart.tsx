'use client';

/**
 * SkillRadarChart — Listening L1..L8 sub-skill radar (§6.4, §27).
 *
 * Consumed by:
 *   - app/listening/results — diagnostic results page.
 *   - app/listening/dashboard — pathway dashboard skill snapshot.
 *
 * Mirrors components/reading/SkillRadarChart.tsx: lazy-loads the recharts
 * surface via `next/dynamic` with `ssr: false` to avoid pulling recharts into
 * the server bundle. The data shape is `SkillScore[]` from
 * `@/lib/listening-pathway-api`, capturing diagnostic vs current values.
 */

import dynamic from 'next/dynamic';
import type { SkillScore } from '@/lib/listening-pathway-api';

const SkillRadarChartInner = dynamic(
  () => import('./SkillRadarChartInner').then((m) => m.SkillRadarChartInner),
  {
    ssr: false,
    loading: () => (
      <div className="flex h-64 items-center justify-center text-sm text-muted">
        Loading chart...
      </div>
    ),
  },
);

export interface SkillRadarChartProps {
  scores: SkillScore[];
}

export function SkillRadarChart({ scores }: SkillRadarChartProps) {
  return <SkillRadarChartInner scores={scores} />;
}

export default SkillRadarChart;
