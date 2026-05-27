'use client';

import { useEffect, useMemo, useState } from 'react';
import { ClipboardList, Layers } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
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

  return (
    <LearnerDashboardShell pageTitle="Case-note drills">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Selection drills"
          icon={ClipboardList}
          accent="amber"
          title="Train relevance triage on real case notes"
          description="Picking the right notes is half of W1 mastery — these drills score every sentence against the gold-standard tag."
          highlights={[
            { icon: Layers, label: 'Drills', value: `${drills.length}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <fieldset className="flex flex-wrap items-center gap-2" aria-label="Filter by profession">
          <legend className="sr-only">Filter drills</legend>
          <span className="text-xs font-bold uppercase tracking-wider text-muted">Profession:</span>
          <button
            type="button"
            onClick={() => setProfession(null)}
            aria-pressed={profession === null}
            className={`rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${profession === null ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
          >
            All
          </button>
          {PROFESSIONS.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => setProfession(p.id)}
              aria-pressed={profession === p.id}
              className={`rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${profession === p.id ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
            >
              {p.label}
            </button>
          ))}
        </fieldset>

        <div className="grid gap-4 lg:grid-cols-12">
          <aside className="lg:col-span-4" aria-label="Drill list">
            <LearnerSurfaceSectionHeader eyebrow="Drills" title="Pick one" description="The runner appears on the right." className="mb-3" />
            <ul className="space-y-2">
              {drills.length === 0 ? (
                <li>
                  <Card padding="md">
                    <CardContent>
                      <p className="text-sm text-muted">No drills available for this filter yet.</p>
                    </CardContent>
                  </Card>
                </li>
              ) : null}
              {drills.map((drill) => {
                const isActive = drill.id === active?.id;
                return (
                  <li key={drill.id}>
                    <button
                      type="button"
                      onClick={() => {
                        setActive(drill);
                        setResult(null);
                        setLastSelected([]);
                      }}
                      aria-pressed={isActive}
                      className={`flex w-full items-start gap-2 rounded-xl border p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${isActive ? 'border-primary bg-primary/10' : 'border-border bg-background hover:border-primary/40'}`}
                    >
                      <span className="flex-1">
                        <span className="flex flex-wrap items-center gap-1">
                          <Badge variant="muted" size="sm">{drill.format}</Badge>
                          <Badge variant="info" size="sm" className="capitalize">{drill.profession}</Badge>
                        </span>
                        <span className="mt-1 block text-sm font-bold text-navy line-clamp-2">{drill.promptMarkdown.slice(0, 120)}</span>
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </aside>

          <section className="lg:col-span-8" aria-label="Drill runner">
            {active ? (
              <>
                <Card padding="md" className="mb-3">
                  <CardContent>
                    <p className="text-sm text-navy whitespace-pre-line">{active.promptMarkdown}</p>
                  </CardContent>
                </Card>
                <CaseNoteHighlighter
                  caseNotes={sentences}
                  onSubmit={(indices) => void onSubmit(indices)}
                  scored={!!result}
                  initialSelectedIndices={lastSelected}
                />
                {submitting ? <p className="mt-2 text-xs text-muted">Submitting…</p> : null}
                {result ? (
                  <Card padding="md" className="mt-3">
                    <CardContent>
                      <p className="text-sm font-bold text-navy">
                        Score: <span className="text-lg">{Math.round(result.scorePercent)}%</span>
                      </p>
                    </CardContent>
                  </Card>
                ) : null}
              </>
            ) : (
              <Card padding="lg">
                <CardContent>
                  <p className="text-sm text-muted">Select a drill from the list to start.</p>
                </CardContent>
              </Card>
            )}
          </section>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
