'use client';

import dynamic from 'next/dynamic';
import type { SkillRadarDto } from '@/lib/reading-pathway-api';

// Lazy-load the actual chart to avoid SSR issues with recharts
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

interface SkillRadarChartProps {
  data: SkillRadarDto;
}

export function SkillRadarChart({ data }: SkillRadarChartProps) {
  return <SkillRadarChartInner data={data} />;
}
