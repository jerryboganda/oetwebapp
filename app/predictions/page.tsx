'use client';

import { useEffect, useState } from 'react';
import { TrendingUp, RefreshCw, BarChart3, Target, AlertCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

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
          <div className="grid grid-cols-2 gap-4">
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
                    <h3 className="font-semibold text-gray-900 dark:text-gray-100 capitalize">{subtest}</h3>
                    {badge && <Badge variant={badge.variant}>{badge.label}</Badge>}
                  </div>

                  {pred ? (
                    <>
                      {/* Score Range Visualization */}
                      <div className="mb-4">
                        <div className="flex items-end gap-1 mb-2">
                          <span className="text-3xl font-bold text-indigo-600 dark:text-indigo-400">{pred.predictedScoreMid}</span>
                          <span className="text-sm text-gray-500 dark:text-gray-400 mb-1">/500</span>
                        </div>
                        <div className="relative h-3 bg-gray-200 dark:bg-gray-700 rounded-full overflow-hidden">
                          <div
                            className="absolute h-full bg-indigo-200 dark:bg-indigo-800 rounded-full"
                            style={{ left: `${(pred.predictedScoreLow / 500) * 100}%`, width: `${((pred.predictedScoreHigh - pred.predictedScoreLow) / 500) * 100}%` }}
                          />
                          <div
                            className="absolute h-full w-1 bg-indigo-600 dark:bg-indigo-400 rounded-full"
                            style={{ left: `${(pred.predictedScoreMid / 500) * 100}%` }}
                          />
                        </div>
                        <div className="flex justify-between mt-1 text-xs text-gray-400">
                          <span>{pred.predictedScoreLow}</span>
                          <span>{pred.predictedScoreHigh}</span>
                        </div>
                      </div>

                      {/* Factors */}
                      {factors && (
                        <div className="space-y-1 text-xs text-gray-600 dark:text-gray-400 flex-1">
                          <p>Based on <strong>{factors.evaluationCount}</strong> evaluations</p>
                          <p>Recent average: <strong>{factors.recentAverage}</strong></p>
                          <p>Trend: <span className={factors.trendDirection === 'improving' ? 'text-green-600 dark:text-green-400' : factors.trendDirection === 'declining' ? 'text-red-500' : 'text-gray-500'}>
                            {factors.trendDirection === 'improving' ? '↑ Improving' : factors.trendDirection === 'declining' ? '↓ Declining' : '→ Stable'}
                            {factors.trend != null && ` (${factors.trend > 0 ? '+' : ''}${factors.trend})`}
                          </span></p>
                        </div>
                      )}

                      <div className="mt-3 flex items-center justify-between">
                        <span className="text-xs text-gray-400">
                          Updated {new Date(pred.computedAt).toLocaleDateString()}
                        </span>
                        <Button size="sm" variant="ghost" onClick={() => handleCompute(subtest)} disabled={computing === subtest}>
                          <RefreshCw className={`w-3.5 h-3.5 ${computing === subtest ? 'animate-spin' : ''}`} />
                        </Button>
                      </div>
                    </>
                  ) : (
                    <div className="flex flex-col items-center justify-center flex-1 py-6 text-center">
                      <BarChart3 className="w-8 h-8 text-gray-300 dark:text-gray-600 mb-2" />
                      <p className="text-sm text-gray-500 dark:text-gray-400 mb-3">No prediction yet</p>
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
          <Card className="p-5 bg-indigo-50 dark:bg-indigo-900/10 border-indigo-200 dark:border-indigo-800">
            <LearnerSurfaceSectionHeader
              icon={<Target className="w-5 h-5" />}
              title="Overall OET Prediction"
            />
            <div className="mt-3 grid grid-cols-3 gap-4 text-center">
              <div>
                <p className="text-xs text-gray-500">Estimated Range</p>
                <p className="text-lg font-bold text-indigo-700 dark:text-indigo-300">
                  {Math.round(predictions.reduce((s, p) => s + p.predictedScoreLow, 0) / predictions.length)}–
                  {Math.round(predictions.reduce((s, p) => s + p.predictedScoreHigh, 0) / predictions.length)}
                </p>
              </div>
              <div>
                <p className="text-xs text-gray-500">Best Estimate</p>
                <p className="text-lg font-bold text-indigo-700 dark:text-indigo-300">
                  {Math.round(predictions.reduce((s, p) => s + p.predictedScoreMid, 0) / predictions.length)}
                </p>
              </div>
              <div>
                <p className="text-xs text-gray-500">Subtests Analyzed</p>
                <p className="text-lg font-bold text-indigo-700 dark:text-indigo-300">{predictions.length}/4</p>
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
