'use client';

import { useState, useEffect } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import type { ExpertLearnerReviewContext, LearnerProfileExpanded } from '@/lib/types/expert';
import { useParams, useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { ArrowLeft, Target, History, BarChart3, FileText } from 'lucide-react';
import { fetchExpertLearnerReviewContext, fetchLearnerProfile, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';

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
    <div className="max-w-4xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Learner Profile">
      <Button variant="ghost" className="pl-0 text-muted" onClick={() => router.back()} aria-label="Go back">
        <ArrowLeft className="w-4 h-4 mr-2" /> Back
      </Button>

      <AsyncStateWrapper status={pageStatus} onRetry={() => setReloadToken((current) => current + 1)} errorMessage={errorMessage ?? undefined}>
        {learner && (
          <>
            <div className="flex items-center justify-between gap-4 flex-wrap">
              <div>
                <h1 className="text-2xl font-bold text-navy">{learner.name}</h1>
                <p className="text-muted text-sm mt-1">Learner ID: {learnerId}</p>
              </div>
              <div className="flex items-center gap-2 flex-wrap">
                <Badge variant="info" className="capitalize text-sm px-3 py-1">{learner.profession}</Badge>
                {learner.visibilityScope && <Badge variant="default">{learner.visibilityScope.replace(/_/g, ' ')}</Badge>}
                {context && <Badge variant="success">{context.reviewsInScope} reviews in scope</Badge>}
              </div>
            </div>

            <InlineAlert variant="info" title="Privacy Notice">
              Learner context is shown only to support the reviews assigned to you. Use it for review decisions only and do not share it outside the console.
            </InlineAlert>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <Card>
                <div className="p-4 border-b border-gray-100 font-semibold text-navy flex items-center gap-2">
                  <Target className="w-4 h-4" /> Goal & Context
                </div>
                <CardContent className="p-4 space-y-4">
                  <div>
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide">Target Score</p>
                    <p className="text-navy mt-1 font-medium">{learner.goalScore}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide">Exam Date</p>
                    <p className="text-navy mt-1">{learner.examDate ? learner.examDate.split('T')[0] : 'Not Set'}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide">Review Scope</p>
                    <p className="text-navy mt-1">{context ? `${context.reviewsInScope} expert-linked review(s)` : 'Loading scope...'}</p>
                  </div>
                </CardContent>
              </Card>

              <Card>
                <div className="p-4 border-b border-gray-100 font-semibold text-navy flex items-center gap-2">
                  <History className="w-4 h-4" /> Platform History
                </div>
                <CardContent className="p-4 space-y-4">
                  <div>
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide">Joined</p>
                    <p className="text-navy mt-1">{learner.joinedAt.split('T')[0]}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide">Total Reviews</p>
                    <p className="text-navy mt-1">{learner.totalReviews}</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold text-muted uppercase tracking-wide">Submissions</p>
                    <p className="text-navy mt-1">{learner.attemptsCount}</p>
                  </div>
                </CardContent>
              </Card>
            </div>

            <Card>
              <div className="p-4 border-b border-gray-100 font-semibold text-navy flex items-center gap-2">
                <BarChart3 className="w-4 h-4" /> Sub-Test Performance
              </div>
              <CardContent className="p-0">
                <table className="w-full text-sm" aria-label="Sub-test performance scores">
                  <thead className="bg-gray-50 border-b border-gray-200">
                    <tr>
                      <th scope="col" className="text-left p-3 font-semibold text-muted">Sub-Test</th>
                      <th scope="col" className="text-left p-3 font-semibold text-muted">Latest Score</th>
                      <th scope="col" className="text-left p-3 font-semibold text-muted">Grade</th>
                      <th scope="col" className="text-left p-3 font-semibold text-muted">Attempts</th>
                    </tr>
                  </thead>
                  <tbody>
                    {learner.subTestScores.map((subTestScore) => (
                      <tr key={subTestScore.subTest} className="border-b border-gray-100 last:border-0">
                        <td className="p-3 capitalize font-medium text-navy">{subTestScore.subTest}</td>
                        <td className="p-3">{subTestScore.latestScore ?? '-'}</td>
                        <td className="p-3">{subTestScore.latestGrade ? <Badge variant={subTestScore.latestGrade.startsWith('A') || subTestScore.latestGrade.startsWith('B') ? 'success' : 'warning'}>{subTestScore.latestGrade}</Badge> : '-'}</td>
                        <td className="p-3">{subTestScore.attempts}</td>
                      </tr>
                    ))}
                    {learner.subTestScores.length === 0 && (
                      <tr><td colSpan={4} className="p-4 text-center text-muted italic">No sub-test data is available yet.</td></tr>
                    )}
                  </tbody>
                </table>
              </CardContent>
            </Card>

            <Card>
              <div className="p-4 border-b border-gray-100 font-semibold text-navy flex items-center gap-2">
                <FileText className="w-4 h-4" /> Prior Expert Reviews
              </div>
              <CardContent className="p-4 space-y-4">
                {learner.priorReviews.length === 0 ? (
                  <p className="italic text-muted text-sm">No prior expert feedback is available for this learner.</p>
                ) : (
                  learner.priorReviews.map((review) => (
                    <div key={review.id} className="p-3 border border-gray-200 rounded-md">
                      <div className="flex items-center justify-between mb-1 gap-3 flex-wrap">
                        <span className="text-sm font-medium text-navy capitalize">{review.type} Review</span>
                        <span className="text-xs text-muted">{review.date.split('T')[0]}</span>
                      </div>
                      <p className="text-xs text-muted mb-1">Expert: {review.reviewerName}</p>
                      <p className="text-sm text-navy">{review.overallComment}</p>
                    </div>
                  ))
                )}
              </CardContent>
            </Card>
          </>
        )}
      </AsyncStateWrapper>
    </div>
  );
}
