'use client';

import { useEffect, useState } from 'react';
import { Bot, CheckCircle2, Edit3, XCircle } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface AiCriterion {
  criterionCode: string; aiScore: number; aiConfidence: string; note: string;
}

interface AiComment {
  criterion: string | null; text: string | null; type: string;
}

interface PreFillData {
  reviewRequestId: string; hasAiPreFill: boolean; message?: string;
  aiEvaluationId?: string; aiScoreRange?: string; aiConfidence?: string;
  aiGeneratedAt?: string; subtestCode?: string;
  suggestedScores?: AiCriterion[]; suggestedComments?: AiComment[];
  instructions?: { guidance: string; actions: string[]; note: string };
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const CONFIDENCE_COLOR: Record<string, string> = { high: 'bg-emerald-100 text-emerald-700', medium: 'bg-amber-100 text-amber-700', low: 'bg-red-100 text-red-700' };

export default function AiPreFillPage() {
  const [data, setData] = useState<PreFillData | null>(null);
  const [loading, setLoading] = useState(false);
  const [reviewId, setReviewId] = useState('');

  useEffect(() => { analytics.track('expert_ai_prefill_viewed'); }, []);

  const load = async () => {
    if (!reviewId) return;
    setLoading(true);
    try { setData(await apiRequest<PreFillData>(`/v1/expert/reviews/${reviewId}/ai-prefill`)); }
    catch { setData(null); }
    finally { setLoading(false); }
  };

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-4xl mx-auto px-4 py-8">
        <div className="mb-8">
          <h1 className="text-2xl font-bold">AI Pre-Fill Assistant</h1>
          <p className="text-muted-foreground mt-1">Use AI-suggested scores as a starting point for your expert review.</p>
        </div>

        <MotionSection className="space-y-6">
          <div className="flex gap-3 items-end">
            <div className="flex-1">
              <label className="text-sm font-medium text-muted-foreground mb-1 block">Review Request ID</label>
              <input className="w-full border rounded-lg px-3 py-2 text-sm" placeholder="Enter review request ID..." value={reviewId} onChange={e => setReviewId(e.target.value)} />
            </div>
            <button onClick={load} disabled={loading} className="px-4 py-2 bg-primary text-primary-foreground rounded-lg text-sm font-medium hover:bg-primary/90 disabled:opacity-50">{loading ? 'Loading...' : 'Load AI Suggestions'}</button>
          </div>

          {loading && <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>}

          {data && !data.hasAiPreFill && (
            <Card className="p-6 text-center text-muted-foreground"><Bot className="w-8 h-8 mx-auto mb-3 opacity-50" /><p>{data.message}</p></Card>
          )}

          {data?.hasAiPreFill && (
            <>
              <MotionItem>
                <Card className="p-5 bg-blue-50/50 dark:bg-blue-950/20">
                  <div className="flex items-start gap-3">
                    <Bot className="w-6 h-6 text-blue-600 flex-shrink-0" />
                    <div>
                      <h3 className="font-semibold">AI Evaluation Available</h3>
                      <div className="flex gap-3 mt-1 text-sm text-muted-foreground flex-wrap">
                        <span>Score range: <strong>{data.aiScoreRange}</strong></span>
                        <Badge className={CONFIDENCE_COLOR[data.aiConfidence ?? 'medium']}>{data.aiConfidence} confidence</Badge>
                        <span className="capitalize">{data.subtestCode}</span>
                      </div>
                      {data.instructions && <p className="text-sm mt-2 text-muted-foreground">{data.instructions.guidance}</p>}
                    </div>
                  </div>
                </Card>
              </MotionItem>

              {data.suggestedScores && data.suggestedScores.length > 0 && (
                <>
                  <h3 className="text-lg font-semibold">Suggested Criterion Scores</h3>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {data.suggestedScores.map(s => (
                      <MotionItem key={s.criterionCode}>
                        <Card className="p-4">
                          <div className="flex items-center justify-between">
                            <h4 className="font-medium capitalize">{s.criterionCode.replace(/_/g, ' ')}</h4>
                            <div className="text-2xl font-bold text-primary">{s.aiScore}</div>
                          </div>
                          <Badge className={`mt-1 ${CONFIDENCE_COLOR[s.aiConfidence] ?? ''}`}>{s.aiConfidence}</Badge>
                          <div className="flex flex-wrap gap-2 mt-3">
                            <button className="flex items-center gap-1 px-3 py-2 rounded-md bg-emerald-100 text-emerald-700 text-xs font-medium hover:bg-emerald-200"><CheckCircle2 className="w-3 h-3" /> Accept</button>
                            <button className="flex items-center gap-1 px-3 py-2 rounded-md bg-amber-100 text-amber-700 text-xs font-medium hover:bg-amber-200"><Edit3 className="w-3 h-3" /> Adjust</button>
                            <button className="flex items-center gap-1 px-3 py-2 rounded-md bg-red-100 text-red-700 text-xs font-medium hover:bg-red-200"><XCircle className="w-3 h-3" /> Override</button>
                          </div>
                        </Card>
                      </MotionItem>
                    ))}
                  </div>
                </>
              )}

              {data.suggestedComments && data.suggestedComments.length > 0 && (
                <>
                  <h3 className="text-lg font-semibold">AI Feedback Suggestions</h3>
                  <div className="space-y-2">
                    {data.suggestedComments.map((c, i) => (
                      <MotionItem key={i}>
                        <Card className="p-3">
                          {c.criterion && <Badge variant="outline" className="mb-1 capitalize text-[10px]">{c.criterion}</Badge>}
                          <p className="text-sm">{c.text}</p>
                        </Card>
                      </MotionItem>
                    ))}
                  </div>
                </>
              )}

              {data.instructions && (
                <Card className="p-4 bg-muted/50">
                  <p className="text-sm font-medium mb-1">{data.instructions.note}</p>
                  <div className="flex gap-2 mt-2">
                    {data.instructions.actions.map(a => <Badge key={a} variant="outline" className="text-xs">{a}</Badge>)}
                  </div>
                </Card>
              )}
            </>
          )}
        </MotionSection>
      </div>
    </div>
  );
}
