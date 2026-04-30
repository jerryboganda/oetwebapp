'use client';

// Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills catalogue.
// Filterable by drill kind / criterion focus. Drills are sourced from
// /v1/speaking/drills (ContentItem rows with ContentType =
// "speaking_drill" — see LearnerService.SpeakingDrills.cs).
import { useEffect, useMemo, useState } from 'react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchSpeakingDrills,
  type SpeakingDrillRow,
  type SpeakingDrillsListResponse,
} from '@/lib/api';

const CRITERION_FILTERS: Array<{ value: string; label: string }> = [
  { value: '', label: 'All criteria' },
  { value: 'intelligibility', label: 'Intelligibility' },
  { value: 'fluency', label: 'Fluency' },
  { value: 'appropriateness', label: 'Appropriateness' },
  { value: 'grammar_expression', label: 'Grammar & expression' },
  { value: 'relationship_building', label: 'Relationship building' },
  { value: 'patient_perspective', label: 'Patient perspective' },
  { value: 'information_giving', label: 'Information giving' },
  { value: 'information_gathering', label: 'Information gathering' },
];

function difficultyVariant(d: string): 'success' | 'warning' | 'danger' | 'muted' {
  if (d === 'easy') return 'success';
  if (d === 'medium') return 'warning';
  if (d === 'hard') return 'danger';
  return 'muted';
}

function kindLabel(kind: string): string {
  return kind.charAt(0).toUpperCase() + kind.slice(1);
}

export default function SpeakingDrillsPage() {
  const [data, setData] = useState<SpeakingDrillsListResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [kindFilter, setKindFilter] = useState<string>('');
  const [criterionFilter, setCriterionFilter] = useState<string>('');

  useEffect(() => {
    let cancelled = false;
    fetchSpeakingDrills({ kind: kindFilter || undefined, criterion: criterionFilter || undefined })
      .then((response) => {
        if (cancelled) return;
        setData(response);
        setError(null);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : 'Could not load drills.';
        setError(message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [kindFilter, criterionFilter]);

  const completionPercent = useMemo(() => {
    if (!data || data.totalCount === 0) return 0;
    return Math.round((data.completedCount / data.totalCount) * 100);
  }, [data]);

  return (
    <LearnerDashboardShell pageTitle="Speaking Drills">
      <div className="mx-auto max-w-5xl space-y-6 p-4 sm:p-6">
        <header className="space-y-2">
          <h1 className="text-2xl font-black text-navy">Speaking drills</h1>
          <p className="text-sm text-muted">
            Short, focused practice tasks targeting one criterion at a time. Drills
            are not graded — they exist to build the muscle memory that powers your
            full role-plays.
          </p>
        </header>

        <Card className="space-y-4 p-4 sm:p-6">
          <div className="flex flex-wrap gap-3">
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Drill kind
              <select
                value={kindFilter}
                onChange={(e) => setKindFilter(e.target.value)}
                className="min-w-[10rem] rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy"
              >
                <option value="">All kinds</option>
                {(data?.kinds ?? []).map((k) => (
                  <option key={k} value={k}>
                    {kindLabel(k)}
                  </option>
                ))}
              </select>
            </label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Criterion focus
              <select
                value={criterionFilter}
                onChange={(e) => setCriterionFilter(e.target.value)}
                className="min-w-[14rem] rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy"
              >
                {CRITERION_FILTERS.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          {data ? (
            <p className="text-xs text-muted">
              {data.totalCount} drill{data.totalCount === 1 ? '' : 's'} ·{' '}
              <span className="font-semibold text-primary">{completionPercent}%</span> completed
            </p>
          ) : null}
        </Card>

        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : null}

        {loading ? (
          <Card className="p-6 text-center text-sm text-muted">Loading drills…</Card>
        ) : data && data.items.length === 0 ? (
          <Card className="p-6 text-center text-sm text-muted">
            No drills match these filters. Try clearing them.
          </Card>
        ) : (
          <ul className="grid gap-4 md:grid-cols-2">
            {(data?.items ?? []).map((drill) => (
              <DrillCard key={drill.id} drill={drill} />
            ))}
          </ul>
        )}
      </div>
    </LearnerDashboardShell>
  );
}

function DrillCard({ drill }: { drill: SpeakingDrillRow }) {
  return (
    <li>
      <Card className="flex h-full flex-col gap-3 p-5">
        <div className="flex items-start justify-between gap-3">
          <div className="space-y-1">
            <p className="text-[11px] font-bold uppercase tracking-widest text-primary">
              {kindLabel(drill.kind)}
            </p>
            <h3 className="text-base font-black leading-tight text-navy">{drill.title}</h3>
          </div>
          <Badge variant={difficultyVariant(drill.difficulty)}>{drill.difficulty}</Badge>
        </div>

        {drill.caseNotes ? (
          <p className="text-sm text-muted">{drill.caseNotes}</p>
        ) : null}

        <div className="flex flex-wrap gap-1">
          {drill.criteriaFocus.map((c) => (
            <Badge key={c} variant="info">
              {c.replace(/_/g, ' ')}
            </Badge>
          ))}
        </div>

        <div className="mt-auto flex items-center justify-between gap-3">
          <span className="text-xs text-muted">
            ≈ {drill.estimatedDurationMinutes} min
          </span>
          {drill.completed ? (
            <Badge variant="success">Completed</Badge>
          ) : (
            <Button
              type="button"
              variant="primary"
              onClick={() => {
                window.location.href = `/speaking/task/${encodeURIComponent(drill.id)}?mode=self`;
              }}
            >
              Start drill
            </Button>
          )}
        </div>
      </Card>
    </li>
  );
}
