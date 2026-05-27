'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Award, FileText, Flag, RefreshCw, Share2, Sparkles, UserRoundCheck } from 'lucide-react';
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
  appealWritingSubmission,
  disputeWritingCanonViolation,
  getClosestExemplar,
  getWritingSubmission,
  getWritingSubmissionGrade,
  publishToShowcase,
  requestTutorReview,
} from '@/lib/writing/api';
import type {
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingExemplarDto,
  WritingGradeDto,
  WritingSubmissionDto,
} from '@/lib/writing/types';

const CRITERION_NAMES: Record<WritingCriterionCode, string> = {
  c1: 'C1 Purpose',
  c2: 'C2 Content',
  c3: 'C3 Conciseness & Clarity',
  c4: 'C4 Genre & Style',
  c5: 'C5 Organisation & Layout',
  c6: 'C6 Language Accuracy',
};

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

export default function WritingSubmissionResultsPage() {
  const params = useParams<{ id: string }>();
  const submissionId = String(params?.id ?? '');

  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [exemplar, setExemplar] = useState<WritingExemplarDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionStatus, setActionStatus] = useState<string | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    void Promise.all([
      getWritingSubmission(submissionId),
      getWritingSubmissionGrade(submissionId),
    ])
      .then(async ([sub, g]) => {
        setSubmission(sub);
        setGrade(g);
        if (sub.scenarioId) {
          const ex = await getClosestExemplar(sub.scenarioId).catch(() => null);
          if (ex) setExemplar(ex);
        }
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load results.'));
  }, [submissionId]);

  const onTutorReview = useCallback(async () => {
    if (!submissionId) return;
    setActionStatus('Requesting tutor review…');
    try {
      await requestTutorReview(submissionId, { priority: 'standard' });
      setActionStatus('Tutor review requested. You will be notified when a tutor returns it.');
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : 'Could not request tutor review.');
    }
  }, [submissionId]);

  const onAppeal = useCallback(async () => {
    if (!submissionId) return;
    const reason = window.prompt(
      "Briefly explain why you believe the grade is wrong (20+ characters). We'll re-grade with a higher-cost model.",
      '',
    );
    if (!reason || reason.trim().length < 20) {
      setActionStatus('Appeal cancelled — at least 20 characters required.');
      return;
    }
    setActionStatus('Submitting appeal…');
    try {
      await appealWritingSubmission(submissionId, { reason: reason.trim() });
      setActionStatus('Appeal submitted. Result will update once reviewed.');
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : 'Could not submit appeal.');
    }
  }, [submissionId]);

  const onShowcase = useCallback(async () => {
    if (!submissionId) return;
    setActionStatus('Publishing to showcase…');
    try {
      await publishToShowcase(submissionId);
      setActionStatus('Letter published anonymously to the showcase.');
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : 'Could not publish to showcase.');
    }
  }, [submissionId]);

  const onDisputeViolation = useCallback(async (ruleId: string, violationId: string) => {
    const reason = window.prompt('Why is this detection wrong? (10+ characters)', '');
    if (!reason || reason.trim().length < 10) return;
    try {
      await disputeWritingCanonViolation(submissionId, { ruleId, violationId, reason: reason.trim() });
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : 'Dispute failed.');
    }
  }, [submissionId]);

  const scores = grade ? gradeToScores(grade) : null;
  const isA = grade?.bandLabel?.startsWith('A');
  const offerRevision = grade?.revisionInvite?.shouldOffer ?? false;

  return (
    <LearnerDashboardShell pageTitle="Submission Results">
      <div className="space-y-6" aria-busy={!grade}>
        <LearnerPageHero
          eyebrow="Result"
          icon={Award}
          accent="amber"
          title={grade?.bandLabel ? `Estimated band: ${grade.bandLabel}` : 'Awaiting grade'}
          description="Six-criterion rubric, canon engine, and the closest exemplar — together they explain exactly what to change next."
          highlights={grade ? [
            { icon: Award, label: 'Raw', value: `${grade.rawTotal}/38` },
            { icon: Sparkles, label: 'Confidence', value: grade.confidenceFlag },
            { icon: FileText, label: 'Mode', value: submission?.mode ?? '—' },
          ] : []}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {actionStatus ? <InlineAlert variant="info">{actionStatus}</InlineAlert> : null}

        {scores ? (
          <section aria-labelledby="criteria-heading" className="grid gap-4 rounded-2xl border border-border bg-surface p-5 shadow-sm lg:grid-cols-2">
            <div>
              <h2 id="criteria-heading" className="text-lg font-bold text-navy">Criteria radar</h2>
              <CriteriaRadar scores={scores} targetScores={{ c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 }} />
            </div>
            <details className="rounded-xl border border-border bg-background p-4" open>
              <summary className="cursor-pointer text-sm font-bold text-navy">Per-criterion feedback</summary>
              <ul className="mt-3 space-y-3">
                {(Object.entries(grade!.perCriterion) as Array<[WritingCriterionCode, NonNullable<typeof grade>['perCriterion'][WritingCriterionCode]]>).map(([code, feedback]) => (
                  <li key={code} className="rounded-lg border border-border bg-surface p-3">
                    <div className="flex items-center justify-between gap-2">
                      <h3 className="text-sm font-bold text-navy">{CRITERION_NAMES[code]}</h3>
                      <Badge variant="info" size="sm">{feedback.score}</Badge>
                    </div>
                    <p className="mt-1 text-xs text-muted leading-snug">{feedback.feedback}</p>
                    {feedback.exemplarFix ? (
                      <p className="mt-1 rounded bg-emerald-50 p-2 text-xs text-emerald-800">
                        <span className="font-bold">Exemplar fix:</span> {feedback.exemplarFix}
                      </p>
                    ) : null}
                  </li>
                ))}
              </ul>
            </details>
          </section>
        ) : null}

        {grade?.topThreePriorities?.length ? (
          <section aria-labelledby="priorities-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="priorities-heading" className="text-lg font-bold text-navy">Top three priorities</h2>
            <ol className="mt-3 grid gap-2 md:grid-cols-3">
              {grade.topThreePriorities.map((priority, idx) => (
                <li key={idx} className="rounded-xl border border-border bg-background p-3">
                  <Badge variant="warning" size="sm">#{idx + 1}</Badge>
                  <p className="mt-2 text-sm text-navy">{priority}</p>
                </li>
              ))}
            </ol>
          </section>
        ) : null}

        {grade?.canonViolations?.length ? (
          <section aria-labelledby="canon-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="canon-heading" className="text-lg font-bold text-navy">Canon violations ({grade.canonViolations.length})</h2>
            <div className="mt-3 grid gap-2 md:grid-cols-2">
              {grade.canonViolations.map((v) => (
                <CanonViolationCard key={v.id} violation={v} onDispute={(rid, vid) => onDisputeViolation(rid, vid)} />
              ))}
            </div>
          </section>
        ) : null}

        {submission && exemplar ? (
          <section aria-labelledby="exemplar-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="exemplar-heading" className="text-lg font-bold text-navy">Side-by-side with the gold-standard exemplar</h2>
            <ExemplarSideBySide
              candidateLetter={submission.letterContent}
              exemplarLetter={exemplar.letterContent}
              exemplarAnnotations={exemplar.annotations}
              className="mt-3"
            />
          </section>
        ) : null}

        <section aria-labelledby="actions-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <h2 id="actions-heading" className="text-lg font-bold text-navy">What's next?</h2>
          <p className="mt-1 text-sm text-muted">Take one of these actions — each is recorded against this submission.</p>
          <div className="mt-3 flex flex-wrap gap-2">
            {offerRevision || (submission && !submission.isRevision) ? (
              <Button asChild>
                <Link href={`/writing/submissions/${encodeURIComponent(submissionId)}/revise`}>
                  <RefreshCw className="h-4 w-4" aria-hidden="true" /> Revise
                </Link>
              </Button>
            ) : null}
            <Button variant="outline" onClick={() => void onAppeal()}>
              <Flag className="h-4 w-4" aria-hidden="true" /> Appeal score
            </Button>
            <Button variant="outline" onClick={() => void onTutorReview()}>
              <UserRoundCheck className="h-4 w-4" aria-hidden="true" /> Request tutor review
            </Button>
            {isA ? (
              <Button variant="outline" onClick={() => void onShowcase()}>
                <Share2 className="h-4 w-4" aria-hidden="true" /> Share to showcase
              </Button>
            ) : null}
          </div>
          {offerRevision && grade?.revisionInvite?.reason ? (
            <Card padding="md" className="mt-4 border-amber-300/70 bg-amber-50/60">
              <CardContent>
                <p className="text-sm text-amber-900">
                  <span className="font-bold">Why revise:</span> {grade.revisionInvite.reason}
                </p>
              </CardContent>
            </Card>
          ) : null}
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
