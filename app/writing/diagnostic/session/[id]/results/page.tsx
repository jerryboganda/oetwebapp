'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, ArrowRight, Route, Sparkles } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import { ExemplarSideBySide } from '@/components/domain/writing/ExemplarSideBySide';
import {
  getClosestExemplar,
  getWritingDiagnosticResults,
  getWritingSubmission,
  type WritingDiagnosticResultsDto,
} from '@/lib/writing/api';
import type {
  WritingExemplarDto,
  WritingSubmissionDto,
  WritingCriteriaScoresDto,
  WritingCriterionCode,
} from '@/lib/writing/types';

const CRITERION_NAMES: Record<WritingCriterionCode, string> = {
  c1: 'C1 Purpose',
  c2: 'C2 Content',
  c3: 'C3 Conciseness & Clarity',
  c4: 'C4 Genre & Style',
  c5: 'C5 Organisation & Layout',
  c6: 'C6 Language Accuracy',
};

function gradeToCriteriaScores(g: WritingDiagnosticResultsDto['grade']): WritingCriteriaScoresDto {
  return {
    c1: g.c1Purpose,
    c2: g.c2Content,
    c3: g.c3Conciseness,
    c4: g.c4Genre,
    c5: g.c5Organisation,
    c6: g.c6Language,
  };
}

export default function WritingDiagnosticResultsPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const sessionId = String(params?.id ?? '');

  const [data, setData] = useState<WritingDiagnosticResultsDto | null>(null);
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [exemplar, setExemplar] = useState<WritingExemplarDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!sessionId) return;
    void getWritingDiagnosticResults(sessionId)
      .then(async (res) => {
        setData(res);
        if (res.submissionId) {
          const sub = await getWritingSubmission(res.submissionId).catch(() => null);
          if (sub) setSubmission(sub);
          if (sub?.scenarioId) {
            const ex = await getClosestExemplar(sub.scenarioId).catch(() => null);
            if (ex) setExemplar(ex);
          }
        }
      })
      .catch((err) => setError(err instanceof Error ? err.message : t('writing.diagnostic.results.error.load')));
  }, [sessionId, t]);

  const grade = data?.grade;
  const scores = grade ? gradeToCriteriaScores(grade) : null;

  return (
    <LearnerDashboardShell pageTitle={t('writing.diagnostic.results.pageTitle')}>
      <div className="space-y-6" aria-busy={!data}>
        <LearnerPageHero
          eyebrow={t('writing.diagnostic.results.eyebrow')}
          icon={Award}
          accent="amber"
          title={grade?.bandLabel ? t('writing.diagnostic.results.estimatedBand', { band: grade.bandLabel }) : t('writing.diagnostic.results.title')}
          description={t('writing.diagnostic.results.hero.description')}
          highlights={grade ? [
            { icon: Award, label: t('writing.diagnostic.results.highlights.raw'), value: `${grade.rawTotal}/38` },
            { icon: Sparkles, label: t('writing.diagnostic.results.highlights.confidence'), value: grade.confidenceFlag },
            { icon: Route, label: t('writing.diagnostic.results.highlights.pathway'), value: data?.pathwayPreview.currentStage ?? '-' },
          ] : []}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {scores ? (
          <section
            aria-labelledby="criteria-heading"
            className="grid gap-4 rounded-2xl border border-border bg-surface p-5 shadow-sm lg:grid-cols-2"
          >
            <div>
              <h2 id="criteria-heading" className="text-lg font-bold text-navy">{t('writing.diagnostic.results.criteria.heading')}</h2>
              <p className="mt-1 text-sm text-muted">{t('writing.diagnostic.results.criteria.subtitle')}</p>
              <CriteriaRadar scores={scores} targetScores={{ c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 }} />
            </div>
            <details className="rounded-xl border border-border bg-background p-4" open>
              <summary className="cursor-pointer text-sm font-bold text-navy">{t('writing.diagnostic.results.criteria.perCriterion')}</summary>
              <ul className="mt-3 space-y-3">
                {(Object.entries(grade!.perCriterion) as Array<[WritingCriterionCode, NonNullable<typeof grade>['perCriterion'][WritingCriterionCode]]>).map(([code, feedback]) => (
                  <li key={code} className="rounded-lg border border-border bg-surface p-3">
                    <div className="flex items-center justify-between gap-2">
                      {/*
                        Criterion names (C1 Purpose, etc.) stay English — they
                        mirror the live OET rubric labels and are referenced
                        verbatim across the platform; spec §32.
                      */}
                      <h3 className="text-sm font-bold text-navy">{CRITERION_NAMES[code]}</h3>
                      <Badge variant="info" size="sm">{feedback.score}</Badge>
                    </div>
                    {/*
                      AI-generated per-criterion feedback + exemplarFix is
                      always English (Phase 11 ships paid AR translation).
                    */}
                    <p className="mt-1 text-xs text-muted leading-snug" dir="ltr">{feedback.feedback}</p>
                    {feedback.exemplarFix ? (
                      <p className="mt-1 rounded bg-emerald-50 p-2 text-xs text-emerald-800" dir="ltr">
                        <span className="font-bold">{t('writing.diagnostic.results.criteria.exemplarFix')}</span> {feedback.exemplarFix}
                      </p>
                    ) : null}
                    {feedback.citedRuleIds.length > 0 ? (
                      <div className="mt-2 flex flex-wrap gap-1">
                        {feedback.citedRuleIds.map((id) => (
                          <Link key={id} href={`/writing/canon/${encodeURIComponent(id)}`} className="text-[10px] font-bold text-primary underline">
                            {id}
                          </Link>
                        ))}
                      </div>
                    ) : null}
                  </li>
                ))}
              </ul>
            </details>
          </section>
        ) : null}

        {grade?.topThreePriorities?.length ? (
          <section aria-labelledby="priorities-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="priorities-heading" className="text-lg font-bold text-navy">{t('writing.diagnostic.results.priorities.heading')}</h2>
            <ol className="mt-3 grid gap-2 md:grid-cols-3">
              {grade.topThreePriorities.map((priority, idx) => (
                <li key={idx} className="rounded-xl border border-border bg-background p-3">
                  <Badge variant="warning" size="sm">#{idx + 1}</Badge>
                  {/* Priorities are AI-generated English feedback (Phase 11). */}
                  <p className="mt-2 text-sm text-navy" dir="ltr">{priority}</p>
                </li>
              ))}
            </ol>
          </section>
        ) : null}

        {grade?.canonViolations?.length ? (
          <section aria-labelledby="canon-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="canon-heading" className="text-lg font-bold text-navy">{t('writing.diagnostic.results.canon.heading', { count: grade.canonViolations.length })}</h2>
            <p className="mt-1 text-sm text-muted">{t('writing.diagnostic.results.canon.subtitle')}</p>
            <div className="mt-3 grid gap-2 md:grid-cols-2">
              {grade.canonViolations.map((v) => (
                <CanonViolationCard key={v.id} violation={v} />
              ))}
            </div>
          </section>
        ) : null}

        {submission && exemplar ? (
          <section aria-labelledby="exemplar-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="exemplar-heading" className="text-lg font-bold text-navy">{t('writing.diagnostic.results.exemplar.heading')}</h2>
            <p className="mt-1 text-sm text-muted">{t('writing.diagnostic.results.exemplar.subtitle')}</p>
            <ExemplarSideBySide
              candidateLetter={submission.letterContent}
              exemplarLetter={exemplar.letterContent}
              exemplarAnnotations={exemplar.annotations}
              className="mt-3"
            />
          </section>
        ) : null}

        {data?.pathwayPreview ? (
          <section aria-labelledby="pathway-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="pathway-heading" className="text-lg font-bold text-navy">{t('writing.diagnostic.results.pathway.heading')}</h2>
            <Card padding="md" className="mt-3">
              <CardContent>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="text-sm text-navy">
                    Stage <span className="font-bold capitalize">{data.pathwayPreview.currentStage}</span>
                    {' · '} Week {data.pathwayPreview.currentWeek}/{data.pathwayPreview.totalWeeks}
                    {data.pathwayPreview.predictedBand ? ` · Predicted band ${data.pathwayPreview.predictedBand}` : ''}
                  </p>
                  <Badge variant="info" size="sm">{t('writing.diagnostic.results.pathway.queued', { count: data.pathwayPreview.items.length })}</Badge>
                </div>
              </CardContent>
            </Card>
          </section>
        ) : null}

        <section className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <div>
            <h2 className="text-base font-bold text-navy">{t('writing.diagnostic.results.cta.heading')}</h2>
            <p className="mt-1 text-sm text-muted">{t('writing.diagnostic.results.cta.subtitle')}</p>
          </div>
          <Button onClick={() => router.push('/writing/today')}>
            {t('writing.diagnostic.results.startPathway')} <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Button>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
