'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Sparkles, Filter } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { listShowcasePosts } from '@/lib/writing/api';
import type {
  WritingLetterType,
  WritingProfession,
  WritingShowcasePostDto,
} from '@/lib/writing/types';

const PROFESSIONS: WritingProfession[] = ['medicine', 'pharmacy', 'nursing', 'other'];
const LETTER_TYPES: WritingLetterType[] = ['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-NM', 'LT-OT'];

export default function WritingShowcasePage() {
  const t = useTranslations();
  const [posts, setPosts] = useState<WritingShowcasePostDto[]>([]);
  const [profession, setProfession] = useState<WritingProfession | null>(null);
  const [letterType, setLetterType] = useState<WritingLetterType | null>(null);
  const [error, setError] = useState<string | null>(null);
  // The filter whose list last settled; anything else is still loading.
  const filterKey = `${profession ?? ''}|${letterType ?? ''}`;
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const loading = settledKey !== filterKey;

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
      })
      .finally(() => {
        if (!cancelled) setSettledKey(`${profession ?? ''}|${letterType ?? ''}`);
      });
    return () => {
      cancelled = true;
    };
  }, [profession, letterType, t]);

  const selectClassName = 'min-h-11 rounded-control border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary';

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.showcase.eyebrow')}
        icon={Sparkles}
        accent="amber"
        title={t('writing.showcase.title')}
        description={t('writing.showcase.description')}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Card padding="sm">
        <fieldset className="flex flex-wrap items-center gap-3" aria-label={t('writing.showcase.filters.legend')}>
          <legend className="sr-only">{t('writing.showcase.filters.legend')}</legend>
          <label className="flex items-center gap-2">
            <span className="eyebrow text-muted">
              <Filter className="me-1 inline h-3 w-3" aria-hidden="true" /> {t('writing.showcase.filters.professionLabel')}
            </span>
            <select
              value={profession ?? ''}
              onChange={(e) => setProfession((e.target.value || null) as WritingProfession | null)}
              className={selectClassName}
            >
              <option value="">{t('writing.showcase.filters.all')}</option>
              {PROFESSIONS.map((p) => <option key={p} value={p}>{t(`writing.practice.library.profession.${p}`)}</option>)}
            </select>
          </label>

          <label className="flex items-center gap-2">
            <span className="eyebrow text-muted">{t('writing.showcase.filters.letterTypeLabel')}</span>
            <select
              value={letterType ?? ''}
              onChange={(e) => setLetterType((e.target.value || null) as WritingLetterType | null)}
              className={selectClassName}
            >
              <option value="">{t('writing.showcase.filters.all')}</option>
              {LETTER_TYPES.map((lt) => <option key={lt} value={lt}>{lt}</option>)}
            </select>
          </label>
        </fieldset>
      </Card>

      {loading && posts.length === 0 ? (
        <LearnerSkeleton variant="card-grid" />
      ) : posts.length === 0 ? (
        error ? null : <EmptyState icon={<Sparkles className="h-8 w-8" />} title={t('writing.showcase.list.empty')} />
      ) : (
        <ul className="grid grid-cols-1 gap-3 md:grid-cols-2" aria-label={t('writing.showcase.list.label')} aria-busy={loading}>
          {posts.map((post, index) => (
            <li key={post.id} className="min-w-0">
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                <Card padding="md" className="h-full">
                  <header className="flex flex-wrap items-center justify-between gap-1">
                    <div className="flex flex-wrap items-center gap-1">
                      <Badge variant="success" size="sm">{t('writing.showcase.list.aGrade')}</Badge>
                      <Badge variant="muted" size="sm">{post.letterType}</Badge>
                      <Badge variant="info" size="sm" className="capitalize">{post.profession}</Badge>
                    </div>
                    <span className="text-xs tabular-nums text-muted">{new Date(post.publishedAt).toLocaleDateString()}</span>
                  </header>
                  {/* The anonymised letter is learner-authored English content. */}
                  <pre className="mt-2 max-h-72 overflow-y-auto whitespace-pre-wrap break-words rounded-control border border-border bg-background p-3 font-sans text-xs leading-relaxed" dir="ltr">
                    {post.anonymizedLetterContent}
                  </pre>
                  <footer className="mt-2 text-xs tabular-nums text-muted">{t('writing.showcase.list.reactions', { count: post.reactionCount })}</footer>
                </Card>
              </MotionItem>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
