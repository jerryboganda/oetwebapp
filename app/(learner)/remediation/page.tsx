'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, ArrowRight, BarChart3 } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface WeakArea {
  subtestCode: string;
  criterionCode: string;
  averageScore: number;
  evaluationCount: number;
  trend: string;
}

interface Resource {
  id: string;
  title: string;
  resourceType: string;
  difficulty: string;
  displayOrder: number;
}

interface RemediationData {
  evaluationsAnalyzed: number;
  weakAreas: WeakArea[];
  availableResources: Resource[];
  recommendations: { area: WeakArea; suggestedResources: { id: string; title: string }[] }[];
}

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const apiRequest = apiClient.request;

export default function RemediationPage() {
  const [data, setData] = useState<RemediationData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [starting, setStarting] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'remediation' });
    apiRequest<RemediationData>('/v1/learner/remediation')
      .then(setData)
      .catch(() => setError('Unable to load remediation data.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleStart(subtestCode: string, criterionCode: string) {
    setStarting(`${subtestCode}:${criterionCode}`);
    try {
      await apiRequest('/v1/learner/remediation/start', {
        method: 'POST',
        body: JSON.stringify({ subtestCode, criterionCode }),
      });
      setToast({ variant: 'success', message: `Remediation session started for ${subtestCode} · ${criterionCode}` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to start remediation session.' });
    } finally {
      setStarting(null);
    }
  }

  return (
    <>
      <LearnerPageHero
        title="Weak-Area Remediation"
        description="Targeted practice to strengthen your weakest areas based on evaluation analysis."
        icon={AlertTriangle}
      />

      {loading ? (
        <div className="space-y-3" aria-hidden="true">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-60 rounded-2xl" />
        </div>
      ) : null}

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {data && data.evaluationsAnalyzed === 0 && (
        <LearnerEmptyState
          icon={BarChart3}
          title="Not enough data"
          description="Complete some evaluations first so we can identify areas for improvement."
          primaryAction={{ label: 'Start Practice', href: '/study-plan' }}
        />
      )}

      {data && data.evaluationsAnalyzed > 0 && data.weakAreas.length === 0 && (
        <LearnerEmptyState
          icon={BarChart3}
          title="No weak areas identified"
          description={`Based on ${data.evaluationsAnalyzed} recent evaluations`}
        />
      )}

      {/* Weak Areas */}
      {data && data.weakAreas.length > 0 && (
        <section>
          <LearnerSurfaceSectionHeader
            icon={BarChart3}
            title="Identified Weak Areas"
            description={`Based on ${data.evaluationsAnalyzed} recent evaluations`}
            className="mb-4"
          />
          <div className="space-y-3">
            {data.weakAreas.map((wa, i) => (
              <MotionItem key={`${wa.subtestCode}-${wa.criterionCode}`} delayIndex={Math.min(i, 5)}>
                <Card padding="sm" className="flex flex-wrap items-center gap-3 sm:flex-nowrap sm:gap-4">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-danger/10">
                    <span className="text-sm font-bold tabular-nums text-danger-strong">#{i + 1}</span>
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="font-medium capitalize text-navy">
                      {wa.subtestCode} · {wa.criterionCode}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
                      <span className="text-sm tabular-nums text-muted">Avg: {wa.averageScore}/6</span>
                      <span className="text-sm tabular-nums text-muted">{wa.evaluationCount} evals</span>
                      {/* "insufficient_data" is not a trend: no badge rather than a false "Stable". */}
                      {wa.trend !== 'insufficient_data' ? (
                        <Badge variant={wa.trend === 'improving' ? 'success' : wa.trend === 'declining' ? 'danger' : 'outline'}>
                          {wa.trend === 'improving' ? '↑ Improving' : wa.trend === 'declining' ? '↓ Declining' : 'Stable'}
                        </Badge>
                      ) : null}
                    </div>
                  </div>
                  <Button
                    size="sm"
                    className="shrink-0"
                    onClick={() => handleStart(wa.subtestCode, wa.criterionCode)}
                    disabled={starting === `${wa.subtestCode}:${wa.criterionCode}`}
                  >
                    {starting === `${wa.subtestCode}:${wa.criterionCode}` ? 'Starting…' : 'Practice'}
                    <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                  </Button>
                </Card>
              </MotionItem>
            ))}
          </div>
        </section>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}
