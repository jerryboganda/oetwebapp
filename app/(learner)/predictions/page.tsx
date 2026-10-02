'use client';

import { useEffect, useState } from 'react';
import { TrendingUp, RefreshCw, BarChart3, Target } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

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
        const message = result.reason === 'insufficient_data'
          ? 'Need at least 2 completed evaluations.'
          : result.reason === 'score_conversion_unavailable'
            ? 'Reading and Listening predictions require owner-approved score conversion.'
            : result.reason === 'score_conversion_table_mismatch'
              ? 'Predictions are paused until evaluations use one approved conversion table.'
              : 'Cannot compute prediction.';
        setToast({ variant: 'error', message });
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

  return (
    <>
      <LearnerPageHero
        title="Score Estimator"
        description="AI-powered predictions based on your practice history and improvement trends."
        icon={TrendingUp}
      />

      <InlineAlert variant="info" title="AI Practice Score">
        AI Practice Score — not an official OET result. Reading and Listening predictions appear only after owner-approved score conversion is available.
      </InlineAlert>

      {loading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2" aria-hidden="true">
          {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-48 rounded-2xl" />)}
        </div>
      ) : (
        <>
          {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

          <MotionSection>
            <div className="grid grid-cols-1 gap-6 sm:grid-cols-2">
              {SUBTESTS.map((subtest, index) => {
                const pred = getPrediction(subtest);
                const factors = pred ? parseFactors(pred.factorsJson) : null;
                const badge = pred ? CONFIDENCE_BADGE[pred.confidenceLevel] ?? CONFIDENCE_BADGE.insufficient : null;

                return (
                  <MotionItem key={subtest} delayIndex={Math.min(index, 5)} className="h-full">
                    <Card className="flex h-full flex-col">
                      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
                        <h2 className="font-semibold capitalize text-navy">{subtest}</h2>
                        {badge && <Badge variant={badge.variant}>{badge.label}</Badge>}
                      </div>

                      {pred ? (
                        <>
                          {/* Score Range Visualization */}
                          <div className="mb-4">
                            <div className="mb-2 flex items-end gap-1">
                              <span className="text-3xl font-bold tabular-nums text-primary"><CountUp value={pred.predictedScoreMid} /></span>
                              <span className="mb-1 text-sm text-muted">/500</span>
                            </div>
                            <div className="relative h-3 overflow-hidden rounded-full bg-border rtl:-scale-x-100" aria-hidden="true">
                              <div
                                className="absolute h-full rounded-full bg-primary/20"
                                style={{ left: `${(pred.predictedScoreLow / 500) * 100}%`, width: `${((pred.predictedScoreHigh - pred.predictedScoreLow) / 500) * 100}%` }}
                              />
                              <div
                                className="absolute h-full w-1 rounded-full bg-primary"
                                style={{ left: `${(pred.predictedScoreMid / 500) * 100}%` }}
                              />
                            </div>
                            <div className="mt-1 flex justify-between text-xs tabular-nums text-muted">
                              <span>{pred.predictedScoreLow}</span>
                              <span>{pred.predictedScoreHigh}</span>
                            </div>
                          </div>

                          {/* Factors */}
                          {factors && (
                            <div className="flex-1 space-y-1 text-xs text-muted">
                              <p>Based on <strong className="tabular-nums">{factors.evaluationCount}</strong> evaluations</p>
                              <p>Recent average: <strong className="tabular-nums">{factors.recentAverage}</strong></p>
                              <p>Trend: <span className={factors.trendDirection === 'improving' ? 'text-success-strong' : factors.trendDirection === 'declining' ? 'text-danger-strong' : 'text-muted'}>
                                {factors.trendDirection === 'improving' ? '↑ Improving' : factors.trendDirection === 'declining' ? '↓ Declining' : '→ Stable'}
                                {factors.trend != null && ` (${factors.trend > 0 ? '+' : ''}${factors.trend})`}
                              </span></p>
                            </div>
                          )}

                          <div className="mt-3 flex items-center justify-between gap-2">
                            <span className="text-xs text-muted">
                              Updated {new Date(pred.computedAt).toLocaleDateString()}
                            </span>
                            <Button size="sm" variant="ghost" onClick={() => handleCompute(subtest)} disabled={computing === subtest} aria-label="Generate Prediction">
                              <RefreshCw className={`h-3.5 w-3.5 ${computing === subtest ? 'animate-spin' : ''}`} aria-hidden="true" />
                            </Button>
                          </div>
                        </>
                      ) : (
                        <div className="flex flex-1 flex-col items-center justify-center py-6 text-center">
                          <BarChart3 className="mb-2 h-8 w-8 text-muted" aria-hidden="true" />
                          <p className="mb-3 text-sm text-muted">No prediction yet</p>
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
            <MotionSection>
              <Card padding="lg" className="border-primary/30 bg-primary/10">
                <LearnerSurfaceSectionHeader
                  icon={Target}
                  title="Overall AI Practice Score"
                />
                <div className="mt-3 grid grid-cols-1 gap-4 text-center sm:grid-cols-3">
                  <div>
                    <p className="tile-label text-muted">Estimated Range</p>
                    <p className="text-lg font-bold tabular-nums text-primary">
                      {Math.round(predictions.reduce((s, p) => s + p.predictedScoreLow, 0) / predictions.length)}–
                      {Math.round(predictions.reduce((s, p) => s + p.predictedScoreHigh, 0) / predictions.length)}
                    </p>
                  </div>
                  <div>
                    <p className="tile-label text-muted">Best Estimate</p>
                    <p className="text-lg font-bold tabular-nums text-primary">
                      <CountUp value={Math.round(predictions.reduce((s, p) => s + p.predictedScoreMid, 0) / predictions.length)} />
                    </p>
                  </div>
                  <div>
                    <p className="tile-label text-muted">Subtests Analyzed</p>
                    <p className="text-lg font-bold tabular-nums text-primary">{predictions.length}/4</p>
                  </div>
                </div>
              </Card>
            </MotionSection>
          )}
        </>
      )}

      <InlineAlert variant="info" title="How predictions work">
        Predictions are based on your evaluation history using weighted trend analysis. More evaluations improve accuracy.
        Compute predictions regularly to track your progress toward your target score.
      </InlineAlert>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}
