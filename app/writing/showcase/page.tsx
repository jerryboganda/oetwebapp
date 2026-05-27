'use client';

import { useEffect, useState } from 'react';
import { Sparkles, Filter } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { listShowcasePosts } from '@/lib/writing/api';
import type {
  WritingLetterType,
  WritingProfession,
  WritingShowcasePostDto,
} from '@/lib/writing/types';

const PROFESSIONS: Array<{ id: WritingProfession; label: string }> = [
  { id: 'medicine', label: 'Medicine' },
  { id: 'pharmacy', label: 'Pharmacy' },
  { id: 'nursing', label: 'Nursing' },
  { id: 'other', label: 'Other' },
];

const LETTER_TYPES: WritingLetterType[] = ['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-RP', 'LT-NM'];

export default function WritingShowcasePage() {
  const [posts, setPosts] = useState<WritingShowcasePostDto[]>([]);
  const [profession, setProfession] = useState<WritingProfession | null>(null);
  const [letterType, setLetterType] = useState<WritingLetterType | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    listShowcasePosts({
      profession: profession ?? undefined,
      letterType: letterType ?? undefined,
      pageSize: 30,
    })
      .then((r) => {
        if (cancelled) return;
        setPosts(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load showcase.');
      });
    return () => {
      cancelled = true;
    };
  }, [profession, letterType]);

  return (
    <LearnerDashboardShell pageTitle="Showcase">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Community"
          icon={Sparkles}
          accent="amber"
          title="Browse A-grade letters from other learners"
          description="Anonymised, opt-in showcase posts — see how peers solved the same letter types."
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <fieldset className="flex flex-wrap items-center gap-3 rounded-2xl border border-border bg-surface p-3 shadow-sm" aria-label="Filter showcase">
          <legend className="sr-only">Filter showcase</legend>
          <span className="text-xs font-bold uppercase tracking-wider text-muted">
            <Filter className="mr-1 inline h-3 w-3" aria-hidden="true" /> Profession:
          </span>
          <select
            value={profession ?? ''}
            onChange={(e) => setProfession((e.target.value || null) as WritingProfession | null)}
            className="min-h-9 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value="">All</option>
            {PROFESSIONS.map((p) => <option key={p.id} value={p.id}>{p.label}</option>)}
          </select>

          <span className="text-xs font-bold uppercase tracking-wider text-muted">Letter type:</span>
          <select
            value={letterType ?? ''}
            onChange={(e) => setLetterType((e.target.value || null) as WritingLetterType | null)}
            className="min-h-9 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value="">All</option>
            {LETTER_TYPES.map((lt) => <option key={lt} value={lt}>{lt}</option>)}
          </select>
        </fieldset>

        <ul className="grid gap-3 md:grid-cols-2" aria-label="Showcase posts">
          {posts.length === 0 ? (
            <li className="col-span-full"><p className="text-sm text-muted">No showcase posts yet for this filter.</p></li>
          ) : null}
          {posts.map((post) => (
            <li key={post.id}>
              <Card padding="md">
                <CardContent>
                  <header className="flex flex-wrap items-center justify-between gap-1">
                    <div className="flex flex-wrap items-center gap-1">
                      <Badge variant="success" size="sm">A grade</Badge>
                      <Badge variant="muted" size="sm">{post.letterType}</Badge>
                      <Badge variant="info" size="sm" className="capitalize">{post.profession}</Badge>
                    </div>
                    <span className="text-xs text-muted">{new Date(post.publishedAt).toLocaleDateString()}</span>
                  </header>
                  <pre className="mt-2 max-h-72 overflow-y-auto whitespace-pre-wrap rounded-lg border border-border bg-background p-3 text-xs leading-relaxed font-sans">
                    {post.anonymizedLetterContent}
                  </pre>
                  <footer className="mt-2 text-xs text-muted">{post.reactionCount} reactions</footer>
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
