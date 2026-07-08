'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  AlertTriangle,
  ArrowRight,
  Gauge,
  Headphones,
  Sparkles,
  TrendingUp,
  Activity,
} from 'lucide-react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { Card } from '@/components/ui/card';
import { StatCard } from '@/components/ui/stat-card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchLearnerSpeakingAnalytics,
  type LearnerSpeakingAnalytics,
} from '@/lib/api/speaking-analytics';

const CRITERION_LABELS: Record<string, string> = {
  intelligibility: 'Intelligibility',
  fluency: 'Fluency',
  appropriateness: 'Appropriateness',
  grammarExpression: 'Grammar & Expression',
  relationshipBuilding: 'Relationship Building',
  patientPerspective: 'Patient Perspective',
  structure: 'Structure',
  informationGathering: 'Information Gathering',
  informationGiving: 'Information Giving',
};

const CLINICAL_CRITERIA = new Set([
  'relationshipBuilding',
  'patientPerspective',
  'structure',
  'informationGathering',
  'informationGiving',
]);

const READINESS_TONE: Record<string, 'success' | 'warning' | 'danger' | 'info' | 'default'> = {
  on_track: 'success',
  needs_focus: 'warning',
  at_risk: 'danger',
  not_enough_data: 'info',
};

const READINESS_LABEL: Record<string, string> = {
  on_track: 'On track for OET',
  needs_focus: 'Borderline - keep practising',
  at_risk: 'At risk - drills recommended',
  not_enough_data: 'Not enough data yet',
};

interface BarPoint {
  criterion: string;
  label: string;
  score: number;
  /** Normalised 0..6 for chart consistency. */
  normalized: number;
  isClinical: boolean;
}

function normalize(criterion: string, score: number): number {
  return CLINICAL_CRITERIA.has(criterion) ? score * 2 : score;
}

function formatBand(band: string): string {
  if (!band || band === 'not_enough_data') return 'Pending';
  return band
    .split(/[_\s-]+/)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

export interface LearnerSpeakingAnalyticsDashboardProps {
  /** Optional initial data, e.g. server-fetched. Falls back to client fetch. */
  initial?: LearnerSpeakingAnalytics | null;
}

export function LearnerSpeakingAnalyticsDashboard({
  initial = null,
}: LearnerSpeakingAnalyticsDashboardProps) {
  const [data, setData] = useState<LearnerSpeakingAnalytics | null>(initial);
  const [isLoading, setIsLoading] = useState(initial === null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    if (initial !== null) return;
    (async () => {
      setIsLoading(true);
      setError(null);
      try {
        const result = await fetchLearnerSpeakingAnalytics();
        if (!cancelled) setData(result);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load analytics.');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [initial]);

  const trendData = useMemo(() => {
    if (!data) return [] as Array<Record<string, string | number>>;
    const byDate = new Map<string, Record<string, string | number>>();
    for (const point of data.criterionTrends ?? []) {
      const existing = byDate.get(point.date) ?? { date: point.date };
      existing[point.criterion] = normalize(point.criterion, point.score);
      byDate.set(point.date, existing);
    }
    return Array.from(byDate.values()).sort((a, b) =>
      String(a.date).localeCompare(String(b.date)),
    );
  }, [data]);

  const barData = useMemo<BarPoint[]>(() => {
    if (!data) return [];
    const buckets = new Map<string, number[]>();
    for (const point of data.criterionTrends ?? []) {
      const arr = buckets.get(point.criterion) ?? [];
      arr.push(point.score);
      buckets.set(point.criterion, arr);
    }
    return Array.from(buckets.entries())
      .map(([criterion, scores]) => {
        const avg = scores.reduce((a, b) => a + b, 0) / Math.max(scores.length, 1);
        return {
          criterion,
          label: CRITERION_LABELS[criterion] ?? criterion,
          score: Number(avg.toFixed(2)),
          normalized: Number(normalize(criterion, avg).toFixed(2)),
          isClinical: CLINICAL_CRITERIA.has(criterion),
        };
      })
      .sort((a, b) => a.normalized - b.normalized);
  }, [data]);

  const usedCriteria = useMemo(
    () => Array.from(new Set((data?.criterionTrends ?? []).map((p) => p.criterion))),
    [data],
  );

  if (isLoading) {
    return (
      <Card className="p-8 text-center text-muted" role="status" aria-live="polite">
        Loading your speaking analytics...
      </Card>
    );
  }

  if (error) {
    return (
      <InlineAlert variant="error" title="Could not load analytics">
        {error}
      </InlineAlert>
    );
  }

  if (!data) {
    return null;
  }

  const readiness = (data.readinessStatus ?? 'not_enough_data') as keyof typeof READINESS_TONE;
  const readinessTone = READINESS_TONE[readiness] ?? 'info';
  const readinessLabel = READINESS_LABEL[readiness] ?? 'Status pending';
  const wpmTone: 'success' | 'warning' | 'default' =
    data.speakingSpeedWpm >= 120 && data.speakingSpeedWpm <= 170
      ? 'success'
      : data.speakingSpeedWpm === 0
        ? 'default'
        : 'warning';

  return (
    <div className="space-y-6">
      {/* -- Hero band ------------------------------------------------- */}
      <Card className="bg-surface p-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.18em] text-muted">
              Estimated speaking band
            </p>
            <p className="mt-2 text-4xl font-bold text-navy dark:text-white">
              {formatBand(data.estimatedBand)}
            </p>
            <p className="mt-1 text-sm text-muted">
              Practice-only - official scoring runs on real OET attempts.
            </p>
          </div>
          <div className="flex flex-col gap-2 sm:items-end">
            <Badge variant={readinessTone === 'success' ? 'success' : readinessTone === 'danger' ? 'danger' : 'muted'}>
              {readinessLabel}
            </Badge>
            <p className="text-sm text-muted">
              {data.sessionCount} session{data.sessionCount === 1 ? '' : 's'} in last 30 days
            </p>
          </div>
        </div>
      </Card>

      {/* -- Vital stats ---------------------------------------------- */}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="Scaled score"
          value={data.currentScaled || '-'}
          icon={<Sparkles className="h-4 w-4" />}
          tone="info"
          hint="Out of 500. Practice estimate."
        />
        <StatCard
          label="Speaking speed"
          value={data.speakingSpeedWpm ? `${data.speakingSpeedWpm} wpm` : '-'}
          icon={<Gauge className="h-4 w-4" />}
          tone={wpmTone}
          hint="Sweet spot: 120-170 wpm"
        />
        <StatCard
          label="Avg role-play length"
          value={
            data.avgRolePlayLengthSeconds > 0
              ? `${Math.round(data.avgRolePlayLengthSeconds)}s`
              : '-'
          }
          icon={<Activity className="h-4 w-4" />}
          tone="default"
          hint="Target: ~5 minutes / 300s"
        />
        <StatCard
          label="Weakest criterion"
          value={
            data.weakestCriterion
              ? CRITERION_LABELS[data.weakestCriterion] ?? data.weakestCriterion
              : '-'
          }
          icon={<TrendingUp className="h-4 w-4" />}
          tone="warning"
          hint={
            data.strongestCriterion
              ? `Strongest: ${CRITERION_LABELS[data.strongestCriterion] ?? data.strongestCriterion}`
              : undefined
          }
        />
      </div>

      {/* -- Criterion trend line chart ------------------------------- */}
      <Card className="p-5">
        <header className="mb-3 flex items-center justify-between">
          <div>
            <h2 className="text-base font-semibold text-navy dark:text-white">
              Criterion trends (last 30 days)
            </h2>
            <p className="text-xs text-muted">
              Per-criterion weekly average. Clinical criteria (0-3) shown doubled to align with the linguistic 0-6 scale.
            </p>
          </div>
        </header>
        <div className="h-72 w-full">
          {trendData.length === 0 ? (
            <div className="flex h-full items-center justify-center text-sm text-muted">
              No trend data yet - complete a few sessions to populate the chart.
            </div>
          ) : (
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={trendData}>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                <XAxis dataKey="date" fontSize={11} stroke="hsl(var(--muted))" />
                <YAxis domain={[0, 6]} fontSize={11} stroke="hsl(var(--muted))" />
                <Tooltip
                  contentStyle={{
                    background: 'var(--surface)',
                    border: '1px solid hsl(var(--border))',
                    borderRadius: 8,
                  }}
                />
                <Legend wrapperStyle={{ fontSize: 11 }} />
                {usedCriteria.map((criterion, idx) => (
                  <Line
                    key={criterion}
                    type="monotone"
                    dataKey={criterion}
                    name={CRITERION_LABELS[criterion] ?? criterion}
                    stroke={`hsl(${(idx * 49) % 360} 70% 50%)`}
                    strokeWidth={2}
                    dot={{ r: 2 }}
                  />
                ))}
              </LineChart>
            </ResponsiveContainer>
          )}
        </div>
      </Card>

      {/* -- Strengths/Weaknesses bar chart --------------------------- */}
      <Card className="p-5">
        <header className="mb-3">
          <h2 className="text-base font-semibold text-navy dark:text-white">
            Strengths and weaknesses
          </h2>
          <p className="text-xs text-muted">
            Bars ordered weakest to strongest. Aim above 4 across the board for a band-B.
          </p>
        </header>
        <div className="h-72 w-full">
          {barData.length === 0 ? (
            <div className="flex h-full items-center justify-center text-sm text-muted">
              No assessment data available.
            </div>
          ) : (
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={barData} layout="vertical">
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                <XAxis type="number" domain={[0, 6]} fontSize={11} stroke="hsl(var(--muted))" />
                <YAxis
                  type="category"
                  dataKey="label"
                  width={150}
                  fontSize={11}
                  stroke="hsl(var(--muted))"
                />
                <Tooltip
                  formatter={((value: unknown) => [
                    typeof value === 'number' ? value.toFixed(2) : String(value ?? ''),
                    'Normalised score',
                  ]) as never}
                  contentStyle={{
                    background: 'var(--surface)',
                    border: '1px solid hsl(var(--border))',
                    borderRadius: 8,
                  }}
                />
                <Bar
                  dataKey="normalized"
                  fill="hsl(var(--primary))"
                  radius={[0, 6, 6, 0]}
                />
              </BarChart>
            </ResponsiveContainer>
          )}
        </div>
      </Card>

      {/* -- Recurring issues + next actions --------------------------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <header className="mb-3 flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 text-warning" />
            <h2 className="text-base font-semibold text-navy dark:text-white">
              Recurring issues
            </h2>
          </header>
          {data.recurringIssues.length === 0 ? (
            <p className="text-sm text-muted">
              No recurring issues flagged. Nice work - keep your streak going.
            </p>
          ) : (
            <ul className="space-y-2" role="list">
              {data.recurringIssues.map((issue) => (
                <li
                  key={issue}
                  className="flex items-start gap-2 rounded-lg border border-border bg-surface/60 p-3"
                >
                  <AlertTriangle
                    className="mt-0.5 h-4 w-4 shrink-0 text-warning"
                    aria-hidden="true"
                  />
                  <span className="text-sm text-navy dark:text-white">{issue}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card className="p-5">
          <header className="mb-3 flex items-center gap-2">
            <Headphones className="h-4 w-4 text-primary" />
            <h2 className="text-base font-semibold text-navy dark:text-white">
              Recommended next actions
            </h2>
          </header>
          <ul className="space-y-2 text-sm" role="list">
            {data.weakestCriterion ? (
              <li className="flex items-start gap-2">
                <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                <span>
                  Drill the{' '}
                  <strong>{CRITERION_LABELS[data.weakestCriterion] ?? data.weakestCriterion}</strong>{' '}
                  criterion until it climbs above 4.
                </span>
              </li>
            ) : null}
            {data.speakingSpeedWpm > 170 ? (
              <li className="flex items-start gap-2">
                <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                <span>
                  Slow down - your average pace ({data.speakingSpeedWpm} wpm) is above the OET sweet
                  spot.
                </span>
              </li>
            ) : null}
            {data.speakingSpeedWpm > 0 && data.speakingSpeedWpm < 120 ? (
              <li className="flex items-start gap-2">
                <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                <span>
                  Pick up the pace - fluency drills will help you hit the OET sweet spot.
                </span>
              </li>
            ) : null}
            <li className="flex items-start gap-2">
              <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              <span>
                Continue the{' '}
                <Link href="/speaking/pathway" className="font-semibold text-primary underline">
                  16-stage speaking pathway
                </Link>
                .
              </span>
            </li>
            <li className="flex items-start gap-2">
              <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              <span>
                Book a tutor-led{' '}
                <Link href="/mocks?subtest=speaking" className="font-semibold text-primary underline">
                  recorded mock
                </Link>{' '}
                when you feel ready.
              </span>
            </li>
          </ul>
          <div className="mt-4">
            <Button asChild variant="primary" size="sm"><Link href="/speaking/drills">Open drills</Link></Button>
          </div>
        </Card>
      </div>
    </div>
  );
}

export default LearnerSpeakingAnalyticsDashboard;
