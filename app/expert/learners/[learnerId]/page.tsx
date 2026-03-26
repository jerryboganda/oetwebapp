'use client';

import { useState, useEffect } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import type { LearnerProfileExpanded } from '@/lib/types/expert';
import { useParams, useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { ArrowLeft, Target, History, BarChart3, FileText } from 'lucide-react';
import { fetchLearnerProfile } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success';

export default function AssignedLearnerPage() {
  const params = useParams();
  const learnerId = params?.learnerId as string | undefined;
  const router = useRouter();

  const [learner, setLearner] = useState<LearnerProfileExpanded | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');

  useEffect(() => {
    if (!learnerId) return;
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        const data = await fetchLearnerProfile(learnerId);
        if (cancelled) return;
        setLearner(data);
        setPageStatus('success');
        analytics.track('learner_profile_viewed', { learnerId });
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    })();
    return () => { cancelled = true; };
  }, [learnerId]);

  return (
    <div className="max-w-4xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Learner Profile">
      <Button variant="ghost" className="pl-0 text-muted" onClick={() => router.back()} aria-label="Go back">
        <ArrowLeft className="w-4 h-4 mr-2" /> Back
      </Button>

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {learner && (
          <>
            <div className="flex items-center justify-between">
              <div>
                <h1 className="text-2xl font-bold text-navy">{learner.name}</h1>
                <p className="text-muted text-sm mt-1">Learner ID: {learnerId}</p>
              </div>
              <Badge variant="info" className="capitalize text-sm px-3 py-1">{learner.profession}</Badge>
            </div>

            <InlineAlert variant="info" title="Privacy Notice">
              Learner data is shared in a limited context for review purposes only. Do not share externally.
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

            {/* Sub-Test Performance */}
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
                    {learner.subTestScores.map(st => (
                      <tr key={st.subTest} className="border-b border-gray-100 last:border-0">
                        <td className="p-3 capitalize font-medium text-navy">{st.subTest}</td>
                        <td className="p-3">{st.latestScore ?? '-'}</td>
                        <td className="p-3">{st.latestGrade ? <Badge variant={st.latestGrade.startsWith('A') || st.latestGrade.startsWith('B') ? 'success' : 'warning'}>{st.latestGrade}</Badge> : '-'}</td>
                        <td className="p-3">{st.attempts}</td>
                      </tr>
                    ))}
                    {learner.subTestScores.length === 0 && (
                      <tr><td colSpan={4} className="p-4 text-center text-muted italic">No sub-test data available.</td></tr>
                    )}
                  </tbody>
                </table>
              </CardContent>
            </Card>

            {/* Prior Expert Reviews */}
            <Card>
              <div className="p-4 border-b border-gray-100 font-semibold text-navy flex items-center gap-2">
                <FileText className="w-4 h-4" /> Prior Expert Reviews
              </div>
              <CardContent className="p-4 space-y-4">
                {learner.priorReviews.length === 0 ? (
                  <p className="italic text-muted text-sm">No prior expert feedback available for this learner.</p>
                ) : (
                  learner.priorReviews.map(review => (
                    <div key={review.id} className="p-3 border border-gray-200 rounded-md">
                      <div className="flex items-center justify-between mb-1">
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
