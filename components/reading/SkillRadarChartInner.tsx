'use client';

import {
  PolarAngleAxis,
  PolarGrid,
  Radar,
  RadarChart,
  Legend,
  ResponsiveContainer,
  Tooltip,
} from 'recharts';
import type { SkillRadarDto } from '@/lib/reading-pathway-api';

interface SkillRadarChartInnerProps {
  data: SkillRadarDto;
}

export function SkillRadarChartInner({ data }: SkillRadarChartInnerProps) {
  if (!data.skills.length) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted">
        No skill data available yet.
      </div>
    );
  }

  const chartData = data.skills.map((s) => ({
    skill: s.name,
    Current: s.current,
    Baseline: s.baseline,
    Target: s.target,
  }));

  return (
    <div className="w-full">
      <ResponsiveContainer width="100%" height={300}>
        <RadarChart data={chartData}>
          <PolarGrid />
          <PolarAngleAxis dataKey="skill" tick={{ fontSize: 11 }} />
          <Tooltip />
          <Radar
            name="Baseline"
            dataKey="Baseline"
            stroke="#9ca3af"
            fill="#9ca3af"
            fillOpacity={0.1}
            strokeDasharray="4 4"
          />
          <Radar
            name="Target"
            dataKey="Target"
            stroke="#3b82f6"
            fill="#3b82f6"
            fillOpacity={0.1}
            strokeDasharray="4 4"
          />
          <Radar
            name="Current"
            dataKey="Current"
            stroke="#7c3aed"
            fill="#7c3aed"
            fillOpacity={0.25}
          />
          <Legend wrapperStyle={{ paddingTop: 8, fontSize: 12 }} />
        </RadarChart>
      </ResponsiveContainer>
    </div>
  );
}
