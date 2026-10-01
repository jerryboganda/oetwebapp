'use client';

import dynamic from 'next/dynamic';
import { Skeleton } from '@/components/ui/skeleton';
import type { SkillRadarDto } from '@/lib/reading-pathway-api';

// Lazy-load the actual chart to avoid SSR issues with recharts
const SkillRadarChartInner = dynamic(
  () => import('./SkillRadarChartInner').then((m) => m.SkillRadarChartInner),
  {
    ssr: false,
    // Same footprint as the 300px chart, so nothing jumps when it arrives.
    loading: () => <Skeleton className="h-[300px] w-full rounded-xl" />,
  },
);

interface SkillRadarChartProps {
  data: SkillRadarDto;
}

export function SkillRadarChart({ data }: SkillRadarChartProps) {
  return <SkillRadarChartInner data={data} />;
}
