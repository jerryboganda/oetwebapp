'use client';

import { useEffect, useMemo, useState } from 'react';
import { BookOpen, FileText, GitCompare, Search } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ExemplarSideBySide } from '@/components/domain/writing/ExemplarSideBySide';
import { listWritingExemplars } from '@/lib/writing/api';
import type { WritingExemplarDto, WritingLetterType, WritingProfession } from '@/lib/writing/types';

const PROFESSIONS: Array<{ value: WritingProfession; label: string }> = [
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'other', label: 'Other' },
];

const LETTER_TYPES: Array<{ value: WritingLetterType; label: string }> = [
  { value: 'LT-RR', label: 'Referral' },
  { value: 'LT-UR', label: 'Urgent referral' },
  { value: 'LT-DG', label: 'Discharge' },
  { value: 'LT-TR', label: 'Transfer' },
  { value: 'LT-NM', label: 'Nursing management' },
  { value: 'LT-RP', label: 'Reply' },
];

export default function WritingExemplarsPage() {
  const [profession, setProfession] = useState<WritingProfession | ''>('');
  const [letterType, setLetterType] = useState<WritingLetterType | ''>('');
  const [items, setItems] = useState<WritingExemplarDto[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [candidateLetter, setCandidateLetter] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listWritingExemplars({
      profession: profession || undefined,
      letterType: letterType || undefined,
      pageSize: 24,
    })
      .then((result) => {
        if (cancelled) return;
        setItems(result.items);
        setSelectedId((current) => current && result.items.some((item) => item.id === current) ? current : result.items[0]?.id ?? null);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load exemplars.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [profession, letterType]);

  const selected = useMemo(
    () => items.find((item) => item.id === selectedId) ?? items[0] ?? null,
    [items, selectedId],
  );

  return (
    <LearnerDashboardShell pageTitle="Writing Exemplars">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Exemplars"
          icon={BookOpen}
          accent="indigo"
          title="Published Writing exemplars"
          description="High-scoring letters with rule annotations, filtered by profession and letter type."
          highlights={[
            { icon: FileText, label: 'Loaded', value: `${items.length}` },
            { icon: GitCompare, label: 'Compare', value: selected ? 'Ready' : 'Waiting' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow="Library filters"
            title="Find a matching exemplar"
            description="Use the same profession and letter type as your current practice case."
            className="mb-4"
          />
          <div className="grid gap-3 md:grid-cols-[1fr_1fr_auto] md:items-end">
            <label className="space-y-1 text-sm font-semibold text-navy">
              Profession
              <select
                value={profession}
                onChange={(event) => setProfession(event.target.value as WritingProfession | '')}
                className="min-h-11 w-full rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <option value="">All professions</option>
                {PROFESSIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </label>
            <label className="space-y-1 text-sm font-semibold text-navy">
              Letter type
              <select
                value={letterType}
                onChange={(event) => setLetterType(event.target.value as WritingLetterType | '')}
                className="min-h-11 w-full rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <option value="">All letter types</option>
                {LETTER_TYPES.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </label>
            <Button type="button" variant="outline" onClick={() => { setProfession(''); setLetterType(''); }}>
              <Search className="h-4 w-4" aria-hidden="true" /> Reset
            </Button>
          </div>
        </section>

        <div className="grid gap-4 xl:grid-cols-[22rem_1fr]">
          <aside className="rounded-2xl border border-border bg-surface p-3 shadow-sm">
            <h2 className="px-1 pb-2 text-sm font-bold text-navy">Exemplar set</h2>
            <div className="space-y-2" aria-live="polite">
              {loading ? <p className="px-1 text-sm text-muted">Loading exemplars...</p> : null}
              {!loading && items.length === 0 ? <p className="px-1 text-sm text-muted">No published exemplars match these filters.</p> : null}
              {items.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => setSelectedId(item.id)}
                  aria-pressed={selected?.id === item.id}
                  className={`w-full rounded-lg border p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${selected?.id === item.id ? 'border-primary bg-primary/5' : 'border-border bg-background hover:border-primary/40'}`}
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="info" size="sm">{item.letterType}</Badge>
                    <Badge variant="muted" size="sm" className="capitalize">{item.profession}</Badge>
                    <Badge variant="success" size="sm">Band {item.targetBand}</Badge>
                  </div>
                  <p className="mt-2 line-clamp-3 text-xs leading-relaxed text-muted">{item.letterContent}</p>
                </button>
              ))}
            </div>
          </aside>

          <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
            <LearnerSurfaceSectionHeader
              eyebrow={selected ? `${selected.profession} / ${selected.letterType}` : 'No exemplar selected'}
              title={selected ? `Band ${selected.targetBand} comparison` : 'Select an exemplar'}
              description="Compare a draft against the selected published exemplar."
              className="mb-4"
            />
            {selected ? (
              <div className="space-y-4">
                <label className="block space-y-2 text-sm font-semibold text-navy">
                  Your letter
                  <textarea
                    value={candidateLetter}
                    onChange={(event) => setCandidateLetter(event.target.value)}
                    rows={8}
                    className="w-full resize-y rounded-lg border border-border bg-background p-3 text-sm leading-relaxed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  />
                </label>
                <ExemplarSideBySide
                  candidateLetter={candidateLetter}
                  exemplarLetter={selected.letterContent}
                  exemplarAnnotations={selected.annotations}
                />
              </div>
            ) : (
              <p className="text-sm text-muted">Published exemplars will appear here after the library loads.</p>
            )}
          </section>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}