'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, PlayCircle, Clock, Monitor, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { cn } from '@/lib/utils';
import { listWritingMocks, startWritingMock } from '@/lib/writing/api';
import type { WritingMockDto } from '@/lib/writing/types';

/** On-screen typed exam vs printable handwritten booklet. */
type Surface = 'computer' | 'paper';
/** Strict exam rules vs relaxed practice (spec §20.2). */
type Rigour = 'strict' | 'practice';

export default function WritingMocksCataloguePage() {
  const t = useTranslations();
  const router = useRouter();
  const [mocks, setMocks] = useState<WritingMockDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [starting, setStarting] = useState<string | null>(null);
  const [surface, setSurface] = useState<Surface>('computer');
  const [rigour, setRigour] = useState<Rigour>('strict');

  useEffect(() => {
    let cancelled = false;
    void listWritingMocks()
      .then((r) => {
        if (cancelled) return;
        setMocks(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.mocks.catalogue.error.load'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [t]);

  const start = async (mockId: string) => {
    setStarting(mockId);
    setError(null);
    try {
      const session = await startWritingMock({ mockId, isPractice: rigour === 'practice' });
      const id = encodeURIComponent(session.id);
      if (surface === 'paper') {
        // Paper mode opens the printable booklet session (owned elsewhere).
        router.push(`/writing/paper/session/${id}`);
      } else {
        // Computer mode; practice adds the relaxed flag (spellcheck on, no paste lock).
        router.push(
          rigour === 'practice'
            ? `/writing/mocks/session/${id}?practice=1`
            : `/writing/mocks/session/${id}`,
        );
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.mocks.catalogue.error.start'));
      setStarting(null);
    }
  };

  const ctaLabel =
    surface === 'paper'
      ? 'Open paper mode'
      : rigour === 'practice'
        ? 'Start practice'
        : 'Start strict mock';

  const optionClassName = (selected: boolean) => cn(
    'rounded-control border p-3 text-start transition-[color,background-color,border-color] duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed',
    selected
      ? 'border-primary bg-primary/5 ring-1 ring-primary/30'
      : 'border-border bg-surface hover:bg-background-light',
  );

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.mocks.catalogue.eyebrow')}
        icon={Award}
        accent="amber"
        title={t('writing.mocks.catalogue.title')}
        description={t('writing.mocks.catalogue.description')}
        highlights={[
          { icon: Award, label: t('writing.mocks.catalogue.highlights.available'), value: `${mocks.length}` },
          { icon: Clock, label: t('writing.mocks.catalogue.highlights.duration'), value: t('writing.mocks.catalogue.highlights.durationValue') },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <InlineAlert variant="warning" live="polite">
        <span className="font-bold">{t('writing.mocks.catalogue.before.title')}</span>{' '}
        {t('writing.mocks.catalogue.before.body')}
      </InlineAlert>

      {/* Mode selection — Computer vs Paper, Strict vs Practice (spec §10/§20.2). */}
      <MotionSection delayIndex={0}>
        <Card padding="md">
          <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
            <fieldset className="min-w-0">
              <legend className="mb-2 eyebrow text-muted">
                How would you like to sit it?
              </legend>
              <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Exam surface">
                {([
                  { value: 'computer' as const, label: 'Computer mode', hint: 'On-screen, timed, typed.', icon: Monitor },
                  { value: 'paper' as const, label: 'Paper mode', hint: 'Print & handwrite, then upload.', icon: FileText },
                ]).map((opt) => {
                  const selected = surface === opt.value;
                  const Icon = opt.icon;
                  return (
                    <button
                      key={opt.value}
                      type="button"
                      role="radio"
                      aria-checked={selected}
                      onClick={() => setSurface(opt.value)}
                      className={optionClassName(selected)}
                    >
                      <span className="flex items-center gap-2">
                        <Icon className={cn('h-4 w-4 shrink-0', selected ? 'text-primary' : 'text-muted')} aria-hidden="true" />
                        <span className={cn('text-sm font-bold', selected ? 'text-primary' : 'text-navy')}>{opt.label}</span>
                      </span>
                      <span className="mt-1 block text-xs text-muted">{opt.hint}</span>
                    </button>
                  );
                })}
              </div>
            </fieldset>

            <fieldset className={cn('min-w-0', surface === 'paper' && 'opacity-50')} aria-disabled={surface === 'paper'}>
              <legend className="mb-2 eyebrow text-muted">Conditions</legend>
              <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Exam conditions">
                {([
                  { value: 'strict' as const, label: 'Strict mock', hint: 'Exam rules: no paste, locked timing.' },
                  { value: 'practice' as const, label: 'Practice', hint: 'Relaxed: spellcheck on, no paste lock.' },
                ]).map((opt) => {
                  const selected = rigour === opt.value;
                  return (
                    <button
                      key={opt.value}
                      type="button"
                      role="radio"
                      aria-checked={selected}
                      disabled={surface === 'paper'}
                      onClick={() => setRigour(opt.value)}
                      className={optionClassName(selected)}
                    >
                      <span className={cn('block text-sm font-bold', selected ? 'text-primary' : 'text-navy')}>{opt.label}</span>
                      <span className="mt-1 block text-xs text-muted">{opt.hint}</span>
                    </button>
                  );
                })}
              </div>
              {surface === 'paper' ? (
                <p className="mt-2 text-xs text-muted">Conditions apply to computer mode only.</p>
              ) : null}
            </fieldset>
          </div>
        </Card>
      </MotionSection>

      {loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : mocks.length === 0 ? (
        error ? null : <EmptyState icon={<Award className="h-8 w-8" />} title={t('writing.mocks.catalogue.list.empty')} />
      ) : (
        <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label={t('writing.mocks.catalogue.list.label')}>
          {mocks.map((mock, index) => (
            <li key={mock.id}>
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                <Card padding="md" className="flex h-full flex-col" aria-label={t('writing.mocks.catalogue.cardAria', { title: mock.title })}>
                  <header className="flex flex-wrap items-center justify-between gap-2">
                    <Badge variant="info" size="sm">{t('writing.mocks.catalogue.badge.mock')}</Badge>
                    <Badge variant={mock.status === 'published' ? 'success' : 'muted'} size="sm">{mock.status}</Badge>
                  </header>
                  {/* Mock title is OET-authored English content. */}
                  <h2 className="mt-2 text-base font-bold text-navy" dir="ltr">{mock.title}</h2>
                  <div className="mt-auto flex flex-wrap gap-2 pt-3">
                    <Button
                      onClick={() => void start(mock.id)}
                      loading={starting === mock.id}
                      disabled={mock.status !== 'published'}
                      size="sm"
                    >
                      <PlayCircle className="h-4 w-4" aria-hidden="true" /> {ctaLabel}
                    </Button>
                    <Button asChild variant="outline" size="sm">
                      <Link href="/writing/stats">{t('writing.mocks.catalogue.readiness')}</Link>
                    </Button>
                  </div>
                </Card>
              </MotionItem>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
