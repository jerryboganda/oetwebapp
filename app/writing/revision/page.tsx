'use client';

import { useState, useEffect, useMemo, useRef } from 'react';
import {
  ChevronLeft,
  ArrowRight,
  AlertCircle,
  TrendingUp,
  Minus,
  Send,
} from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { RevisionDiffViewer } from '@/components/domain/revision-diff-viewer';
import { WritingImprovementBanner } from '@/components/domain/writing-improvement-banner';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchWritingEntitlement, fetchWritingRevisionData, isApiError, submitWritingRevision, type WritingEntitlement } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { CriteriaDelta } from '@/lib/mock-data';
import { computeImprovementScore } from '@/lib/writing-revision/improvement-score';

type RevisionState = {
  resultId: string;
  attemptId: string;
  deltas: CriteriaDelta[];
  originalText: string;
  evaluatedRevisedText: string;
  revisedText: string;
  unresolvedIssues: string[];
  loading: boolean;
  error: string | null;
};

type EntitlementBlock = Pick<WritingEntitlement, 'reason' | 'resetAt'>;

const EMPTY_DELTAS: CriteriaDelta[] = [];
const EMPTY_UNRESOLVED_ISSUES: string[] = [];

export default function WritingRevisionMode() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const resultId = searchParams?.get('id') ?? '';
  const missingRevisionMessage = 'Revision unavailable. Open a completed Writing result first.';
  const [revisionState, setRevisionState] = useState<RevisionState>({
    resultId,
    attemptId: '',
    deltas: [],
    originalText: '',
    evaluatedRevisedText: '',
    revisedText: '',
    unresolvedIssues: [],
    loading: resultId.length > 0,
    error: null,
  });
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [entitlementBlock, setEntitlementBlock] = useState<EntitlementBlock | null>(null);
  const submitKeyRef = useRef<{ attemptId: string; content: string; key: string } | null>(null);
  const isCurrentResult = revisionState.resultId === resultId;
  const deltas = isCurrentResult ? revisionState.deltas : EMPTY_DELTAS;
  const originalText = isCurrentResult ? revisionState.originalText : '';
  const evaluatedRevisedText = isCurrentResult ? revisionState.evaluatedRevisedText : '';
  const revisedText = isCurrentResult ? revisionState.revisedText : '';
  const unresolvedIssues = isCurrentResult ? revisionState.unresolvedIssues : EMPTY_UNRESOLVED_ISSUES;
  const loading = resultId.length > 0 && (!isCurrentResult || revisionState.loading);
  const error = !resultId ? missingRevisionMessage : isCurrentResult ? revisionState.error : null;
  const draftHasDiverged = revisedText.trim() !== evaluatedRevisedText.trim();

  const improvement = useMemo(
    () => computeImprovementScore({ deltas, unresolvedIssuesCount: unresolvedIssues.length }),
    [deltas, unresolvedIssues],
  );

  useEffect(() => {
    if (!resultId) {
      return;
    }
    let active = true;
    analytics.track('content_view', { content: 'revision', resultId, subtest: 'writing' });
    fetchWritingRevisionData(resultId)
      .then((data) => {
        if (!active) return;
        setRevisionState({
          resultId,
          attemptId: data.attemptId,
          deltas: data.deltas,
          originalText: data.originalText,
          evaluatedRevisedText: data.revisedText,
          revisedText: data.revisedText,
          unresolvedIssues: data.unresolvedIssues,
          loading: false,
          error: null,
        });
      })
      .catch(() => {
        if (!active) return;
        setRevisionState({
          resultId,
          attemptId: '',
          deltas: [],
          originalText: '',
          evaluatedRevisedText: '',
          revisedText: '',
          unresolvedIssues: [],
          loading: false,
          error: 'Failed to load revision data. Please try again.',
        });
      });
    return () => {
      active = false;
    };
  }, [resultId]);

  useEffect(() => {
    if (loading || error || deltas.length === 0) return;
    analytics.track('writing_revision_score_computed', {
      resultId,
      score: improvement.score,
      band: improvement.band,
      criteriaImproved: improvement.criteriaImproved,
      criteriaRegressed: improvement.criteriaRegressed,
      unresolvedIssuesCount: unresolvedIssues.length,
    });
  }, [loading, error, deltas.length, improvement.score, improvement.band, improvement.criteriaImproved, improvement.criteriaRegressed, unresolvedIssues.length, resultId]);

  const updateRevisedText = (value: string) => {
    setSubmitError(null);
    setEntitlementBlock(null);
    setRevisionState((current) => ({
      ...current,
      revisedText: value,
    }));
  };

  const getRevisionSubmitKey = (attemptId: string, content: string) => {
    const current = submitKeyRef.current;
    if (current?.attemptId === attemptId && current.content === content) {
      return current.key;
    }

    const randomId = globalThis.crypto?.randomUUID?.().replaceAll('-', '')
      ?? `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 14)}`;
    const key = `wr-${randomId}`;
    submitKeyRef.current = { attemptId, content, key };
    return key;
  };

  const blockForEntitlement = (reason: string, resetAt: string | null) => {
    setEntitlementBlock({ reason, resetAt });
    setSubmitError(null);
  };

  const handleSubmitRevision = async () => {
    if (!revisionState.attemptId) {
      setSubmitError('Revision unavailable. Reopen the completed Writing result and try again.');
      return;
    }

    if (!revisedText.trim()) {
      setSubmitError('Add your revised letter before submitting it for evaluation.');
      return;
    }

    try {
      const entitlement = await fetchWritingEntitlement();
      if (!entitlement.allowed && (entitlement.reason === 'premium_required' || entitlement.reason === 'quota_exceeded')) {
        blockForEntitlement(entitlement.reason, entitlement.resetAt);
        return;
      }
    } catch {
      // Server-side entitlement remains authoritative on submit.
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      const submitted = await submitWritingRevision(
        revisionState.attemptId,
        revisedText,
        getRevisionSubmitKey(revisionState.attemptId, revisedText),
      );
      submitKeyRef.current = null;
      analytics.track('writing_revision_submitted', {
        resultId,
        attemptId: revisionState.attemptId,
        revisionAttemptId: submitted.attemptId,
        evaluationId: submitted.evaluationId,
      });
      router.push(`/writing/result?id=${encodeURIComponent(submitted.evaluationId)}`);
    } catch (err) {
      if (isApiError(err) && err.status === 402) {
        blockForEntitlement(
          err.code === 'premium_required' || err.code === 'quota_exceeded' ? err.code : 'quota_exceeded',
          null,
        );
        return;
      }

      setSubmitError(isApiError(err) ? err.userMessage : 'Could not submit this revision. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  const entitlementTitle = entitlementBlock?.reason === 'premium_required'
    ? 'AI grading is a premium feature'
    : 'Free quota reached';
  const entitlementMessage = entitlementBlock?.reason === 'premium_required'
    ? 'Upgrade to submit this revision for instant rule-cited feedback. Your edited letter is still here.'
    : entitlementBlock?.resetAt
      ? `You have used all free Writing gradings for this window. Quota resets ${new Date(entitlementBlock.resetAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}. Your edited letter is still here.`
      : 'You have reached the free Writing grading quota. Upgrade for unlimited revision grading. Your edited letter is still here.';

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Revision Mode">
        <div className="space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-[240px] rounded-2xl sm:h-[280px] lg:h-80" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Revision Mode">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Revision Mode">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="Revision Workspace"
          icon={TrendingUp}
          accent="amber"
          title="Revision Mode"
          description="Compare original vs. revised submission and see exactly which criteria changed."
          highlights={[
            { icon: TrendingUp, label: 'Delta summary', value: `${deltas.length} criteria` },
            { icon: AlertCircle, label: 'Open issues', value: `${unresolvedIssues.length} items` },
            { icon: Minus, label: 'Mode', value: 'Side-by-side review' },
          ]}
          aside={
            <div className="flex flex-col gap-3">
              <Link href={`/writing/feedback?id=${resultId}`} className="inline-flex items-center justify-center gap-2 rounded-xl border border-border bg-surface px-4 py-2 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-background-light">
                <ChevronLeft className="w-4 h-4" /> Back to feedback
              </Link>
              <Link href="/writing" className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
                Done
              </Link>
              <Button type="button" onClick={handleSubmitRevision} loading={submitting} disabled={!revisedText.trim() || Boolean(entitlementBlock)}>
                <Send className="h-4 w-4" /> Submit revision
              </Button>
            </div>
          }
        />

        {entitlementBlock ? (
          <InlineAlert
            variant="warning"
            title={entitlementTitle}
            action={(
              <Link href="/billing/subscribe" className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
                Upgrade
              </Link>
            )}
          >
            {entitlementMessage}
          </InlineAlert>
        ) : null}

        {submitError ? <InlineAlert variant="error">{submitError}</InlineAlert> : null}

        {draftHasDiverged ? (
          <InlineAlert variant="info">
            Score movement reflects the last evaluated revision. Submit this edited draft to refresh the comparison.
          </InlineAlert>
        ) : null}

        <MotionSection>
          <WritingImprovementBanner result={improvement} />
        </MotionSection>

        <MotionSection delayIndex={1}>
          <Card className="border-border bg-surface p-6">
            <LearnerSurfaceSectionHeader
              eyebrow="Rewrite"
              title="Revise your letter"
              description="Edit the second version here, then submit it as a linked revision for a fresh Writing evaluation."
              className="mb-5"
            />
            <label htmlFor="writing-revision-content" className="sr-only">Revised letter</label>
            <textarea
              id="writing-revision-content"
              value={revisedText}
              onChange={(event) => updateRevisedText(event.target.value)}
              className="min-h-[280px] w-full resize-y rounded-2xl border border-border bg-background-light p-4 font-serif text-base leading-7 text-navy shadow-inner outline-none transition-colors focus:border-primary focus:ring-2 focus:ring-primary/20"
              aria-describedby="writing-revision-submit-note"
            />
            <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <p id="writing-revision-submit-note" className="text-sm text-muted">
                This creates a new linked revision attempt. Your original letter remains unchanged.
              </p>
              <Button type="button" onClick={handleSubmitRevision} loading={submitting} disabled={!revisedText.trim() || Boolean(entitlementBlock)}>
                <Send className="h-4 w-4" /> Submit revision
              </Button>
            </div>
          </Card>
        </MotionSection>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
          <MotionSection delayIndex={2} className="lg:col-span-2">
            <Card className="border-border bg-surface p-6">
              <LearnerSurfaceSectionHeader
                eyebrow="Criterion Delta"
                title="What changed"
                description="Track score movement across each criterion and spot the biggest gains."
                className="mb-5"
              />
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                {deltas.map((delta) => {
                  const diff = delta.revised - delta.original;
                  return (
                    <div key={delta.name} className="rounded-2xl border border-border bg-background-light p-3">
                      <div className="mb-2 truncate text-xs font-bold uppercase tracking-wider text-muted" title={delta.name}>{delta.name}</div>
                      <div className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-2 text-sm font-medium text-muted">
                          <span>{delta.original}</span>
                          <ArrowRight className="w-3 h-3 text-muted/60" />
                          <span className={diff > 0 ? 'font-bold text-success' : ''}>{delta.revised}</span>
                          <span className="text-xs text-muted/60">/ {delta.max}</span>
                        </div>
                        {diff > 0 ? (
                          <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-success/10 text-xs font-bold text-success">+{diff}</span>
                        ) : (
                          <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-background-light text-xs font-bold text-muted"><Minus className="w-3 h-3" /></span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </Card>
          </MotionSection>

          <MotionSection delayIndex={3}>
            <Card className="flex h-full flex-col border-warning/30 bg-warning/10 p-6">
              <LearnerSurfaceSectionHeader
                eyebrow="Open Issues"
                title="Still to fix"
                description="These are the gaps that remain after the revision pass."
                className="mb-4"
              />
              <ul className="flex-1 space-y-3">
                {unresolvedIssues.map((issue, idx) => (
                  <li key={idx} className="flex items-start gap-2 text-sm text-warning">
                    <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-warning" />
                    <span className="leading-snug">{issue}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>
        </div>

        <MotionSection delayIndex={4}>
          <RevisionDiffViewer original={originalText} revised={revisedText} />
        </MotionSection>
      </main>
    </LearnerDashboardShell>
  );
}
