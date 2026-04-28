'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, History, Sparkles, Target, Users } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import {
  ExpertRouteFreshnessBadge,
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { fetchExpertLearnerReviewContext, fetchLearnerProfile, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ExpertLearnerReviewContext, LearnerProfileExpanded } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success';

export default function AssignedLearnerPage() {
  const params = useParams();
  const learnerId = params?.learnerId as string | undefined;
  const router = useRouter();

  const [learner, setLearner] = useState<LearnerProfileExpanded | null>(null);
  const [context, setContext] = useState<ExpertLearnerReviewContext | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    if (!learnerId) return;
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        setErrorMessage(null);
        const [data, scopedContext] = await Promise.all([
          fetchLearnerProfile(learnerId),
          fetchExpertLearnerReviewContext(learnerId),
        ]);
        if (cancelled) return;
        setLearner(data);
        setContext(scopedContext);
        setPageStatus('success');
        analytics.track('learner_profile_viewed', { learnerId });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load learner context right now.');
          setPageStatus('error');
        }
      }
    })();
    return () => { cancelled = true; };
  }, [learnerId, reloadToken]);

  return (
    <ExpertRouteWorkspace role="main" aria-label="Learner Profile">
      <div className="space-y-4">
        <Button variant="ghost" className="w-fit pl-0 text-muted" onClick={() => router.back()} aria-label="Go back">
          <ArrowLeft className="mr-2 h-4 w-4" /> Back
        </Button>

        <AsyncStateWrapper status={pageStatus} onRetry={() => setReloadToken((current) => current + 1)} errorMessage={errorMessage ?? undefined}>
          {learner ? (
            <div className="space-y-6">
              <ExpertRouteHero
                eyebrow="Learner Profile"
                icon={Sparkles}
                accent="primary"
                title={learner.name}
                description="Learner context is shown only to support the reviews assigned to you. Use it for review decisions only and do not share it outside the console."
                highlights={[
                  { icon: Target, label: 'Target score', value: String(learner.goalScore) },
                  { icon: Users, label: 'Scope', value: context ? `${context.reviewsInScope} review${context.reviewsInScope === 1 ? '' : 's'}` : 'Loading...' },
                  { icon: History, label: 'Joined', value: learner.joinedAt.split('T')[0] },
                ]}
                aside={(
                  <div className="space-y-3">
                    <ExpertRouteFreshnessBadge value={learner.joinedAt} />
                    <div className="flex flex-wrap gap-2">
                      <Badge variant="info" className="capitalize px-3 py-1 text-sm">{learner.profession}</Badge>
                      {learner.visibilityScope ? <Badge variant="default">{learner.visibilityScope.replace(/_/g, ' ')}</Badge> : null}
                      {context ? <Badge variant="success">{context.reviewsInScope} reviews in scope</Badge> : null}
                    </div>
                  </div>
                )}
              />

              <InlineAlert variant="info" title="Privacy Notice">
                Learner context is shown only to support the reviews assigned to you. Use it for review decisions only and do not share it outside the console.
              </InlineAlert>

              <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <ExpertRouteSummaryCard
                  label="Goal & Context"
                  value={String(learner.goalScore)}
                  hint={learner.examDate ? `Exam date ${learner.examDate.split('T')[0]}` : 'Exam date not set'}
                  accent="primary"
                  icon={Target}
                />
                <ExpertRouteSummaryCard
                  label="Review Scope"
                  value={context ? `${context.reviewsInScope}` : '-'}
                  hint={context ? 'Expert-linked review(s) in scope.' : 'Loading scope...'}
                  accent="navy"
                  icon={Users}
                />
                <ExpertRouteSummaryCard
                  label="Platform History"
                  value={learner.totalReviews}
                  hint={`${learner.attemptsCount} submission${learner.attemptsCount === 1 ? '' : 's'} recorded.`}
                  accent="emerald"
                  icon={History}
                />
              </div>

              <section className="space-y-4">
                <ExpertRouteSectionHeader
                  eyebrow="Profile Overview"
                  title="Learner context"
                  description="Goal, review scope, and platform history shown in the same surface rhythm as the learner dashboard."
                />
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  <Card className="border-slate-200 shadow-sm">
                    <div className="flex items-center gap-2 border-b border-gray-100 p-4 font-semibold text-navy">
                      <Target className="h-4 w-4" /> Goal & Context
                    </div>
                    <CardContent className="space-y-4 p-4">
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Target Score</p>
                        <p className="mt-1 font-medium text-navy">{learner.goalScore}</p>
                      </div>
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Exam Date</p>
                        <p className="mt-1 text-navy">{learner.examDate ? learner.examDate.split('T')[0] : 'Not Set'}</p>
                      </div>
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Review Scope</p>
                        <p className="mt-1 text-navy">{context ? `${context.reviewsInScope} expert-linked review(s)` : 'Loading scope...'}</p>
                      </div>
                    </CardContent>
                  </Card>

                  <Card className="border-slate-200 shadow-sm">
                    <div className="flex items-center gap-2 border-b border-gray-100 p-4 font-semibold text-navy">
                      <History className="h-4 w-4" /> Platform History
                    </div>
                    <CardContent className="space-y-4 p-4">
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Joined</p>
                        <p className="mt-1 text-navy">{learner.joinedAt.split('T')[0]}</p>
                      </div>
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Total Reviews</p>
                        <p className="mt-1 text-navy">{learner.totalReviews}</p>
                      </div>
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Submissions</p>
                        <p className="mt-1 text-navy">{learner.attemptsCount}</p>
                      </div>
                    </CardContent>
                  </Card>
                </div>
              </section>

              <section className="space-y-4">
                <ExpertRouteSectionHeader
                  eyebrow="Performance Evidence"
                  title="Sub-test performance"
                  description="The table below keeps the learner's latest outcomes visible while preserving the original review evidence."
                />
                <Card className="overflow-hidden border-slate-200 shadow-sm">
                  <CardContent className="p-0">
                    <table className="w-full text-sm" aria-label="Sub-test performance scores">
                      <thead className="border-b border-gray-200 bg-gray-50">
                        <tr>
                          <th scope="col" className="p-3 text-left font-semibold text-muted">Sub-Test</th>
                          <th scope="col" className="p-3 text-left font-semibold text-muted">Latest Score</th>
                          <th scope="col" className="p-3 text-left font-semibold text-muted">Grade</th>
                          <th scope="col" className="p-3 text-left font-semibold text-muted">Attempts</th>
                        </tr>
                      </thead>
                      <tbody>
                        {learner.subTestScores.map((subTestScore) => (
                          <tr key={subTestScore.subTest} className="border-b border-gray-100 last:border-0">
                            <td className="p-3 font-medium capitalize text-navy">{subTestScore.subTest}</td>
                            <td className="p-3">{subTestScore.latestScore ?? '-'}</td>
                            <td className="p-3">
                              {subTestScore.latestGrade ? <Badge variant={subTestScore.latestGrade.startsWith('A') || subTestScore.latestGrade.startsWith('B') ? 'success' : 'warning'}>{subTestScore.latestGrade}</Badge> : '-'}
                            </td>
                            <td className="p-3">{subTestScore.attempts}</td>
                          </tr>
                        ))}
                        {learner.subTestScores.length === 0 ? (
                          <tr>
                            <td colSpan={4} className="p-4 text-center italic text-muted">No sub-test data is available yet.</td>
                          </tr>
                        ) : null}
                      </tbody>
                    </table>
                  </CardContent>
                </Card>
              </section>

              <section className="space-y-4">
                <ExpertRouteSectionHeader
                  eyebrow="Prior Tutor Reviews"
                  title="Historical feedback"
                  description="Keep earlier tutor comments visible so the current review stays consistent with prior context."
                />
                <div className="grid grid-cols-1 gap-4">
                  {learner.priorReviews.length === 0 ? (
                    <Card className="border-slate-200 shadow-sm">
                      <CardContent className="p-4">
                        <p className="text-sm italic text-muted">No prior tutor feedback is available for this learner.</p>
                      </CardContent>
                    </Card>
                  ) : (
                    learner.priorReviews.map((review) => (
                      <Card key={review.id} className="border-slate-200 shadow-sm">
                        <CardContent className="space-y-3 p-4">
                          <div className="flex items-center justify-between gap-3 flex-wrap">
                            <span className="text-sm font-semibold capitalize text-navy">{review.type} Review</span>
                            <span className="text-xs text-muted">{review.date.split('T')[0]}</span>
                          </div>
                          <p className="text-xs text-muted">Expert: {review.reviewerName}</p>
                          <p className="text-sm text-navy">{review.overallComment}</p>
                        </CardContent>
                      </Card>
                    ))
                  )}
                </div>
              </section>
            </div>
          ) : null}
        </AsyncStateWrapper>
      </div>
    </ExpertRouteWorkspace>
  );
}
