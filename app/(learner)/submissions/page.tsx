'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  FileText,
  Headphones,
  PenTool,
  Mic,
  MessageSquare,
  GitCompare,
  Send,
  Clock,
  CheckCircle2,
  AlertCircle,
  History,
  Play,
} from 'lucide-react';
import React from 'react';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { cardClassName } from '@/components/ui/card';
import { fetchSubmissions, fetchMyAttemptHistory, type LearnerAttemptHistoryItem } from '@/lib/api';
import type { Submission, SubTest, ReviewStatus } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { WritingMyWorkList } from '@/components/domain/writing/WritingMyWorkList';
import { cn } from '@/lib/utils';

// Sub-test identity (DESIGN.md §2 skill tokens), never a status colour.
const SUBTEST_STYLE: Record<SubTest, { icon: React.ElementType; badge: string }> = {
  Reading:   { icon: FileText,   badge: 'bg-skill-reading/10 text-skill-reading border-skill-reading/20' },
  Listening: { icon: Headphones, badge: 'bg-skill-listening/10 text-skill-listening border-skill-listening/20' },
  Writing:   { icon: PenTool,    badge: 'bg-skill-writing/10 text-skill-writing border-skill-writing/20' },
  Speaking:  { icon: Mic,        badge: 'bg-skill-speaking/10 text-skill-speaking border-skill-speaking/20' },
};
// Full mocks span every sub-test, so they get the neutral accent rather than borrowing one skill.
const MOCK_BADGE = 'bg-lavender text-primary border-primary/20';

// The server sends a ready-to-show label: a score ("192/500") reads as a result, anything else
// ("Marking in progress") is a state and gets the attention tone.
const SCORE_LABEL = /^\d+\/\d+$/;

function formatSubmissionAttemptDate(value: string) {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  const includesTime = /T\d{2}:\d{2}/.test(value);

  return new Intl.DateTimeFormat(undefined, includesTime
    ? {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
      }
    : {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
      }).format(parsed);
}

function ReviewBadge({ status }: { status: ReviewStatus }) {
  if (status === 'reviewed') return (
    <Badge variant="success" className="gap-1.5 px-2.5 py-1">
      <CheckCircle2 className="w-3.5 h-3.5" aria-hidden="true" /> Reviewed
    </Badge>
  );
  if (status === 'pending') return (
    <Badge variant="warning" className="gap-1.5 px-2.5 py-1">
      <Clock className="w-3.5 h-3.5" aria-hidden="true" /> Pending
    </Badge>
  );
  return (
    <Badge variant="slate" className="gap-1.5 px-2.5 py-1">
      <AlertCircle className="w-3.5 h-3.5" aria-hidden="true" /> Not Requested
    </Badge>
  );
}

/**
 * Entry-point wrapper: filtering is driven by `?subtest=writing` or
 * `?subtest=speaking` (see the "Past submissions" card of app/writing/page.tsx
 * and the Speaking hub), and reading that query param via useSearchParams
 * forces a CSR bailout — Suspense is required around it, matching the pattern
 * already used in app/mocks/page.tsx.
 */
export default function SubmissionHistory() {
  return (
    <Suspense fallback={null}>
      <SubmissionHistoryInner />
    </Suspense>
  );
}

function SubmissionHistoryInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  // The Writing hub's "Past submissions" card links here with ?subtest=writing
  // so it never opens the global all-subtest history — see brief item 5.
  const writingOnly = searchParams?.get('subtest') === 'writing';
  // The Speaking hub's "Speaking submissions" card links here with ?subtest=speaking: every Speaking
  // card and full mock with its score and grade, never the other three sub-tests.
  const speakingOnly = searchParams?.get('subtest') === 'speaking';
  const subtestFilter = writingOnly ? 'writing' : speakingOnly ? 'speaking' : undefined;
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [attempts, setAttempts] = useState<LearnerAttemptHistoryItem[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Writing view only: drafts + V2 submissions (my-work). null = not loaded yet or failed.
  const [myWorkCount, setMyWorkCount] = useState<number | null>(null);

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'submissions' });
    // Server-side filter is the real fix: it applies before each endpoint's
    // limit/cursor truncation, so a Writing-only view stays complete even
    // once a learner has 100+ more-recent Reading/Listening/Speaking
    // attempts (see brief item 5). The visible* filters below are kept as a
    // defensive belt-and-suspenders pass, not the primary filter.
    // Speaking results live under "Attempt activity" below; the evidence list is tutor-review oriented.
    const evidence = speakingOnly
      ? Promise.resolve<Submission[]>([])
      : fetchSubmissions(subtestFilter ? { subtest: subtestFilter } : undefined);
    evidence
      .then((data) => { setSubmissions(data); setLoading(false); })
      .catch(() => { setError('Failed to load submissions. Please try again.'); setLoading(false); });
    // Unified all-four-subtest attempt history (Master Catalogue §2). Best
    // effort: the review list below still renders if this endpoint fails.
    fetchMyAttemptHistory(100, subtestFilter)
      .then((items) => setAttempts(items))
      .catch(() => setAttempts([]));
  }, [speakingOnly, subtestFilter]);

  // Defensive only — the server-side subtest filter above is what actually
  // keeps this correct once either list exceeds its page/limit size.
  const visibleSubmissions = useMemo(
    () => (writingOnly ? submissions.filter((sub) => sub.subTest === 'Writing') : submissions),
    [submissions, writingOnly],
  );
  const visibleAttempts = useMemo(
    () => (subtestFilter ? (attempts ?? []).filter((attempt) => attempt.subtest === subtestFilter) : attempts),
    [attempts, subtestFilter],
  );

  const pendingReviewCount = visibleSubmissions.filter((submission) => submission.reviewStatus === 'pending').length;
  const comparisonReadyCount = visibleSubmissions.filter((submission) => Boolean(submission.actions.compareRoute)).length;

  return (
    <>
      <LearnerPageHero
        eyebrow={speakingOnly ? 'Speaking Submissions' : writingOnly ? 'Writing Evidence' : 'Evidence History'}
        icon={History}
        accent={speakingOnly ? 'speaking' : 'slate'}
        title={speakingOnly
          ? 'Reopen your Speaking role-plays and mock results'
          : writingOnly ? 'Reopen Writing letters that need review or comparison' : 'Reopen the attempts that need review or comparison'}
        description={speakingOnly
          ? 'Every Speaking role-play and full mock with its score, OET grade and where to resume — Reading, Listening and Writing attempts are never shown here.'
          : writingOnly
            ? 'Every submitted Writing letter and its feedback/review state — Reading, Listening and Speaking attempts are never shown here.'
            : 'Use submission history to find the attempts that still need feedback, comparison, or a fresh follow-up decision.'}
        highlights={speakingOnly
          ? [{ icon: History, label: 'Attempts', value: `${visibleAttempts?.length ?? 0} recorded` }]
          : [
              { icon: History, label: 'Attempts', value: `${Math.max(visibleSubmissions.length, visibleAttempts?.length ?? 0)} recorded` },
              { icon: Clock, label: 'Pending reviews', value: `${pendingReviewCount} waiting` },
              { icon: GitCompare, label: 'Compare ready', value: `${comparisonReadyCount} attempts` },
            ]}
      />

      {/* Post Submissions: Writing drafts and submitted letters, with Resume / Retry. */}
      {writingOnly ? <WritingMyWorkList onCountChange={setMyWorkCount} /> : null}

      {/* Unified attempt activity — Reading / Listening / Writing / Speaking
          plus full mocks: exact item title/ID, subtest, start time, status,
          balance source and credits used; reopening never deducts again.
          Filtered to Writing only when opened from inside Writing. */}
      {visibleAttempts && visibleAttempts.length > 0 ? (
        <section aria-label="Attempt activity">
          <LearnerSurfaceSectionHeader
            eyebrow={speakingOnly ? 'Speaking' : writingOnly ? 'Writing' : 'All Subtests'}
            title="Attempt activity"
            description={speakingOnly
              ? 'Every Speaking role-play and full mock: score, OET grade, credits used and where to resume or retry grading.'
              : writingOnly
                ? 'Every opened Writing letter, its balance source, credits used and where to resume.'
                : 'Every opened exam or card, its balance source, credits used and where to resume.'}
            className="mb-4"
          />
          <ul className="space-y-2">
            {visibleAttempts.map((attempt, index) => {
              const styleKeyMap: Record<string, SubTest> = {
                reading: 'Reading',
                listening: 'Listening',
                writing: 'Writing',
                speaking: 'Speaking',
              };
              const styleKey = styleKeyMap[attempt.subtest];
              const style = styleKey ? SUBTEST_STYLE[styleKey] : null;
              const Icon = attempt.subtest === 'mock' ? GitCompare : style?.icon ?? FileText;
              return (
                <li key={`${attempt.subtest}-${attempt.attemptId}`}>
                  <MotionItem
                    delayIndex={Math.min(index, 5)}
                    className={cn(cardClassName({ padding: 'sm' }), 'flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between')}
                  >
                    <div className="flex min-w-0 items-start gap-3">
                      <span className={cn('mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full border', style?.badge ?? MOCK_BADGE)}>
                        <Icon className="h-4 w-4" aria-hidden="true" />
                      </span>
                      <div className="min-w-0">
                        <p className="truncate text-sm font-bold text-navy">
                          {attempt.title}
                          <span className="ms-2 font-mono text-2xs font-medium text-muted">{attempt.attemptId}</span>
                        </p>
                        <p className="mt-0.5 text-xs text-muted">
                          <span className="capitalize">{attempt.subtest}</span>
                          {' · '}started {formatSubmissionAttemptDate(attempt.startedAt)}
                          {' · '}
                          <span className={attempt.status === 'in_progress' ? 'font-bold text-warning-strong' : 'font-bold text-success-strong'}>
                            {attempt.status === 'in_progress' ? 'In progress' : 'Completed'}
                          </span>
                          {attempt.resultLabel ? (
                            <>
                              {' · '}
                              <span className={SCORE_LABEL.test(attempt.resultLabel) ? 'font-bold text-navy' : 'font-bold text-warning-strong'}>
                                {attempt.resultLabel}
                              </span>
                              {attempt.grade ? (
                                <>
                                  {' · '}
                                  <span className="font-bold text-navy">Grade {attempt.grade}</span>
                                </>
                              ) : null}
                            </>
                          ) : null}
                          {attempt.balanceSource || attempt.creditsUsed > 0 ? (
                            <>
                              {' · '}
                              {attempt.balanceSource === 'shared'
                                ? 'Shared Credits'
                                : attempt.balanceSource === 'flexible_ws'
                                  ? 'Flexible W/S'
                                  : attempt.balanceSource === 'mock'
                                    ? 'Mock allowance'
                                    : `${attempt.subtest} balance`}
                              {attempt.creditsUsed > 0 ? ` · ${attempt.creditsUsed} credit${attempt.creditsUsed === 1 ? '' : 's'} used` : ''}
                            </>
                          ) : null}
                        </p>
                      </div>
                    </div>
                    <Button asChild variant="outline" size="sm" className="shrink-0 self-start sm:self-center">
                      <Link href={attempt.route}>
                        <Play className="h-3.5 w-3.5" aria-hidden="true" />
                        {attempt.status === 'in_progress' ? 'Resume' : 'Review'}
                      </Link>
                    </Button>
                  </MotionItem>
                </li>
              );
            })}
          </ul>
        </section>
      ) : null}

      {loading ? (
        <div className="space-y-4" aria-hidden="true">
          {[1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-36 rounded-2xl" />
          ))}
        </div>
      ) : null}

      {!loading && error ? (
        <InlineAlert variant="error">{error}</InlineAlert>
      ) : null}

      {/* A Speaking-only learner has a Full Speaking Mock under "Attempt activity" but no Past Evidence
          card (those rows have no Evaluation), so wait for the attempt list before saying history is empty.
          The Writing view is empty only once my-work has loaded with no draft or letter in it. */}
      {!loading && !error && visibleSubmissions.length === 0 && (writingOnly ? myWorkCount === 0 : (attempts !== null && visibleAttempts?.length === 0)) ? (
        <EmptyState
          icon={<History className="h-8 w-8" />}
          title={speakingOnly ? 'No Speaking attempts yet' : writingOnly ? 'No Writing submissions yet' : 'No submissions yet'}
          description={speakingOnly
            ? 'Complete a Speaking role-play or full mock to see your history here.'
            : writingOnly ? 'Complete a Writing letter to see your history here.' : 'Complete a writing or speaking task to see your history here.'}
          action={speakingOnly
            ? { label: 'Start Speaking', onClick: () => router.push('/speaking') }
            : { label: 'Start a writing task', onClick: () => router.push('/writing') }}
          className="py-16"
        />
      ) : null}

      {!loading && !error && visibleSubmissions.length > 0 ? (
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow={writingOnly ? 'Writing Evidence' : 'Past Evidence'}
            title="Keep review state and score direction visible"
            className="mb-4"
          />

          <div className="space-y-4">
            {visibleSubmissions.map((sub, idx) => {
              const meta = SUBTEST_STYLE[sub.subTest] ?? SUBTEST_STYLE.Writing;
              const Icon = meta.icon;
              const canRequest = sub.canRequestReview;
              return (
                <MotionItem
                  key={sub.id}
                  delayIndex={Math.min(idx, 5)}
                  className={cn(cardClassName({ padding: 'lg' }), 'flex flex-col justify-between gap-5 transition-colors hover:border-border-hover md:flex-row md:gap-6')}
                >
                  <div className="min-w-0 flex-1 space-y-4">
                    <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
                      <div className="min-w-0">
                        <div className="mb-2 flex flex-wrap items-center gap-x-3 gap-y-1.5">
                          <span className={cn('tile-label inline-flex items-center gap-1.5 rounded-control border px-2.5 py-1', meta.badge)}>
                            <Icon className="h-3.5 w-3.5" aria-hidden="true" />
                            {sub.subTest}
                          </span>
                          <span className="text-sm font-medium text-muted">{formatSubmissionAttemptDate(sub.attemptDate)}</span>
                        </div>
                        <h3 className="text-lg font-bold leading-tight text-navy">{sub.taskName}</h3>
                      </div>
                      <div className="shrink-0 rounded-xl border border-border bg-background-light p-3 sm:border-none sm:bg-transparent sm:p-0 sm:text-end">
                        <div className="eyebrow mb-1 text-muted">Score Estimate</div>
                        <div className={`text-xl font-bold tabular-nums ${sub.scoreEstimate === 'Pending' ? 'text-muted' : 'text-navy'}`}>
                          {sub.scoreEstimate}
                        </div>
                      </div>
                    </div>
                    <div className="flex flex-wrap items-center gap-3 border-t border-border pt-2">
                      <span className="text-sm font-medium text-muted">Review Status:</span>
                      <ReviewBadge status={sub.reviewStatus} />
                    </div>
                  </div>

                  <div className="flex shrink-0 flex-col justify-center gap-2 border-t border-border pt-5 md:w-48 md:border-s md:border-t-0 md:ps-6 md:pt-0">
                    <Button
                      variant="outline"
                      fullWidth
                      onClick={() => sub.actions.reopenFeedbackRoute && router.push(sub.actions.reopenFeedbackRoute)}
                      disabled={!sub.actions.reopenFeedbackRoute}
                    >
                      <MessageSquare className="w-4 h-4" aria-hidden="true" />
                      Reopen Feedback
                    </Button>
                    <Button
                      variant="outline"
                      fullWidth
                      onClick={() => sub.actions.compareRoute && router.push(sub.actions.compareRoute)}
                      disabled={!sub.actions.compareRoute}
                    >
                      <GitCompare className="w-4 h-4" aria-hidden="true" />
                      Compare Attempts
                    </Button>
                    {/* Tutor review is a Writing feature; Speaking results are AI-only. */}
                    {sub.subTest === 'Writing' ? (
                      <Button
                        variant="primary"
                        fullWidth
                        onClick={() => sub.actions.requestReviewRoute && router.push(sub.actions.requestReviewRoute)}
                        disabled={!canRequest || !sub.actions.requestReviewRoute}
                      >
                        <Send className="w-4 h-4" aria-hidden="true" />
                        Request Tutor Review
                      </Button>
                    ) : null}
                  </div>
                </MotionItem>
              );
            })}
          </div>
        </section>
      ) : null}
    </>
  );
}
