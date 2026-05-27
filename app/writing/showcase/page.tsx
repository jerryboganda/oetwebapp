'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
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

const PROFESSIONS: WritingProfession[] = ['medicine', 'pharmacy', 'nursing', 'other'];
const LETTER_TYPES: WritingLetterType[] = ['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-RP', 'LT-NM'];

export default function WritingShowcasePage() {
  const t = useTranslations();
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
        setError(err instanceof Error ? err.message : t('writing.showcase.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [profession, letterType, t]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.showcase.pageTitle')}>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={t('writing.showcase.eyebrow')}
          icon={Sparkles}
          accent="amber"
          title={t('writing.showcase.title')}
          description={t('writing.showcase.description')}
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <fieldset className="flex flex-wrap items-center gap-3 rounded-2xl border border-border bg-surface p-3 shadow-sm" aria-label={t('writing.showcase.filters.legend')}>
          <legend className="sr-only">{t('writing.showcase.filters.legend')}</legend>
          <span className="text-xs font-bold uppercase tracking-wider text-muted">
            <Filter className="mr-1 inline h-3 w-3" aria-hidden="true" /> {t('writing.showcase.filters.professionLabel')}
          </span>
          <select
            value={profession ?? ''}
            onChange={(e) => setProfession((e.target.value || null) as WritingProfession | null)}
            className="min-h-9 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value="">{t('writing.showcase.filters.all')}</option>
            {PROFESSIONS.map((p) => <option key={p} value={p}>{t(`writing.practice.library.profession.${p}`)}</option>)}
          </select>

          <span className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.showcase.filters.letterTypeLabel')}</span>
          <select
            value={letterType ?? ''}
            onChange={(e) => setLetterType((e.target.value || null) as WritingLetterType | null)}
            className="min-h-9 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value="">{t('writing.showcase.filters.all')}</option>
            {LETTER_TYPES.map((lt) => <option key={lt} value={lt}>{lt}</option>)}
          </select>
        </fieldset>

        <ul className="grid gap-3 md:grid-cols-2" aria-label={t('writing.showcase.list.label')}>
          {posts.length === 0 ? (
            <li className="col-span-full"><p className="text-sm text-muted">{t('writing.showcase.list.empty')}</p></li>
          ) : null}
          {posts.map((post) => (
            <li key={post.id}>
              <Card padding="md">
                <CardContent>
                  <header className="flex flex-wrap items-center justify-between gap-1">
                    <div className="flex flex-wrap items-center gap-1">
                      <Badge variant="success" size="sm">{t('writing.showcase.list.aGrade')}</Badge>
                      <Badge variant="muted" size="sm">{post.letterType}</Badge>
                      <Badge variant="info" size="sm" className="capitalize">{post.profession}</Badge>
                    </div>
                    <span className="text-xs text-muted">{new Date(post.publishedAt).toLocaleDateString()}</span>
                  </header>
                  {/* The anonymised letter is learner-authored English content. */}
                  <pre className="mt-2 max-h-72 overflow-y-auto whitespace-pre-wrap rounded-lg border border-border bg-background p-3 text-xs leading-relaxed font-sans" dir="ltr">
                    {post.anonymizedLetterContent}
                  </pre>
                  <footer className="mt-2 text-xs text-muted">{t('writing.showcase.list.reactions', { count: post.reactionCount })}</footer>
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
