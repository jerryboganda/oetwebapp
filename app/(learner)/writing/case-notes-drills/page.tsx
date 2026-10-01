'use client';

import { useEffect, useMemo, useState } from 'react';
import { ClipboardList, Layers } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { CaseNoteHighlighter, type CaseNoteSentence } from '@/components/domain/writing/CaseNoteHighlighter';
import { listCaseNoteDrills, submitCaseNoteDrillAttempt } from '@/lib/writing/api';
import type {
  WritingCaseNoteDrillAttemptResultDto,
  WritingCaseNoteDrillDto,
  WritingProfession,
} from '@/lib/writing/types';

const PROFESSIONS: Array<{ id: WritingProfession; label: string }> = [
  { id: 'medicine', label: 'Medicine' },
  { id: 'pharmacy', label: 'Pharmacy' },
  { id: 'nursing', label: 'Nursing' },
  { id: 'other', label: 'Other' },
];

export default function WritingCaseNoteDrillsPage() {
  const [drills, setDrills] = useState<WritingCaseNoteDrillDto[]>([]);
  const [active, setActive] = useState<WritingCaseNoteDrillDto | null>(null);
  const [result, setResult] = useState<WritingCaseNoteDrillAttemptResultDto | null>(null);
  const [lastSelected, setLastSelected] = useState<number[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [profession, setProfession] = useState<WritingProfession | null>(null);
  const [submitting, setSubmitting] = useState(false);
  // The filter whose list last settled; anything else is still loading.
  const [settledFilter, setSettledFilter] = useState<WritingProfession | 'all' | null>(null);
  const loadingDrills = settledFilter !== (profession ?? 'all');

  useEffect(() => {
    let cancelled = false;
    listCaseNoteDrills(profession ? { profession } : {})
      .then((r) => {
        if (cancelled) return;
        setDrills(r.items);
        if (!active && r.items[0]) setActive(r.items[0]);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load case-note drills.');
      })
      .finally(() => {
        if (!cancelled) setSettledFilter(profession ?? 'all');
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profession]);

  const sentences: CaseNoteSentence[] = useMemo(() => {
    if (!active) return [];
    return active.sentences.map((s) => ({
      index: s.index,
      text: s.text,
      groundTruth: result ? result.perSentence.find((p) => p.index === s.index)?.correctLabel : undefined,
    }));
  }, [active, result]);

  const onSubmit = async (selectedIndices: number[]) => {
    if (!active) return;
    setSubmitting(true);
    setError(null);
    setLastSelected(selectedIndices);
    try {
      const r = await submitCaseNoteDrillAttempt(active.id, { selectedIndices });
      setResult(r);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not submit drill attempt.');
    } finally {
      setSubmitting(false);
    }
  };

  const pillClassName = (selected: boolean) => `pressable min-h-11 rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${selected ? 'border-primary bg-primary text-white dark:bg-primary-700' : 'hover-primary border-border bg-background text-navy hover:border-primary/40'}`;

  return (
    <>
      <LearnerPageHero
        eyebrow="Selection drills"
        icon={ClipboardList}
        accent="amber"
        title="Train relevance triage on real case notes"
        description="Picking the right notes is half of W1 mastery. These drills score every sentence against the gold-standard tag."
        highlights={[
          { icon: Layers, label: 'Drills', value: `${drills.length}` },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <fieldset className="flex flex-wrap items-center gap-2" aria-label="Filter by profession">
        <legend className="sr-only">Filter drills</legend>
        <span className="eyebrow text-muted">Profession:</span>
        <button
          type="button"
          onClick={() => setProfession(null)}
          aria-pressed={profession === null}
          className={pillClassName(profession === null)}
        >
          All
        </button>
        {PROFESSIONS.map((p) => (
          <button
            key={p.id}
            type="button"
            onClick={() => setProfession(p.id)}
            aria-pressed={profession === p.id}
            className={pillClassName(profession === p.id)}
          >
            {p.label}
          </button>
        ))}
      </fieldset>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
        <aside className="min-w-0 lg:col-span-4" aria-label="Drill list" aria-busy={loadingDrills}>
          <LearnerSurfaceSectionHeader eyebrow="Drills" title="Pick one" className="mb-3" />
          {loadingDrills && drills.length === 0 ? (
            <LearnerSkeleton variant="list" />
          ) : drills.length === 0 ? (
            error ? null : <EmptyState icon={<ClipboardList className="h-8 w-8" />} title="No drills available for this filter yet." className="py-8" />
          ) : (
            <ul className="space-y-2">
              {drills.map((drill, index) => {
                const isActive = drill.id === active?.id;
                return (
                  <li key={drill.id}>
                    <MotionItem delayIndex={Math.min(index, 5)}>
                      <button
                        type="button"
                        onClick={() => {
                          setActive(drill);
                          setResult(null);
                          setLastSelected([]);
                        }}
                        aria-pressed={isActive}
                        className={`flex w-full items-start gap-2 rounded-xl border p-3 text-start transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${isActive ? 'border-primary bg-primary/10' : 'hover-primary border-border bg-background hover:border-primary/40'}`}
                      >
                        <span className="min-w-0 flex-1">
                          <span className="flex flex-wrap items-center gap-1">
                            <Badge variant="muted" size="sm">{drill.format}</Badge>
                            <Badge variant="info" size="sm" className="capitalize">{drill.profession}</Badge>
                          </span>
                          <span className="mt-1 block line-clamp-2 text-sm font-bold text-navy">{drill.promptMarkdown.slice(0, 120)}</span>
                        </span>
                      </button>
                    </MotionItem>
                  </li>
                );
              })}
            </ul>
          )}
        </aside>

        <section className="min-w-0 space-y-3 lg:col-span-8" aria-label="Drill runner">
          {active ? (
            <>
              <Card padding="md">
                <p className="whitespace-pre-line text-sm text-navy">{active.promptMarkdown}</p>
              </Card>
              <CaseNoteHighlighter
                caseNotes={sentences}
                onSubmit={(indices) => void onSubmit(indices)}
                scored={!!result}
                initialSelectedIndices={lastSelected}
              />
              {submitting ? <p role="status" className="text-xs text-muted">Submitting…</p> : null}
              {result ? (
                <Card padding="md">
                  <p className="text-sm font-bold text-navy">
                    Score: <CountUp value={Math.round(result.scorePercent)} suffix="%" className="text-lg" />
                  </p>
                </Card>
              ) : null}
            </>
          ) : (
            <EmptyState icon={<ClipboardList className="h-8 w-8" />} title="Select a drill from the list to start." />
          )}
        </section>
      </div>
    </>
  );
}
