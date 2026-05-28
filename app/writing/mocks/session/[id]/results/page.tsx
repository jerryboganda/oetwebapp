'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, TrendingUp } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import { BandHistoryChart } from '@/components/domain/writing/BandHistoryChart';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import { getWritingMockResults, getWritingStatsBands } from '@/lib/writing/api';
import type {
  WritingBandHistoryPointDto,
  WritingCriteriaScoresDto,
  WritingGradeDto,
  WritingMockSessionDto,
} from '@/lib/writing/types';

function gradeToScores(g: WritingGradeDto): WritingCriteriaScoresDto {
  return {
    c1: g.c1Purpose,
    c2: g.c2Content,
    c3: g.c3Conciseness,
    c4: g.c4Genre,
    c5: g.c5Organisation,
    c6: g.c6Language,
  };
}

export default function WritingMockResultsPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const sessionId = String(params?.id ?? '');
  const [session, setSession] = useState<WritingMockSessionDto | null>(null);
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [bandHistory, setBandHistory] = useState<WritingBandHistoryPointDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    let attempts = 0;
    let timer: ReturnType<typeof setTimeout> | null = null;

    const load = () => {
      attempts++;
      void Promise.all([getWritingMockResults(sessionId), getWritingStatsBands().catch(() => null)])
        .then(([r, b]) => {
          if (cancelled) return;
          setSession(r.session);
          setGrade(r.grade);
          if (b) setBandHistory(b.history);
          setError(null);
        })
        .catch((err) => {
          if (cancelled) return;
          if (attempts < 15) {
            setError(t('writing.mocks.results.statusGrading'));
            timer = setTimeout(load, 2000);
            return;
          }
          setError(err instanceof Error ? err.message : t('writing.mocks.results.error.load'));
        });
    };

    load();

    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [sessionId, t]);

  const mockHistory = bandHistory.filter((p) => !p.isRevision);
  const mockNumber = mockHistory.length;
  const previous = mockNumber > 1 ? mockHistory[mockHistory.length - 2] : null;
  const current = mockHistory[mockHistory.length - 1];
  const delta = current && previous ? current.estimatedBand - previous.estimatedBand : null;

  const scores = grade ? gradeToScores(grade) : null;

  return (
    <LearnerDashboardShell pageTitle={t('writing.mocks.results.pageTitle')}>
      <div className="space-y-6" aria-busy={!grade}>
        <LearnerPageHero
          eyebrow={t('writing.mocks.results.eyebrow', { n: Math.max(1, mockNumber) })}
          icon={Award}
          accent="amber"
          title={grade?.bandLabel ? t('writing.mocks.results.heroTitle', { band: grade.bandLabel }) : t('writing.mocks.results.heroTitleFallback')}
          description={t('writing.mocks.results.description')}
          highlights={[
            { icon: Award, label: t('writing.mocks.results.highlights.raw'), value: grade ? `${grade.rawTotal}/38` : '—' },
            { icon: TrendingUp, label: t('writing.mocks.results.highlights.delta'), value: delta === null ? t('writing.mocks.results.highlights.firstMock') : (delta > 0 ? `+${delta.toFixed(1)}` : delta.toFixed(1)) },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {scores ? (
          <section className="grid gap-4 rounded-2xl border border-border bg-surface p-5 shadow-sm lg:grid-cols-2">
            <div>
              <h2 className="text-lg font-bold text-navy">{t('writing.mocks.results.criteria.heading')}</h2>
              <CriteriaRadar scores={scores} targetScores={{ c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 }} />
            </div>
            <div>
              <h2 className="text-lg font-bold text-navy">{t('writing.mocks.results.trajectory.heading')}</h2>
              <BandHistoryChart data={mockHistory.map((p) => ({ date: p.date, rawTotal: p.rawTotal, estimatedBand: p.estimatedBand, letterType: p.letterType }))} />
            </div>
          </section>
        ) : null}

        {grade?.canonViolations?.length ? (
          <section aria-labelledby="canon-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="canon-heading" className="text-lg font-bold text-navy">{t('writing.mocks.results.canon.heading')}</h2>
            <div className="mt-3 grid gap-2 md:grid-cols-2">
              {grade.canonViolations.map((v) => (
                <CanonViolationCard key={v.id} violation={v} />
              ))}
            </div>
          </section>
        ) : null}

        {grade?.topThreePriorities?.length ? (
          <Card padding="md">
            <CardContent>
              <h2 className="text-base font-bold text-navy">{t('writing.mocks.results.priorities.heading')}</h2>
              <ol className="mt-3 grid gap-2 md:grid-cols-3">
                {grade.topThreePriorities.map((priority, idx) => (
                  <li key={idx} className="rounded-xl border border-border bg-background p-3">
                    <Badge variant="warning" size="sm">#{idx + 1}</Badge>
                    {/* Priorities are AI-generated English content. */}
                    <p className="mt-2 text-sm text-navy" dir="ltr">{priority}</p>
                  </li>
                ))}
              </ol>
            </CardContent>
          </Card>
        ) : null}

        <section className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <div>
            <h2 className="text-base font-bold text-navy">{t('writing.mocks.results.next.heading')}</h2>
            <p className="mt-1 text-sm text-muted">{t('writing.mocks.results.next.description')}</p>
          </div>
          <div className="flex gap-2">
            <Button asChild variant="outline">
              <Link href="/writing/mocks">{t('writing.mocks.results.back')}</Link>
            </Button>
            <Button asChild>
              <Link href={session?.submissionId ? `/writing/submissions/${encodeURIComponent(session.submissionId)}/results` : '/writing/today'}>
                {t('writing.mocks.results.openSubmission')}
              </Link>
            </Button>
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
