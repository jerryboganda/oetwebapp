'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, ArrowRight, BookOpen, BarChart3, Lightbulb } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

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

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...init?.headers },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

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
      setToast({ variant: 'success', message: `Remediation session started for ${subtestCode} — ${criterionCode}` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to start remediation session.' });
    } finally {
      setStarting(null);
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-60" />
          <Skeleton className="h-40 rounded-xl" />
          <Skeleton className="h-60 rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <LearnerPageHero
        title="Weak-Area Remediation"
        description="Targeted practice to strengthen your weakest areas based on evaluation analysis."
        icon={<AlertTriangle className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {data && data.evaluationsAnalyzed === 0 && (
        <InlineAlert variant="info" title="Not enough data">
          Complete some evaluations first so we can identify areas for improvement.
        </InlineAlert>
      )}

      {/* Weak Areas */}
      {data && data.weakAreas.length > 0 && (
        <MotionSection>
          <LearnerSurfaceSectionHeader icon={<BarChart3 className="w-5 h-5" />} title="Identified Weak Areas" />
          <p className="text-sm text-muted mb-4">Based on {data.evaluationsAnalyzed} recent evaluations</p>
          <div className="space-y-3">
            {data.weakAreas.map((wa, i) => (
              <MotionItem key={`${wa.subtestCode}-${wa.criterionCode}`}>
                <Card className="p-4 flex items-center gap-4">
                  <div className="w-10 h-10 rounded-full bg-danger/10 flex items-center justify-center flex-shrink-0">
                    <span className="text-sm font-bold text-danger">#{i + 1}</span>
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="font-medium text-navy capitalize">
                      {wa.subtestCode} — {wa.criterionCode}
                    </p>
                    <div className="flex items-center gap-3 mt-1">
                      <span className="text-sm text-muted">Avg: {wa.averageScore}/6</span>
                      <span className="text-sm text-muted">{wa.evaluationCount} evals</span>
                      <Badge variant={wa.trend === 'improving' ? 'success' : wa.trend === 'declining' ? 'danger' : 'outline'}>
                        {wa.trend === 'improving' ? '↑ Improving' : wa.trend === 'declining' ? '↓ Declining' : 'Stable'}
                      </Badge>
                    </div>
                  </div>
                  <Button
                    size="sm"
                    onClick={() => handleStart(wa.subtestCode, wa.criterionCode)}
                    disabled={starting === `${wa.subtestCode}:${wa.criterionCode}`}
                  >
                    {starting === `${wa.subtestCode}:${wa.criterionCode}` ? 'Starting…' : 'Practice'}
                    <ArrowRight className="w-4 h-4 ml-1" />
                  </Button>
                </Card>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}

      {/* Recommendations */}
      {data && data.recommendations.length > 0 && (
        <MotionSection className="mt-6">
          <LearnerSurfaceSectionHeader icon={<Lightbulb className="w-5 h-5" />} title="Recommended Resources" />
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 mt-3">
            {data.recommendations.map((rec, i) => (
              <MotionItem key={i}>
                <Card className="p-4">
                  <p className="font-medium text-sm capitalize text-navy">
                    {rec.area.subtestCode} — {rec.area.criterionCode}
                  </p>
                  <ul className="mt-2 space-y-1">
                    {rec.suggestedResources.map((r) => (
                      <li key={r.id} className="text-xs text-muted flex items-center gap-1">
                        <BookOpen className="w-3 h-3" /> {r.title}
                      </li>
                    ))}
                  </ul>
                </Card>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}

      {/* Available Resources */}
      {data && data.availableResources.length > 0 && (
        <MotionSection className="mt-6">
          <LearnerSurfaceSectionHeader icon={<BookOpen className="w-5 h-5" />} title="Foundation Resources" />
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 mt-3">
            {data.availableResources.map((r) => (
              <MotionItem key={r.id}>
                <Card className="p-3">
                  <Badge variant="outline" className="mb-2">{r.resourceType.replace(/_/g, ' ')}</Badge>
                  <p className="text-sm font-medium text-navy">{r.title}</p>
                  <p className="text-xs text-muted mt-1 capitalize">{r.difficulty}</p>
                </Card>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}
    </LearnerDashboardShell>
  );
}
