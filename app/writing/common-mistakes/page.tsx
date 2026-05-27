'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { AlertCircle, Filter } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { MistakeCard } from '@/components/domain/writing/MistakeCard';
import { listCommonMistakes } from '@/lib/writing/api';
import type { WritingCommonMistakeDto, WritingSubSkill } from '@/lib/writing/types';

const SKILLS: WritingSubSkill[] = ['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'];

export default function WritingCommonMistakesPage() {
  const [items, setItems] = useState<WritingCommonMistakeDto[]>([]);
  const [subSkill, setSubSkill] = useState<WritingSubSkill | null>(null);
  const [category, setCategory] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    listCommonMistakes({ subSkill: subSkill ?? undefined, category: category || undefined })
      .then((r) => {
        if (cancelled) return;
        setItems(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load mistakes.');
      });
    return () => {
      cancelled = true;
    };
  }, [subSkill, category]);

  const categories = useMemo(() => {
    const s = new Set<string>();
    for (const m of items) s.add(m.category);
    return Array.from(s).sort();
  }, [items]);

  return (
    <LearnerDashboardShell pageTitle="Common Mistakes Library">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Library"
          icon={AlertCircle}
          accent="amber"
          title="The 20 mistakes that cost the most marks"
          description="Every card has a wrong/right example pair and links straight to the canon rule and lesson."
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-3 shadow-sm">
          <fieldset className="flex flex-wrap items-center gap-2" aria-label="Filters">
            <legend className="sr-only">Filters</legend>
            <span className="text-xs font-bold uppercase tracking-wider text-muted">
              <Filter className="mr-1 inline h-3 w-3" aria-hidden="true" /> Skill:
            </span>
            <button
              type="button"
              onClick={() => setSubSkill(null)}
              aria-pressed={subSkill === null}
              className={`rounded-full border px-3 py-1.5 text-xs font-bold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${subSkill === null ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
            >
              All
            </button>
            {SKILLS.map((skill) => (
              <button
                key={skill}
                type="button"
                onClick={() => setSubSkill(skill)}
                aria-pressed={subSkill === skill}
                className={`rounded-full border px-3 py-1.5 text-xs font-bold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${subSkill === skill ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
              >
                {skill}
              </button>
            ))}
          </fieldset>

          <label className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-muted">
            Category:
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="min-h-9 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">All</option>
              {categories.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </label>
        </div>

        <ul className="grid gap-3 md:grid-cols-2" aria-label="Common mistakes">
          {items.length === 0 ? (
            <li className="col-span-full"><p className="text-sm text-muted">No mistakes match the filter.</p></li>
          ) : null}
          {items.map((mistake) => (
            <li key={mistake.id}>
              <MistakeCard mistake={mistake} />
            </li>
          ))}
        </ul>

        <div className="flex justify-end">
          <Button asChild variant="outline">
            <Link href="/writing/common-mistakes/mine">See mistakes from your own letters</Link>
          </Button>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
