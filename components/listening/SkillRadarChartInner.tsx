'use client';

/**
 * SkillRadarChartInner — recharts-backed L1..L8 radar surface.
 *
 * Loaded only on the client via dynamic import from {@link SkillRadarChart}.
 * Plots two layers per skill:
 *   - "Diagnostic" — baseline at the time of the initial 23-question test.
 *   - "Current"    — rolling score updated by every subsequent attempt.
 *
 * Each axis label falls back from `SkillScore.label` to the skill code (`L1`)
 * when the backend has not supplied a friendly label.
 */

import {
  PolarAngleAxis,
  PolarGrid,
  Radar,
  RadarChart,
  Legend,
  ResponsiveContainer,
  Tooltip,
} from 'recharts';
import type { SkillScore } from '@/lib/listening-pathway-api';

export interface SkillRadarChartInnerProps {
  scores: SkillScore[];
}

interface ChartRow {
  skill: string;
  Current: number;
  Diagnostic: number;
}

export function SkillRadarChartInner({ scores }: SkillRadarChartInnerProps) {
  if (!scores.length) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted">
        No skill data available yet.
      </div>
    );
  }

  const chartData: ChartRow[] = scores.map((s) => ({
    skill: s.label || s.skillCode,
    Current: clamp(s.currentScore),
    Diagnostic: clamp(s.diagnosticScore),
  }));

  return (
    <div className="w-full">
      <ResponsiveContainer width="100%" height={320}>
        <RadarChart data={chartData} outerRadius="75%">
          <PolarGrid />
          <PolarAngleAxis dataKey="skill" tick={{ fontSize: 11 }} />
          <Tooltip
            formatter={(value) => `${value}%`}
            cursor={{ stroke: '#cbd5e1' }}
          />
          <Radar
            name="Diagnostic"
            dataKey="Diagnostic"
            stroke="#9ca3af"
            fill="#9ca3af"
            fillOpacity={0.12}
            strokeDasharray="4 4"
          />
          <Radar
            name="Current"
            dataKey="Current"
            stroke="#7c3aed"
            fill="#7c3aed"
            fillOpacity={0.3}
          />
          <Legend wrapperStyle={{ paddingTop: 8, fontSize: 12 }} />
        </RadarChart>
      </ResponsiveContainer>
    </div>
  );
}

function clamp(value: number): number {
  if (!Number.isFinite(value)) return 0;
  if (value < 0) return 0;
  if (value > 100) return 100;
  return value;
}

export default SkillRadarChartInner;
