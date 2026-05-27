'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, FilterIcon, Layers, Library, Search } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { listWritingScenarios } from '@/lib/writing/api';
import type {
  WritingLetterType,
  WritingProfession,
  WritingScenarioDto,
} from '@/lib/writing/types';

const PROFESSIONS: Array<{ id: WritingProfession; label: string }> = [
  { id: 'medicine', label: 'Medicine' },
  { id: 'pharmacy', label: 'Pharmacy' },
  { id: 'nursing', label: 'Nursing' },
  { id: 'other', label: 'Other' },
];

const LETTER_TYPES: Array<{ id: WritingLetterType; label: string }> = [
  { id: 'LT-RR', label: 'Routine referral' },
  { id: 'LT-UR', label: 'Urgent referral' },
  { id: 'LT-DG', label: 'Discharge' },
  { id: 'LT-TR', label: 'Transfer' },
  { id: 'LT-RP', label: 'Response' },
  { id: 'LT-NM', label: 'Non-medical' },
];

const DIFFICULTIES = [1, 2, 3, 4, 5] as const;

export default function WritingPracticeLibraryPage() {
  const [scenarios, setScenarios] = useState<WritingScenarioDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [profession, setProfession] = useState<WritingProfession | null>(null);
  const [letterType, setLetterType] = useState<WritingLetterType | null>(null);
  const [difficulty, setDifficulty] = useState<number | null>(null);
  const [search, setSearch] = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    listWritingScenarios({
      profession: profession ?? undefined,
      letterType: letterType ?? undefined,
      difficulty: difficulty ?? undefined,
      search: search.trim() || undefined,
      pageSize: 50,
    })
      .then((result) => {
        if (cancelled) return;
        setScenarios(result.items);
        setError(null);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load scenarios.');
      })
      .finally(() => {
        if (cancelled) return;
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [profession, letterType, difficulty, search]);

  const topicSet = useMemo(() => {
    const s = new Set<string>();
    for (const sc of scenarios) {
      for (const t of sc.topics) s.add(t);
    }
    return Array.from(s).sort();
  }, [scenarios]);

  return (
    <LearnerDashboardShell pageTitle="Practice Library">
      <div className="space-y-6" aria-busy={loading}>
        <LearnerPageHero
          eyebrow="Practice"
          icon={Library}
          accent="amber"
          title="Browse the scenario library"
          description="Choose any scenario, then pick practice or coached mode. Letters are graded the same way the diagnostic was."
          highlights={[
            { icon: Layers, label: 'Total', value: `${scenarios.length}` },
            { icon: FilterIcon, label: 'Active filters', value: `${[profession, letterType, difficulty, search].filter(Boolean).length}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <LearnerSurfaceSectionHeader
          eyebrow="Filters"
          title="Narrow the catalogue"
          description="Filters apply server-side; topic chips reflect the current results."
        />

        <fieldset className="grid gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm md:grid-cols-2" aria-label="Filter scenarios">
          <legend className="sr-only">Filter scenarios</legend>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            Profession
            <select
              value={profession ?? ''}
              onChange={(e) => setProfession((e.target.value || null) as WritingProfession | null)}
              className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">All</option>
              {PROFESSIONS.map((p) => (
                <option key={p.id} value={p.id}>{p.label}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            Letter type
            <select
              value={letterType ?? ''}
              onChange={(e) => setLetterType((e.target.value || null) as WritingLetterType | null)}
              className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">All</option>
              {LETTER_TYPES.map((t) => (
                <option key={t.id} value={t.id}>{t.label} ({t.id})</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            Difficulty
            <select
              value={difficulty ?? ''}
              onChange={(e) => setDifficulty(e.target.value ? Number(e.target.value) : null)}
              className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">All</option>
              {DIFFICULTIES.map((d) => (
                <option key={d} value={d}>Level {d}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            Search
            <span className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search by title or topic"
                className="min-h-11 w-full rounded-lg border border-border bg-background pl-9 pr-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </span>
          </label>
        </fieldset>

        {topicSet.length > 0 ? (
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
            <span className="font-bold uppercase tracking-wider">Topics in results:</span>
            {topicSet.slice(0, 12).map((t) => (
              <Badge key={t} variant="muted" size="sm">{t}</Badge>
            ))}
            {topicSet.length > 12 ? <span>+{topicSet.length - 12} more</span> : null}
          </div>
        ) : null}

        <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label="Writing scenarios">
          {scenarios.length === 0 && !loading ? (
            <li className="col-span-full">
              <Card padding="lg">
                <CardContent>
                  <p className="text-sm text-muted">No scenarios match the filters.</p>
                </CardContent>
              </Card>
            </li>
          ) : null}
          {scenarios.map((scenario) => (
            <li key={scenario.id}>
              <Card padding="md" aria-label={`Scenario: ${scenario.title}`}>
                <CardContent>
                  <header className="flex flex-wrap items-center justify-between gap-1">
                    <div className="flex flex-wrap items-center gap-1">
                      <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
                      <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                      <Badge variant={scenario.difficulty >= 4 ? 'danger' : scenario.difficulty >= 3 ? 'warning' : 'success'} size="sm">
                        Level {scenario.difficulty}
                      </Badge>
                    </div>
                  </header>
                  <h2 className="mt-2 text-base font-bold text-navy">{scenario.title}</h2>
                  {scenario.topics.length > 0 ? (
                    <p className="mt-1 text-xs text-muted">Topics: {scenario.topics.slice(0, 4).join(', ')}</p>
                  ) : null}
                  <div className="mt-3">
                    <Button asChild size="sm">
                      <Link href={`/writing/practice/session/${encodeURIComponent(scenario.id)}`} aria-label={`Practice scenario ${scenario.title}`}>
                        Practice this <ArrowRight className="h-3 w-3" aria-hidden="true" />
                      </Link>
                    </Button>
                  </div>
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
