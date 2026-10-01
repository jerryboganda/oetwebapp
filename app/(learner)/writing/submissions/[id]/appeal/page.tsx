'use client';

/**
 * Writing Module V2 — Score Appeal page (spec §12.6).
 *
 * Flow:
 *   1. Learner enters a reason for the appeal.
 *   2. We confirm the charge ($5) — or use the entitlement if they have one.
 *   3. POST kicks off GPT-5.5 second-opinion grading.
 *   4. UI polls /appeal until status is `resolved`.
 *   5. If |Δ| > 3 raw points the backend averages and surfaces the
 *      new grade side-by-side with the original.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Clock, Scale } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import {
  getWritingSubmissionGrade,
  getWritingAppealResult,
  requestWritingAppeal,
} from '@/lib/writing/api';
import type { WritingGradeDto, WritingScoreAppealDto } from '@/lib/writing/types';

const APPEAL_PRICE_USD = 5;

/**
 * Pending/in-progress statuses that warrant polling for an update.
 * Backend reports `pending`, `in_progress`, `pending_manual`, `resolved`.
 */
const ACTIVE_STATUSES = new Set(['pending', 'in_progress', 'in-progress', 'pending_manual']);

export default function WritingAppealPage() {
  const params = useParams<{ id: string }>();
  const submissionId = String(params?.id ?? '');
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [appeal, setAppeal] = useState<WritingScoreAppealDto | null>(null);
  const [reason, setReason] = useState('');
  const [confirmedCharge, setConfirmedCharge] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<'load' | 'submit' | null>(null);

  const load = useCallback(async () => {
    if (!submissionId) return;
    setBusy('load');
    setError(null);
    try {
      const [g, a] = await Promise.all([
        getWritingSubmissionGrade(submissionId).catch(() => null),
        getWritingAppealResult(submissionId).catch(() => null),
      ]);
      setGrade(g);
      setAppeal(a);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load this submission.');
    } finally {
      setBusy(null);
    }
  }, [submissionId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Poll while the appeal is still being graded.
  useEffect(() => {
    if (!appeal || !ACTIVE_STATUSES.has(appeal.status)) return;
    const interval = setInterval(() => {
      void getWritingAppealResult(submissionId).then((next) => {
        if (next) setAppeal(next);
      }).catch(() => undefined);
    }, 5000);
    return () => clearInterval(interval);
  }, [appeal, submissionId]);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!reason.trim()) {
      setError('Please describe why you are appealing.');
      return;
    }
    if (!confirmedCharge) {
      setError('You must confirm the appeal charge before continuing.');
      return;
    }
    setBusy('submit');
    setError(null);
    try {
      const result = await requestWritingAppeal(submissionId, reason.trim());
      setAppeal(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Appeal request failed.');
    } finally {
      setBusy(null);
    }
  };

  const statusBadge = useMemo(() => {
    if (!appeal) return null;
    switch (appeal.status) {
      case 'pending':
      case 'in_progress':
      case 'in-progress':
        return <Badge variant="warning">Grading</Badge>;
      case 'pending_manual':
        return <Badge variant="info">Tutor queued</Badge>;
      case 'resolved':
        return <Badge variant="success">Resolved</Badge>;
      case 'rejected':
        return <Badge variant="danger">Rejected</Badge>;
      default:
        return <Badge>{appeal.status}</Badge>;
    }
  }, [appeal]);

  const deltaRaw = useMemo(() => {
    if (!appeal || appeal.secondOpinionRawTotal == null) return null;
    return appeal.secondOpinionRawTotal - appeal.originalRawTotal;
  }, [appeal]);

  const finalRaw = appeal?.finalRawTotal ?? appeal?.secondOpinionRawTotal ?? null;
  const wasAdjusted = finalRaw != null && finalRaw !== appeal?.originalRawTotal;

  return (
    <>
      <LearnerPageHero
        eyebrow="Submission"
        icon={Scale}
        accent="writing"
        title="Score appeal"
        description="A second AI examiner regrades your letter independently. If their score differs by more than 3 raw points, we average the two and update your record."
        aside={
          <Button asChild variant="outline" size="sm">
            <Link href={`/writing/submissions/${submissionId}`}>
              <ArrowLeft className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" /> Back to submission
            </Link>
          </Button>
        }
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {/* Original grade summary */}
      <MotionSection delayIndex={0}>
        <Card padding="lg">
          <h2 className="mb-3 text-lg font-bold text-navy">Original AI grade</h2>
          {!grade ? (
            busy === 'load'
              ? <Skeleton lines={2} className="max-w-md" />
              : <p className="text-sm text-muted">No grade available yet.</p>
          ) : (
            <dl className="grid grid-cols-2 gap-3 text-sm md:grid-cols-4">
              <div className="min-w-0">
                <dt className="eyebrow text-muted">Band</dt>
                <dd className="text-xl font-bold text-navy">{grade.bandLabel}</dd>
              </div>
              <div className="min-w-0">
                <dt className="eyebrow text-muted">Raw total</dt>
                <dd className="text-xl font-bold tabular-nums text-navy">{grade.rawTotal} / 38</dd>
              </div>
              <div className="min-w-0">
                <dt className="eyebrow text-muted">Confidence</dt>
                <dd className="font-bold text-navy">{grade.confidenceFlag}</dd>
              </div>
              <div className="min-w-0">
                <dt className="eyebrow text-muted">Model</dt>
                <dd className="break-words font-mono text-xs text-navy">{grade.modelUsed}</dd>
              </div>
            </dl>
          )}
        </Card>
      </MotionSection>

      {/* Appeal form OR result */}
      {!appeal ? (
        <MotionSection delayIndex={1}>
          <Card padding="lg" className="space-y-4">
            <h2 className="text-lg font-bold text-navy">Request a second opinion</h2>
            <p className="text-sm text-muted">
              Appeals are reviewed by an independent GPT-class examiner. A reason is required so
              the second examiner knows what to focus on.
            </p>
            <form onSubmit={onSubmit} className="space-y-4" aria-label="Score appeal request">
              <label className="block">
                <span className="eyebrow text-muted">Reason for appeal</span>
                <textarea
                  required
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  maxLength={500}
                  rows={5}
                  placeholder="E.g. The C2 Content score does not reflect that I covered all four bullet points from the case notes."
                  className="mt-1 w-full rounded-control border border-border bg-background p-3 text-sm text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                />
                <span className="block text-end text-xs tabular-nums text-muted">{500 - reason.length} characters remaining</span>
              </label>

              <label className="flex items-start gap-3 rounded-xl border border-warning/20 bg-warning/10 p-4 text-sm text-warning-strong">
                <input
                  type="checkbox"
                  className="mt-1 h-4 w-4 shrink-0 accent-primary"
                  checked={confirmedCharge}
                  onChange={(e) => setConfirmedCharge(e.target.checked)}
                />
                <span>
                  I understand a ${APPEAL_PRICE_USD} charge applies for this appeal (or one
                  appeal entitlement from my plan will be consumed). If the second opinion
                  differs by more than 3 raw points, my score will be updated to the average
                  and the change disclosed.
                </span>
              </label>

              <Button type="submit" loading={busy === 'submit'}>
                {busy === 'submit' ? 'Requesting…' : 'Request appeal'}
              </Button>
            </form>
          </Card>
        </MotionSection>
      ) : (
        <MotionSection delayIndex={1}>
          <Card padding="lg" className="space-y-4">
            <header className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-lg font-bold text-navy">Appeal status</h2>
              {statusBadge}
            </header>

            <p className="text-xs text-muted">
              Requested {new Date(appeal.requestedAt).toLocaleString()}
              {appeal.resolvedAt ? ` · Resolved ${new Date(appeal.resolvedAt).toLocaleString()}` : ''}
            </p>

            {/* A static icon: the regrade runs 30–60 s, longer than a spinner may loop (WCAG 2.2.2). */}
            {ACTIVE_STATUSES.has(appeal.status) ? (
              <div role="status" className="flex items-center gap-3 rounded-xl border border-warning/20 bg-warning/10 p-4 text-sm text-warning-strong">
                <Clock className="h-5 w-5 shrink-0" aria-hidden="true" />
                <span>The second examiner is regrading your letter. This usually takes 30–60 seconds.</span>
              </div>
            ) : null}

            {/* Side-by-side comparison once the second opinion is in */}
            {appeal.secondOpinionRawTotal != null ? (
              <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                <div className="min-w-0 rounded-xl border border-border bg-background-light p-4">
                  <p className="tile-label text-muted">Original</p>
                  <p className="text-3xl font-bold text-navy"><CountUp value={appeal.originalRawTotal} /></p>
                  <p className="text-xs text-muted">raw / 38</p>
                </div>
                <div className="min-w-0 rounded-xl border border-border bg-background-light p-4">
                  <p className="tile-label text-muted">Second opinion</p>
                  <p className="text-3xl font-bold text-navy"><CountUp value={appeal.secondOpinionRawTotal} /></p>
                  <p className={`text-xs tabular-nums ${deltaRaw != null && Math.abs(deltaRaw) > 3 ? 'text-warning-strong' : 'text-muted'}`}>
                    {deltaRaw == null ? 'raw / 38' : `${deltaRaw >= 0 ? '+' : ''}${deltaRaw} vs original`}
                  </p>
                </div>
                <div className={`min-w-0 rounded-xl border p-4 ${wasAdjusted ? 'border-success/30 bg-success/10' : 'border-border bg-background-light'}`}>
                  <p className="tile-label text-muted">Final on record</p>
                  <p className="text-3xl font-bold text-navy"><CountUp value={finalRaw ?? appeal.originalRawTotal} /></p>
                  <p className="text-xs text-muted">
                    {wasAdjusted
                      ? 'Score updated to the average (Δ > 3).'
                      : 'No change, within tolerance.'}
                  </p>
                </div>
              </div>
            ) : null}

            {appeal.reasoning ? (
              <article className="space-y-2 rounded-xl bg-background-light p-4">
                <h3 className="text-sm font-bold text-navy">Examiner notes</h3>
                <p className="max-w-3xl whitespace-pre-wrap text-sm text-navy/90">{appeal.reasoning}</p>
              </article>
            ) : null}

            {appeal.status === 'resolved' && wasAdjusted ? (
              <InlineAlert variant="success">
                Your record has been updated to {finalRaw} / 38.
              </InlineAlert>
            ) : null}
            {appeal.status === 'pending_manual' ? (
              <InlineAlert variant="warning">
                The AI examiner was unavailable. A human tutor will review this appeal shortly.
              </InlineAlert>
            ) : null}
          </Card>
        </MotionSection>
      )}
    </>
  );
}
