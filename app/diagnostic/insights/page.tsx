'use client';

import { useEffect, useState } from 'react';
import { Compass, ArrowRight, Brain, BarChart3 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

interface SubtestAnalysis {
  subtestCode: string;
  scoreRange: string | null;
  weakCriteria: { code: string; label: string; score: string }[];
  recommendation: string;
}

interface DiagnosticData {
  hasDiagnostic: boolean;
  diagnosticDate?: string;
  subtestAnalysis?: SubtestAnalysis[];
  priorityOrder?: { rank: number; analysis: SubtestAnalysis }[];
  suggestedWeeklyFocus?: Record<string, number>;
  message?: string;
}

async function apiRequest<T = unknown>(path: string): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function DiagnosticInsightsPage() {
  const [data, setData] = useState<DiagnosticData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'diagnostic-insights' });
    apiRequest<DiagnosticData>('/v1/learner/diagnostic-personalization')
      .then(setData)
      .catch(() => setError('Unable to load diagnostic insights.'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-60" />
          <Skeleton className="h-60 rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Diagnostic Insights"
        subtitle="Personalized analysis and study recommendations based on your diagnostic evaluation."
        icon={<Compass className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {data && !data.hasDiagnostic && (
        <InlineAlert variant="info" title="No diagnostic yet">
          {data.message ?? 'Complete a diagnostic test to unlock personalized insights.'}
        </InlineAlert>
      )}

      {data?.hasDiagnostic && data.subtestAnalysis && (
        <>
          {/* Priority Order */}
          {data.priorityOrder && (
            <MotionSection>
              <LearnerSurfaceSectionHeader icon={<Brain className="w-5 h-5" />} title="Focus Priority" />
              <p className="text-sm text-gray-500 mb-4">
                Diagnostic from {data.diagnosticDate ? new Date(data.diagnosticDate).toLocaleDateString() : 'recently'}
              </p>
              <div className="space-y-3">
                {data.priorityOrder.map((po) => (
                  <MotionItem key={po.rank}>
                    <Card className="p-4 flex items-start gap-4">
                      <div className="w-8 h-8 rounded-full bg-indigo-100 dark:bg-indigo-900/30 flex items-center justify-center flex-shrink-0">
                        <span className="text-sm font-bold text-indigo-600 dark:text-indigo-400">#{po.rank}</span>
                      </div>
                      <div className="flex-1">
                        <div className="flex items-center gap-2 mb-1">
                          <h4 className="font-semibold text-gray-900 dark:text-gray-100 capitalize">{po.analysis.subtestCode}</h4>
                          {po.analysis.scoreRange && (
                            <Badge variant="outline">{po.analysis.scoreRange}</Badge>
                          )}
                        </div>
                        <p className="text-sm text-gray-600 dark:text-gray-400">{po.analysis.recommendation}</p>
                        {po.analysis.weakCriteria.length > 0 && (
                          <div className="flex flex-wrap gap-1 mt-2">
                            {po.analysis.weakCriteria.map((wc) => (
                              <Badge key={wc.code} variant="destructive" className="text-xs">
                                {wc.code}: {wc.score}
                              </Badge>
                            ))}
                          </div>
                        )}
                      </div>
                    </Card>
                  </MotionItem>
                ))}
              </div>
            </MotionSection>
          )}

          {/* Weekly Focus Allocation */}
          {data.suggestedWeeklyFocus && (
            <MotionSection className="mt-6">
              <LearnerSurfaceSectionHeader icon={<BarChart3 className="w-5 h-5" />} title="Suggested Weekly Time Allocation" />
              <Card className="p-5 mt-3">
                <div className="grid grid-cols-4 gap-4">
                  {Object.entries(data.suggestedWeeklyFocus).map(([subtest, percent]) => (
                    <div key={subtest} className="text-center">
                      <div className="relative h-24 flex items-end justify-center mb-2">
                        <div
                          className="w-12 bg-indigo-500 dark:bg-indigo-400 rounded-t-lg transition-all"
                          style={{ height: `${percent}%` }}
                        />
                      </div>
                      <p className="text-xs font-medium capitalize text-gray-900 dark:text-gray-100">{subtest}</p>
                      <p className="text-xs text-gray-500">{percent}%</p>
                    </div>
                  ))}
                </div>
              </Card>
            </MotionSection>
          )}

          {/* Per-subtest analysis */}
          <MotionSection className="mt-6">
            <LearnerSurfaceSectionHeader icon={<Compass className="w-5 h-5" />} title="Detailed Analysis" />
            <div className="grid gap-4 sm:grid-cols-2 mt-3">
              {data.subtestAnalysis.map((sa) => (
                <MotionItem key={sa.subtestCode}>
                  <Card className="p-4">
                    <div className="flex items-center justify-between mb-2">
                      <h4 className="font-semibold text-gray-900 dark:text-gray-100 capitalize">{sa.subtestCode}</h4>
                      {sa.scoreRange && <Badge variant="outline">{sa.scoreRange}</Badge>}
                    </div>
                    <p className="text-sm text-gray-600 dark:text-gray-400 mb-3">{sa.recommendation}</p>
                    {sa.weakCriteria.length > 0 ? (
                      <div>
                        <p className="text-xs font-medium text-gray-500 mb-1">Weak criteria:</p>
                        <div className="flex flex-wrap gap-1">
                          {sa.weakCriteria.map((wc) => (
                            <Badge key={wc.code} variant="destructive" className="text-xs">{wc.code}: {wc.score}</Badge>
                          ))}
                        </div>
                      </div>
                    ) : (
                      <Badge variant="success" className="text-xs">Strong performance</Badge>
                    )}
                  </Card>
                </MotionItem>
              ))}
            </div>
          </MotionSection>
        </>
      )}
    </LearnerDashboardShell>
  );
}
