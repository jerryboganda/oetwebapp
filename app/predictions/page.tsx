'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { BarChart3, RefreshCw, Target, TrendingUp } from 'lucide-react';
import { useEffect, useState } from 'react';

interface Prediction {
  id: string;
  examTypeCode: string;
  subtestCode: string;
  predictedScoreLow: number;
  predictedScoreHigh: number;
  predictedScoreMid: number;
  confidenceLevel: string;
  factorsJson: string;
  evaluationCount: number;
  computedAt: string;
}

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const SUBTESTS = ['writing', 'speaking', 'reading', 'listening'];

const CONFIDENCE_BADGE: Record<string, { label: string; variant: 'default' | 'success' | 'danger' | 'outline' }> = {
  good: { label: 'High Confidence', variant: 'success' },
  moderate: { label: 'Moderate', variant: 'default' },
  low: { label: 'Low Confidence', variant: 'outline' },
  insufficient: { label: 'Insufficient Data', variant: 'danger' },
};

const apiRequest = apiClient.request;

export default function ScoreEstimatorPage() {
  const [predictions, setPredictions] = useState<Prediction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [computing, setComputing] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'score-estimator' });
    loadPredictions();
  }, []);

  async function loadPredictions() {
    try {
      setLoading(true);
      const data = await apiRequest<Prediction[]>('/v1/predictions?examTypeCode=oet');
      setPredictions(Array.isArray(data) ? data : []);
    } catch {
      setError('Unable to load predictions.');
    } finally {
      setLoading(false);
    }
  }

  async function handleCompute(subtestCode: string) {
    setComputing(subtestCode);
    try {
      const result = await apiRequest<{ available: boolean; prediction?: Prediction; reason?: string }>(
        '/v1/predictions/compute',
        { method: 'POST', body: JSON.stringify({ examTypeCode: 'oet', subtestCode }) },
      );
      if (result.available && result.prediction) {
        setPredictions((prev) => {
          const filtered = prev.filter((p) => p.subtestCode !== subtestCode);
          return [...filtered, result.prediction!];
        });
        setToast({ variant: 'success', message: `${subtestCode} prediction updated.` });
      } else {
        setToast({ variant: 'error', message: result.reason === 'insufficient_data' ? 'Need at least 2 completed evaluations.' : 'Cannot compute prediction.' });
      }
    } catch {
      setToast({ variant: 'error', message: 'Failed to compute prediction.' });
    } finally {
      setComputing(null);
    }
  }

  function getPrediction(subtestCode: string) {
    return predictions.find((p) => p.subtestCode === subtestCode);
  }

  function parseFactors(json: string) {
    try { return JSON.parse(json); } catch { return null; }
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-60" />
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-48 rounded-xl" />)}
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <LearnerPageHero
        title="Score Estimator"
        description="AI-powered predictions based on your practice history and improvement trends."
        icon={<TrendingUp className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      <MotionSection>
        <div className="grid gap-6 sm:grid-cols-2">
          {SUBTESTS.map((subtest) => {
            const pred = getPrediction(subtest);
            const factors = pred ? parseFactors(pred.factorsJson) : null;
            const badge = pred ? CONFIDENCE_BADGE[pred.confidenceLevel] ?? CONFIDENCE_BADGE.insufficient : null;

            return (
              <MotionItem key={subtest}>
                <Card className="p-5 h-full flex flex-col">
                  <div className="flex items-center justify-between mb-4">
                    <h3 className="font-semibold text-navy capitalize">{subtest}</h3>
                    {badge && <Badge variant={badge.variant}>{badge.label}</Badge>}
                  </div>

                  {pred ? (
                    <>
                      {/* Score Range Visualization */}
                      <div className="mb-4">
                        <div className="flex items-end gap-1 mb-2">
                          <span className="text-3xl font-bold text-primary">{pred.predictedScoreMid}</span>
                          <span className="text-sm text-muted mb-1">/500</span>
                        </div>
                        <div className="relative h-3 bg-border rounded-full overflow-hidden">
                          <div
                            className="absolute h-full bg-primary/20 rounded-full"
                            style={{ left: `${(pred.predictedScoreLow / 500) * 100}%`, width: `${((pred.predictedScoreHigh - pred.predictedScoreLow) / 500) * 100}%` }}
                          />
                          <div
                            className="absolute h-full w-1 bg-primary rounded-full"
                            style={{ left: `${(pred.predictedScoreMid / 500) * 100}%` }}
                          />
                        </div>
                        <div className="flex justify-between mt-1 text-xs text-muted/60">
                          <span>{pred.predictedScoreLow}</span>
                          <span>{pred.predictedScoreHigh}</span>
                        </div>
                      </div>

                      {/* Factors */}
                      {factors && (
                        <div className="space-y-1 text-xs text-muted flex-1">
                          <p>Based on <strong>{factors.evaluationCount}</strong> evaluations</p>
                          <p>Recent average: <strong>{factors.recentAverage}</strong></p>
                          <p>Trend: <span className={factors.trendDirection === 'improving' ? 'text-success' : factors.trendDirection === 'declining' ? 'text-danger' : 'text-muted'}>
                            {factors.trendDirection === 'improving' ? '↑ Improving' : factors.trendDirection === 'declining' ? '↓ Declining' : '→ Stable'}
                            {factors.trend != null && ` (${factors.trend > 0 ? '+' : ''}${factors.trend})`}
                          </span></p>
                        </div>
                      )}

                      <div className="mt-3 flex items-center justify-between">
                        <span className="text-xs text-muted/60">
                          Updated {new Date(pred.computedAt).toLocaleDateString()}
                        </span>
                        <Button size="sm" variant="ghost" onClick={() => handleCompute(subtest)} disabled={computing === subtest}>
                          <RefreshCw className={`w-3.5 h-3.5 ${computing === subtest ? 'animate-spin' : ''}`} />
                        </Button>
                      </div>
                    </>
                  ) : (
                    <div className="flex flex-col items-center justify-center flex-1 py-6 text-center">
                      <BarChart3 className="w-8 h-8 text-muted/40 mb-2" />
                      <p className="text-sm text-muted mb-3">No prediction yet</p>
                      <Button size="sm" onClick={() => handleCompute(subtest)} disabled={computing === subtest}>
                        {computing === subtest ? 'Computing…' : 'Generate Prediction'}
                      </Button>
                    </div>
                  )}
                </Card>
              </MotionItem>
            );
          })}
        </div>
      </MotionSection>

      {/* Overall prediction summary */}
      {predictions.length >= 2 && (
        <MotionSection className="mt-6">
          <Card className="p-5 bg-primary/10 border-primary/30">
            <LearnerSurfaceSectionHeader
              icon={<Target className="w-5 h-5" />}
              title="Overall OET Prediction"
            />
            <div className="mt-3 grid grid-cols-1 sm:grid-cols-3 gap-4 text-center">
              <div>
                <p className="text-xs text-muted">Estimated Range</p>
                <p className="text-lg font-bold text-primary">
                  {Math.round(predictions.reduce((s, p) => s + p.predictedScoreLow, 0) / predictions.length)}–
                  {Math.round(predictions.reduce((s, p) => s + p.predictedScoreHigh, 0) / predictions.length)}
                </p>
              </div>
              <div>
                <p className="text-xs text-muted">Best Estimate</p>
                <p className="text-lg font-bold text-primary">
                  {Math.round(predictions.reduce((s, p) => s + p.predictedScoreMid, 0) / predictions.length)}
                </p>
              </div>
              <div>
                <p className="text-xs text-muted">Subtests Analyzed</p>
                <p className="text-lg font-bold text-primary">{predictions.length}/4</p>
              </div>
            </div>
          </Card>
        </MotionSection>
      )}

      <MotionSection className="mt-6">
        <InlineAlert variant="info" title="How predictions work">
          Predictions are based on your evaluation history using weighted trend analysis. More evaluations improve accuracy.
          Compute predictions regularly to track your progress toward your target score.
        </InlineAlert>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
