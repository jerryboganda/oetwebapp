'use client';

import { useEffect, useState } from 'react';
import { BarChart, Bar, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, LineChart, Line } from 'recharts';
import { Shield, Target, TrendingUp, BarChart3 } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Card, CardContent } from '@/components/ui/card';
import { Select } from '@/components/ui/form-controls';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success';

interface ScoringDistribution {
  criterion: string;
  mean: number;
  stdDev: number;
  min: number;
  max: number;
  count: number;
}

interface AiHumanAgreement {
  comparisons: number;
  averageDifference: number;
  maxDifference: number;
  agreementRate: number;
}

interface CalibrationPoint {
  date: string;
  averageScore: number;
}

interface ScoringQualityData {
  totalReviewsInWindow: number;
  days: number;
  scoringDistribution: ScoringDistribution[];
  aiHumanAgreement: AiHumanAgreement;
  calibrationTrend: CalibrationPoint[];
  reworkRate: number;
}

async function fetchScoringQuality(days: number): Promise<ScoringQualityData> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}/v1/expert/scoring-quality?days=${days}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function ScoringQualityPage() {
  const [data, setData] = useState<ScoringQualityData | null>(null);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [days, setDays] = useState('30');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    analytics.track('content_view', { page: 'expert-scoring-quality' });

    async function load() {
      try {
        setStatus('loading');
        const result = await fetchScoringQuality(Number(days));
        if (!cancelled) { setData(result); setStatus('success'); }
      } catch {
        if (!cancelled) { setErrorMsg('Unable to load scoring quality data.'); setStatus('error'); }
      }
    }
    load();
    return () => { cancelled = true; };
  }, [days]);

  return (
    <ExpertRouteWorkspace>
      <ExpertRouteHero
        title="Scoring Quality"
        subtitle="Track your calibration accuracy, AI-human agreement, and scoring consistency."
      />

      <div className="mb-4 flex justify-end">
        <Select value={days} onChange={(e) => setDays(e.target.value)}>
          <option value="7">Last 7 days</option>
          <option value="30">Last 30 days</option>
          <option value="90">Last 90 days</option>
          <option value="180">Last 180 days</option>
        </Select>
      </div>

      <AsyncStateWrapper status={status} errorMessage={errorMsg} onRetry={() => setDays(days)}>
        {data && (
          <>
            {/* Summary cards */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
              <ExpertRouteSummaryCard label="Reviews Analyzed" value={data.totalReviewsInWindow} icon={<BarChart3 className="w-5 h-5" />} />
              <ExpertRouteSummaryCard label="AI Agreement Rate" value={`${data.aiHumanAgreement.agreementRate}%`} icon={<Target className="w-5 h-5" />} />
              <ExpertRouteSummaryCard label="Avg Score Diff" value={data.aiHumanAgreement.averageDifference.toFixed(2)} icon={<Shield className="w-5 h-5" />} />
              <ExpertRouteSummaryCard label="Rework Rate" value={`${data.reworkRate}%`} icon={<TrendingUp className="w-5 h-5" />} />
            </div>

            {/* Scoring Distribution */}
            {data.scoringDistribution.length > 0 && (
              <>
                <ExpertRouteSectionHeader title="Scoring Distribution by Criterion" />
                <Card className="mb-6">
                  <CardContent className="pt-4">
                    <ResponsiveContainer width="100%" height={280}>
                      <BarChart data={data.scoringDistribution}>
                        <CartesianGrid strokeDasharray="3 3" />
                        <XAxis dataKey="criterion" fontSize={12} />
                        <YAxis domain={[0, 6]} />
                        <Tooltip />
                        <Bar dataKey="mean" fill="#6366f1" name="Mean Score" radius={[4, 4, 0, 0]} />
                      </BarChart>
                    </ResponsiveContainer>
                    <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 mt-4">
                      {data.scoringDistribution.map((sd) => (
                        <div key={sd.criterion} className="text-xs p-2 rounded-lg bg-gray-50 dark:bg-gray-800">
                          <p className="font-medium capitalize">{sd.criterion}</p>
                          <p className="text-gray-500">μ={sd.mean} σ={sd.stdDev} n={sd.count}</p>
                          <p className="text-gray-500">Range: {sd.min}–{sd.max}</p>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </>
            )}

            {/* Calibration Trend */}
            {data.calibrationTrend.length > 0 && (
              <>
                <ExpertRouteSectionHeader title="Calibration Trend Over Time" />
                <Card className="mb-6">
                  <CardContent className="pt-4">
                    <ResponsiveContainer width="100%" height={220}>
                      <LineChart data={data.calibrationTrend}>
                        <CartesianGrid strokeDasharray="3 3" />
                        <XAxis dataKey="date" tickFormatter={(v: string) => new Date(v).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} fontSize={11} />
                        <YAxis domain={[0, 6]} />
                        <Tooltip labelFormatter={(v: string) => new Date(v).toLocaleDateString()} />
                        <Line type="monotone" dataKey="averageScore" stroke="#6366f1" strokeWidth={2} dot={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  </CardContent>
                </Card>
              </>
            )}

            {/* AI-Human Agreement Detail */}
            <ExpertRouteSectionHeader title="AI vs. Human Agreement" />
            <Card>
              <CardContent className="pt-4">
                <div className="grid grid-cols-3 gap-4 text-center">
                  <div>
                    <p className="text-xs text-gray-500">Comparisons</p>
                    <p className="text-xl font-bold text-indigo-600 dark:text-indigo-400">{data.aiHumanAgreement.comparisons}</p>
                  </div>
                  <div>
                    <p className="text-xs text-gray-500">Avg Difference</p>
                    <p className="text-xl font-bold text-indigo-600 dark:text-indigo-400">{data.aiHumanAgreement.averageDifference.toFixed(2)}</p>
                  </div>
                  <div>
                    <p className="text-xs text-gray-500">Max Difference</p>
                    <p className="text-xl font-bold text-indigo-600 dark:text-indigo-400">{data.aiHumanAgreement.maxDifference.toFixed(2)}</p>
                  </div>
                </div>
                <p className="text-xs text-gray-500 mt-3 text-center">
                  Agreement is measured as the percentage of reviews where the expert-AI score difference ≤ 1.0 point.
                </p>
              </CardContent>
            </Card>
          </>
        )}
      </AsyncStateWrapper>
    </ExpertRouteWorkspace>
  );
}
