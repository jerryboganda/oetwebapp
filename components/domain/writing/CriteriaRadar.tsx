'use client';

import {
  Radar,
  RadarChart,
  PolarGrid,
  PolarAngleAxis,
  PolarRadiusAxis,
  ResponsiveContainer,
  Legend,
  Tooltip,
} from 'recharts';
import { cn } from '@/lib/utils';
import type { WritingCriteriaScoresDto } from '@/lib/writing/types';

export interface CriteriaRadarProps {
  scores: WritingCriteriaScoresDto;
  targetScores?: WritingCriteriaScoresDto;
  className?: string;
  /**
   * Pin the radial axis maximum. Default 7 (C2-C6 max). C1 (purpose)
   * tops out at 3 but is normalised to the 0-7 axis by being charted
   * raw (so a perfect C1 reads as ~3/7 on the radar). Use `normalise`
   * to instead show every criterion on a 0-100 scale.
   */
  axisMax?: number;
  normalise?: boolean;
}

const AXIS_DEF: { code: keyof WritingCriteriaScoresDto; label: string; max: number }[] = [
  { code: 'c1', label: 'C1 Purpose', max: 3 },
  { code: 'c2', label: 'C2 Content', max: 7 },
  { code: 'c3', label: 'C3 Clarity', max: 7 },
  { code: 'c4', label: 'C4 Genre', max: 7 },
  { code: 'c5', label: 'C5 Organisation', max: 7 },
  { code: 'c6', label: 'C6 Language', max: 7 },
];

function buildSeries(
  scores: WritingCriteriaScoresDto | undefined,
  target: WritingCriteriaScoresDto | undefined,
  normalise: boolean,
) {
  return AXIS_DEF.map(({ code, label, max }) => ({
    criterion: label,
    current: scores ? (normalise ? (scores[code] / max) * 100 : scores[code]) : 0,
    target: target ? (normalise ? (target[code] / max) * 100 : target[code]) : undefined,
  }));
}

/**
 * 6-axis radar chart of OET writing criteria (C1-C6).
 *
 * Recharts is already in the project deps. The chart is fully
 * responsive — wraps its parent container.
 *
 * If `targetScores` is supplied, a dashed second polygon is overlaid
 * (e.g. target band for the candidate).
 */
export function CriteriaRadar({
  scores,
  targetScores,
  className,
  axisMax = 7,
  normalise = false,
}: CriteriaRadarProps) {
  const data = buildSeries(scores, targetScores, normalise);
  const effectiveMax = normalise ? 100 : axisMax;

  return (
    <div
      className={cn('w-full h-72', className)}
      role="img"
      aria-label="Writing criteria radar chart showing scores across the six OET criteria"
    >
      <ResponsiveContainer width="100%" height="100%">
        <RadarChart data={data} outerRadius="78%">
          <PolarGrid stroke="rgba(100,116,139,0.3)" />
          <PolarAngleAxis dataKey="criterion" tick={{ fontSize: 11, fontWeight: 700 }} />
          <PolarRadiusAxis angle={90} domain={[0, effectiveMax]} tick={false} axisLine={false} />
          <Tooltip
            formatter={(value) => {
              const num = typeof value === 'number' ? value : Number(value ?? 0);
              return normalise ? `${Math.round(num)}%` : `${num.toFixed(1)}`;
            }}
            contentStyle={{
              fontSize: 12,
              fontWeight: 600,
              borderRadius: 8,
              border: '1px solid rgba(100,116,139,0.3)',
            }}
          />
          {targetScores ? (
            <Radar
              name="Target"
              dataKey="target"
              stroke="rgba(16,185,129,0.9)"
              strokeWidth={1.5}
              strokeDasharray="4 4"
              fill="rgba(16,185,129,0.08)"
              fillOpacity={0.6}
            />
          ) : null}
          <Radar
            name="Current"
            dataKey="current"
            stroke="rgba(99,102,241,0.95)"
            strokeWidth={2}
            fill="rgba(99,102,241,0.25)"
            fillOpacity={0.7}
          />
          <Legend
            wrapperStyle={{ fontSize: 11, fontWeight: 700, paddingTop: 8 }}
            iconSize={10}
          />
        </RadarChart>
      </ResponsiveContainer>
    </div>
  );
}
