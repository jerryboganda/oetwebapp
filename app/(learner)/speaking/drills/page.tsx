'use client';

// Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills catalogue.
// Filterable by drill kind / criterion focus. Drills are sourced from
// /v1/speaking/drills (ContentItem rows with ContentType =
// "speaking_drill" — see LearnerService.SpeakingDrills.cs).
import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Dumbbell } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { Select } from '@/components/ui/form-controls';
import { MotionItem } from '@/components/ui/motion-primitives';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { CRITERION_LABEL, type SpeakingCriterionCode } from '@/lib/api/speaking-assessments';
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

function kindLabel(kind: string): string {
  return kind.charAt(0).toUpperCase() + kind.slice(1);
}

/** Drills carry criterion codes (`patientPerspective`); learners read the criterion's name. */
function criterionLabel(code: string): string {
  return CRITERION_LABEL[code as SpeakingCriterionCode] ?? code.replace(/_/g, ' ');
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
    <>
      <LearnerPageHero
        eyebrow="Speaking"
        icon={Dumbbell}
        accent="speaking"
        title="Speaking drills"
        description="Short, focused practice tasks targeting one criterion at a time. Drills are not graded. They exist to build the muscle memory that powers your full role-plays."
      />

      <Card padding="lg" className="space-y-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:max-w-2xl">
          <Select
            label="Drill kind"
            value={kindFilter}
            onChange={(e) => setKindFilter(e.target.value)}
            options={[
              { value: '', label: 'All kinds' },
              ...(data?.kinds ?? []).map((k) => ({ value: k, label: kindLabel(k) })),
            ]}
          />
          <Select
            label="Criterion focus"
            value={criterionFilter}
            onChange={(e) => setCriterionFilter(e.target.value)}
            options={CRITERION_FILTERS}
          />
        </div>

        {data ? (
          <p className="text-xs tabular-nums text-muted">
            {data.totalCount} drill{data.totalCount === 1 ? '' : 's'} ·{' '}
            <span className="font-semibold text-primary">{completionPercent}%</span> completed
          </p>
        ) : null}
      </Card>

      {error ? (
        <InlineAlert variant="error">{error}</InlineAlert>
      ) : loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : data && data.items.length === 0 ? (
        <EmptyState
          icon={<Dumbbell className="h-8 w-8" />}
          title="No drills match these filters."
          description="Try clearing them."
        />
      ) : (
        <ul className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {(data?.items ?? []).map((drill, index) => (
            <li key={drill.id} className="min-w-0">
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                <DrillCard drill={drill} />
              </MotionItem>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function DrillCard({ drill }: { drill: SpeakingDrillRow }) {
  return (
    <Card padding="md" className="flex h-full flex-col gap-3">
      <div className="space-y-1">
        <p className="eyebrow text-skill-speaking">
          {kindLabel(drill.kind)}
        </p>
        <h3 className="text-base font-bold leading-tight text-navy">{drill.title}</h3>
      </div>

      {drill.caseNotes ? (
        <p className="text-sm text-muted">{drill.caseNotes}</p>
      ) : null}

      <div className="flex flex-wrap gap-1.5">
        {drill.criteriaFocus.map((c) => (
          <Badge key={c} variant="muted">
            {criterionLabel(c)}
          </Badge>
        ))}
      </div>

      <div className="mt-auto flex flex-wrap items-center justify-between gap-3">
        <span className="text-xs tabular-nums text-muted">
          ≈ {drill.estimatedDurationMinutes} min
        </span>
        {drill.completed ? (
          <Badge variant="success">Completed</Badge>
        ) : (
          <Button type="button" variant="primary" asChild>
            <Link href={`/speaking/drills/${encodeURIComponent(drill.drillId || drill.id)}`}>
              Start drill
            </Link>
          </Button>
        )}
      </div>
    </Card>
  );
}
