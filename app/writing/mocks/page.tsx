'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, PlayCircle, Clock, AlertTriangle, Monitor, FileText } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
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
        // Computer mode; practice adds the relaxed flag (scratchpad, no paste lock).
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

  return (
    <LearnerDashboardShell pageTitle={t('writing.mocks.catalogue.pageTitle')}>
      <div className="space-y-6">
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

        <Card padding="md" className="border-amber-300/70 bg-amber-50/40">
          <CardContent>
            <p className="flex items-start gap-2 text-sm text-amber-900">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span>
                <span className="font-bold">{t('writing.mocks.catalogue.before.title')}</span>{' '}
                {t('writing.mocks.catalogue.before.body')}
              </span>
            </p>
          </CardContent>
        </Card>

        {/* Mode selection — Computer vs Paper, Strict vs Practice (spec §10/§20.2). */}
        <Card padding="md">
          <CardContent>
            <div className="grid gap-5 sm:grid-cols-2">
              <fieldset>
                <legend className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">
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
                        className={cn(
                          'rounded-xl border p-3 text-left transition-[color,background-color,border-color] duration-150',
                          selected
                            ? 'border-primary bg-primary/5 ring-1 ring-primary/30'
                            : 'border-border bg-surface hover:bg-background-light',
                        )}
                      >
                        <span className="flex items-center gap-2">
                          <Icon className={cn('h-4 w-4', selected ? 'text-primary' : 'text-muted')} aria-hidden="true" />
                          <span className={cn('text-sm font-bold', selected ? 'text-primary' : 'text-navy')}>{opt.label}</span>
                        </span>
                        <span className="mt-1 block text-xs text-muted">{opt.hint}</span>
                      </button>
                    );
                  })}
                </div>
              </fieldset>

              <fieldset className={cn(surface === 'paper' && 'opacity-50')} aria-disabled={surface === 'paper'}>
                <legend className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">Conditions</legend>
                <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Exam conditions">
                  {([
                    { value: 'strict' as const, label: 'Strict mock', hint: 'Exam rules: no paste, locked timing.' },
                    { value: 'practice' as const, label: 'Practice', hint: 'Relaxed: scratchpad, no paste lock.' },
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
                        className={cn(
                          'rounded-xl border p-3 text-left transition-[color,background-color,border-color] duration-150 disabled:cursor-not-allowed',
                          selected
                            ? 'border-primary bg-primary/5 ring-1 ring-primary/30'
                            : 'border-border bg-surface hover:bg-background-light',
                        )}
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
          </CardContent>
        </Card>

        <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label={t('writing.mocks.catalogue.list.label')}>
          {mocks.length === 0 ? (
            <li className="col-span-full"><p className="text-sm text-muted">{t('writing.mocks.catalogue.list.empty')}</p></li>
          ) : null}
          {mocks.map((mock) => (
            <li key={mock.id}>
              <Card padding="md" aria-label={t('writing.mocks.catalogue.cardAria', { title: mock.title })}>
                <CardContent>
                  <header className="flex flex-wrap items-center justify-between gap-2">
                    <Badge variant="info" size="sm">{t('writing.mocks.catalogue.badge.mock')}</Badge>
                    <Badge variant={mock.status === 'published' ? 'success' : 'muted'} size="sm">{mock.status}</Badge>
                  </header>
                  {/* Mock title is OET-authored English content. */}
                  <h2 className="mt-2 text-base font-bold text-navy" dir="ltr">{mock.title}</h2>
                  <div className="mt-3 flex flex-wrap gap-2">
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
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
