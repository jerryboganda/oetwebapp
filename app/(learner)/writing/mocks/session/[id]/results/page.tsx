'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, Clock, FileText, TrendingUp } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, cardClassName } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { CriterionScoreRow } from '@/components/domain/results/criterion-score-row';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import { BandHistoryChart } from '@/components/domain/writing/BandHistoryChart';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import { WritingReleaseCountdown, releasePollDelayMs } from '@/components/domain/writing/WritingReleaseCountdown';
import { WritingStimulusViewer } from '@/components/domain/writing/WritingStimulusViewer';
import { getWritingAnswerSheet, getWritingMockResults, getWritingStatsBands } from '@/lib/writing/api';
import { candidatePriorities } from '@/lib/writing/priorities';
import { WRITING_RAW_MAX } from '@/lib/scoring';
import { cn } from '@/lib/utils';
import type {
  WritingBandHistoryPointDto,
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingGradeDto,
  WritingMockResultsDto,
  WritingMockSessionDto,
} from '@/lib/writing/types';

/** The page keeps watching a queued/grading/held mock for up to an hour before it stops polling. */
const RESULT_WATCH_LIMIT_MS = 60 * 60 * 1000;

const CRITERION_NAMES: Record<WritingCriterionCode, string> = {
  c1: 'Purpose',
  c2: 'Content',
  c3: 'Conciseness & Clarity',
  c4: 'Genre & Style',
  c5: 'Organisation & Layout',
  c6: 'Language Accuracy',
};

// C1 Purpose is out of 3, the rest out of 7; targets mirror the radar overlay.
const CRITERION_MAX: Record<WritingCriterionCode, number> = { c1: 3, c2: 7, c3: 7, c4: 7, c5: 7, c6: 7 };
const CRITERION_TARGET: Record<WritingCriterionCode, number> = { c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 };

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
  const [status, setStatus] = useState<string>('graded');
  // 15-minute release window as the server last reported it (absent from an old API).
  const [release, setRelease] = useState<Pick<WritingMockResultsDto, 'releaseState' | 'releaseAt' | 'serverNow'>>({});
  const [bandHistory, setBandHistory] = useState<WritingBandHistoryPointDto[]>([]);
  const [answerSheetPath, setAnswerSheetPath] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    let attempts = 0;
    let timer: ReturnType<typeof setTimeout> | null = null;
    const startedAt = Date.now();

    const load = () => {
      attempts++;
      void Promise.all([getWritingMockResults(sessionId), getWritingStatsBands().catch(() => null)])
        .then(([r, b]) => {
          if (cancelled) return;
          setSession(r.session);
          setGrade(r.grade);
          setRelease({ releaseState: r.releaseState, releaseAt: r.releaseAt, serverNow: r.serverNow });
          // Answer-sheet PDF, resolved from the submission behind this session.
          if (r.session.submissionId) {
            void getWritingAnswerSheet(r.session.submissionId)
              .then((a) => {
                if (!cancelled) setAnswerSheetPath(a.answerSheetPdfDownloadPath ?? null);
              })
              .catch(() => {});
          }
          const nextStatus = r.status ?? (r.grade ? 'graded' : 'awaiting_review');
          setStatus(nextStatus);
          if (b) setBandHistory(b.history);
          setError(null);
          // Keep polling until AI grading finishes and the result is released
          // (queued/preflight/grading, a held result reads 'grading'), and while
          // awaiting the examiner's band (human-marked path). Release-aware pace:
          // sparse while a finished result is held, landing just after its release time.
          const stillInProgress = !r.grade && ['queued', 'preflight', 'grading', 'awaiting_review'].includes(nextStatus);
          // A release window (up to 15 minutes plus grading) outlasts the old 10-minute cap.
          const withinWatch = r.releaseAt ? Date.now() - startedAt < RESULT_WATCH_LIMIT_MS : attempts < 120;
          if (stillInProgress && withinWatch) {
            timer = setTimeout(load, releasePollDelayMs(r, 5000));
          }
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
  const priorities = candidatePriorities(grade?.topThreePriorities);

  // Not yet released: the server holds the result for 15 minutes after submission.
  // A failed or cancelled mock has nothing to wait for, and an old API (no
  // releaseState) keeps the legacy cards below.
  const awaitingRelease =
    !grade && !!release.releaseState && release.releaseState !== 'released' && status !== 'failed' && status !== 'cancelled';

  const criteriaScoreLabel = t('writing.mocks.results.highlights.criteriaScore');
  const sectionCard = cardClassName({ padding: 'lg' });

  return (
    <>
      {grade ? (
        <ResultsScorePanel
          eyebrow={t('writing.mocks.results.eyebrow', { n: Math.max(1, mockNumber) })}
          icon={Award}
          title={t('writing.mocks.results.heroTitle', { band: grade.bandLabel })}
          subtitle={t('writing.mocks.results.description')}
          // The ring fills on the raw /38 scale its label shows: estimatedBand is
          // stored in raw-total units, never a 0–7 band.
          gaugeValue={(grade.rawTotal / WRITING_RAW_MAX) * 100}
          gaugeCenter={<span className="text-2xl font-black text-navy">{grade.bandLabel}</span>}
          gaugeLabel={`${criteriaScoreLabel} ${grade.rawTotal}/38`}
          // Neutral: raw marks have no inline pass mark (Writing is country-aware), and
          // the band label beside the ring already carries the result.
          gaugeColor="var(--color-primary)"
          stats={[
            { label: criteriaScoreLabel, value: `${grade.rawTotal}/38`, tone: 'info', icon: <Award /> },
            {
              label: t('writing.mocks.results.highlights.delta'),
              // Whole raw marks out of 38, not a one-decimal band.
              value: delta === null ? t('writing.mocks.results.highlights.firstMock') : `${delta > 0 ? '+' : ''}${Math.round(delta)}`,
              tone: delta != null && delta > 0 ? 'success' : delta != null && delta < 0 ? 'danger' : 'default',
              icon: <TrendingUp />,
            },
          ]}
        />
      ) : (
        <LearnerPageHero
          eyebrow={t('writing.mocks.results.eyebrow', { n: Math.max(1, mockNumber) })}
          icon={Award}
          accent="writing"
          title={t('writing.mocks.results.heroTitleFallback')}
          description={t('writing.mocks.results.description')}
        />
      )}

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {!session && !error ? <LearnerSkeleton variant="list" /> : null}

      {awaitingRelease ? (
        <Card padding="md" data-testid="writing-mock-release-pending">
          <div className="flex items-start gap-3">
            <Clock className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden />
            <div className="min-w-0">
              {release.releaseAt ? (
                <WritingReleaseCountdown
                  releaseAt={release.releaseAt}
                  serverNow={release.serverNow}
                  releaseState={release.releaseState}
                />
              ) : (
                <p className="text-sm text-muted">{t('writing.release.finalising')}</p>
              )}
            </div>
          </div>
        </Card>
      ) : null}

      {!grade && !release.releaseState && status === 'awaiting_review' ? (
        <Card padding="md">
          <div className="flex items-start gap-3">
            <Clock className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden />
            <div className="min-w-0">
              <h2 className="text-base font-bold text-navy">{t('writing.mocks.results.awaiting.title')}</h2>
              <p className="mt-1 text-sm text-muted">{t('writing.mocks.results.awaiting.body')}</p>
            </div>
          </div>
        </Card>
      ) : null}

      {scores ? (
        <MotionSection delayIndex={0}>
          <section className={cn(sectionCard, 'grid grid-cols-1 gap-6 lg:grid-cols-2')}>
            <div className="min-w-0">
              <h2 className="text-lg font-bold text-navy">{t('writing.mocks.results.criteria.heading')}</h2>
              <CriteriaRadar scores={scores} targetScores={{ c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 }} />
              <div className="mt-4 space-y-2">
                {(Object.keys(CRITERION_NAMES) as WritingCriterionCode[]).map((code, index) => (
                  <MotionItem key={code} delayIndex={Math.min(index, 5)}>
                    <CriterionScoreRow
                      label={CRITERION_NAMES[code]}
                      score={scores![code]}
                      max={CRITERION_MAX[code]}
                      target={CRITERION_TARGET[code]}
                    />
                  </MotionItem>
                ))}
              </div>
            </div>
            <div className="min-w-0">
              <h2 className="text-lg font-bold text-navy">{t('writing.mocks.results.trajectory.heading')}</h2>
              <BandHistoryChart data={mockHistory.map((p) => ({ date: p.date, rawTotal: p.rawTotal, estimatedBand: p.estimatedBand, letterType: p.letterType }))} />
            </div>
          </section>
        </MotionSection>
      ) : null}

      {/* Answer Sheet PDF — official answer to tally the letter against (read-only). */}
      {answerSheetPath ? (
        <section aria-labelledby="answer-sheet-heading" className={sectionCard}>
          <h2 id="answer-sheet-heading" className="flex items-center gap-1.5 text-lg font-bold text-navy">
            <FileText className="h-5 w-5 text-primary" aria-hidden="true" /> Answer sheet
          </h2>
          <p className="mt-1 text-sm text-muted">Tally your letter against the official answer sheet.</p>
          <div className="mt-3 h-[75vh] overflow-hidden rounded-xl border border-border">
            <WritingStimulusViewer downloadPath={answerSheetPath} title="Answer Sheet" />
          </div>
        </section>
      ) : null}

      {grade?.canonViolations?.length ? (
        <MotionSection>
          <section aria-labelledby="canon-heading" className={sectionCard}>
            <h2 id="canon-heading" className="text-lg font-bold text-navy">{t('writing.mocks.results.canon.heading')}</h2>
            <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-2">
              {grade.canonViolations.map((v) => (
                <CanonViolationCard key={v.id} violation={v} />
              ))}
            </div>
          </section>
        </MotionSection>
      ) : null}

      {priorities.length ? (
        <MotionSection>
          <section className={sectionCard}>
            <h2 className="text-lg font-bold text-navy">{t('writing.mocks.results.priorities.heading')}</h2>
            <ol className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-3">
              {priorities.map((priority, idx) => (
                <li key={idx} className="min-w-0">
                  <MotionItem delayIndex={Math.min(idx, 5)} className="h-full rounded-xl bg-background-light p-3">
                    <Badge variant="warning" size="sm" className="tabular-nums">#{idx + 1}</Badge>
                    {/* Priorities are AI-generated English content. */}
                    <p className="mt-2 text-sm text-navy" dir="ltr">{priority}</p>
                  </MotionItem>
                </li>
              ))}
            </ol>
          </section>
        </MotionSection>
      ) : null}

      <section className={cn(sectionCard, 'flex flex-wrap items-center justify-between gap-3')}>
        <div className="min-w-0">
          <h2 className="text-lg font-bold text-navy">{t('writing.mocks.results.next.heading')}</h2>
          <p className="mt-1 text-sm text-muted">{t('writing.mocks.results.next.description')}</p>
        </div>
        <div className="flex flex-wrap gap-2">
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
    </>
  );
}
