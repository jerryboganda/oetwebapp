'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { ArrowRight, Bot, CheckCircle2, Edit3, XCircle } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { apiClient, fetchSpeakingReviewDetail, fetchWritingReviewDetail, isApiError, saveDraftReview } from '@/lib/api';
import type { ReviewDraft } from '@/lib/types/expert';

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

type CriterionMode = 'pending' | 'accepted' | 'adjusted' | 'overridden';

interface CriterionDecision {
  mode: CriterionMode;
  chosenScore: number;
}

const apiRequest = apiClient.request;

const CONFIDENCE_COLOR: Record<string, string> = { high: 'bg-emerald-100 text-emerald-700', medium: 'bg-amber-100 text-amber-700', low: 'bg-red-100 text-red-700' };

// Mirrors backend ExpertService.MaxScoreForCriterion — writing: purpose=3,
// others=7; speaking: clinical-communication cluster=3, linguistic=6.
const SPEAKING_CLINICAL_CRITERIA = ['relationshipbuilding', 'patientperspective', 'providingstructure', 'informationgathering', 'informationgiving'];

function criterionMax(subtestCode: string | undefined, criterionCode: string): number {
  const code = criterionCode.toLowerCase();
  if ((subtestCode ?? '').toLowerCase() === 'writing') {
    return code === 'purpose' ? 3 : 7;
  }
  return SPEAKING_CLINICAL_CRITERIA.includes(code) ? 3 : 6;
}

function clampScore(value: number, max: number): number {
  if (Number.isNaN(value)) return 0;
  return Math.max(0, Math.min(max, Math.round(value)));
}

export default function AiPreFillPage() {
  const searchParams = useSearchParams();
  const [reviewId, setReviewId] = useState('');
  const [loadedReviewId, setLoadedReviewId] = useState<string | null>(null);
  const [data, setData] = useState<PreFillData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [decisions, setDecisions] = useState<Record<string, CriterionDecision>>({});
  const [applying, setApplying] = useState(false);
  const [applied, setApplied] = useState(false);
  const [applyError, setApplyError] = useState<string | null>(null);
  const autoLoadedRef = useRef(false);

  useEffect(() => { analytics.track('expert_ai_prefill_viewed'); }, []);

  const load = useCallback(async (id: string) => {
    const trimmed = id.trim();
    if (!trimmed) return;
    setLoading(true);
    setError(null);
    setApplied(false);
    setApplyError(null);
    try {
      const result = await apiRequest<PreFillData>(`/v1/expert/reviews/${encodeURIComponent(trimmed)}/ai-prefill`);
      setData(result);
      setLoadedReviewId(trimmed);
      const next: Record<string, CriterionDecision> = {};
      for (const suggestion of result.suggestedScores ?? []) {
        next[suggestion.criterionCode] = {
          mode: 'pending',
          chosenScore: clampScore(suggestion.aiScore, criterionMax(result.subtestCode, suggestion.criterionCode)),
        };
      }
      setDecisions(next);
    } catch (err) {
      setData(null);
      setLoadedReviewId(null);
      setDecisions({});
      setError(isApiError(err) ? err.userMessage : 'Failed to load AI suggestions for this review.');
    } finally {
      setLoading(false);
    }
  }, []);

  // Deep link support: /expert/ai-prefill?reviewId=... prefills and auto-loads.
  useEffect(() => {
    if (autoLoadedRef.current) return;
    const fromQuery = searchParams?.get('reviewId')?.trim();
    if (!fromQuery) return;
    autoLoadedRef.current = true;
    setReviewId(fromQuery);
    void load(fromQuery);
  }, [load, searchParams]);

  const setDecision = useCallback((criterionCode: string, decision: CriterionDecision) => {
    setApplied(false);
    setDecisions((current) => ({ ...current, [criterionCode]: decision }));
  }, []);

  const decidedScores = useMemo(
    () => Object.fromEntries(
      Object.entries(decisions)
        .filter(([, decision]) => decision.mode !== 'pending')
        .map(([code, decision]) => [code, decision.chosenScore]),
    ),
    [decisions],
  );
  const decidedCount = Object.keys(decidedScores).length;

  const applyToDraft = useCallback(async () => {
    if (!loadedReviewId || decidedCount === 0) return;
    setApplying(true);
    setApplyError(null);
    try {
      // Merge over the existing server draft so applying scores never wipes
      // comments or the final feedback the expert already saved.
      const detail = (data?.subtestCode ?? '').toLowerCase() === 'writing'
        ? await fetchWritingReviewDetail(loadedReviewId)
        : await fetchSpeakingReviewDetail(loadedReviewId);
      const existing = detail.existingDraft ?? null;
      const timestampComments = existing?.timestampComments ?? [];
      const anchoredComments = existing?.anchoredComments ?? [];
      const comments: ReviewDraft['comments'] = timestampComments.length > 0 ? timestampComments : anchoredComments;
      await saveDraftReview({
        reviewRequestId: loadedReviewId,
        scores: { ...(existing?.scores ?? {}), ...decidedScores },
        criterionComments: existing?.criterionComments ?? {},
        finalComment: existing?.finalComment ?? '',
        comments,
        scratchpad: existing?.scratchpad ?? '',
        checklistItems: existing?.checklistItems ?? [],
        savedAt: new Date().toISOString(),
        version: existing?.version,
      });
      setApplied(true);
      analytics.track('expert_ai_prefill_applied', { reviewRequestId: loadedReviewId, criteria: decidedCount });
    } catch (err) {
      setApplyError(isApiError(err) ? err.userMessage : 'Failed to apply the scores to your review draft.');
    } finally {
      setApplying(false);
    }
  }, [data?.subtestCode, decidedCount, decidedScores, loadedReviewId]);

  return (
    <div className="min-h-screen bg-surface">
      <div className="max-w-4xl mx-auto px-4 py-8">
        <div className="mb-8">
          <h1 className="text-2xl font-bold">AI Pre-Fill Assistant</h1>
          <p className="text-muted mt-1">Use AI-suggested scores as a starting point for your tutor review.</p>
        </div>

        <MotionSection className="space-y-6">
          <div className="flex gap-3 items-end">
            <div className="flex-1">
              <label className="text-sm font-medium text-muted mb-1 block" htmlFor="ai-prefill-review-id">Review Request ID</label>
              <input id="ai-prefill-review-id" className="w-full border rounded-lg px-3 py-2 text-sm" placeholder="Enter review request ID..." value={reviewId} onChange={e => setReviewId(e.target.value)} />
            </div>
            <button onClick={() => void load(reviewId)} disabled={loading || !reviewId.trim()} className="px-4 py-2 bg-primary text-primary-foreground rounded-lg text-sm font-medium hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 disabled:opacity-50">{loading ? 'Loading...' : 'Load AI Suggestions'}</button>
          </div>

          {error && <InlineAlert variant="error">{error}</InlineAlert>}

          {loading && <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>}

          {data && !data.hasAiPreFill && (
            <Card className="p-6 text-center text-muted"><Bot className="w-8 h-8 mx-auto mb-3 opacity-50" /><p>{data.message}</p></Card>
          )}

          {data?.hasAiPreFill && (
            <>
              <MotionItem>
                <Card className="p-5 bg-blue-50/50 dark:bg-blue-950/20">
                  <div className="flex items-start gap-3">
                    <Bot className="w-6 h-6 text-blue-600 flex-shrink-0" />
                    <div>
                      <h3 className="font-semibold">AI Evaluation Available</h3>
                      <div className="flex gap-3 mt-1 text-sm text-muted flex-wrap">
                        <span>Score range: <strong>{data.aiScoreRange}</strong></span>
                        <Badge className={CONFIDENCE_COLOR[data.aiConfidence ?? 'medium']}>{data.aiConfidence} confidence</Badge>
                        <span className="capitalize">{data.subtestCode}</span>
                      </div>
                      {data.instructions && <p className="text-sm mt-2 text-muted">{data.instructions.guidance}</p>}
                    </div>
                  </div>
                </Card>
              </MotionItem>

              {data.suggestedScores && data.suggestedScores.length > 0 && (
                <>
                  <h3 className="text-lg font-semibold">Suggested Criterion Scores</h3>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {data.suggestedScores.map(s => {
                      const max = criterionMax(data.subtestCode, s.criterionCode);
                      const decision = decisions[s.criterionCode] ?? { mode: 'pending' as CriterionMode, chosenScore: clampScore(s.aiScore, max) };
                      return (
                        <MotionItem key={s.criterionCode}>
                          <Card className="p-4">
                            <div className="flex items-center justify-between">
                              <h4 className="font-medium capitalize">{s.criterionCode.replace(/_/g, ' ')}</h4>
                              <div className="text-2xl font-bold text-primary">{s.aiScore}<span className="text-sm font-normal text-muted">/{max}</span></div>
                            </div>
                            <Badge className={`mt-1 ${CONFIDENCE_COLOR[s.aiConfidence] ?? ''}`}>{s.aiConfidence}</Badge>
                            <div className="flex flex-wrap gap-2 mt-3">
                              <button
                                type="button"
                                onClick={() => setDecision(s.criterionCode, { mode: 'accepted', chosenScore: clampScore(s.aiScore, max) })}
                                aria-pressed={decision.mode === 'accepted'}
                                className={`flex items-center gap-1 px-3 py-2 rounded-md text-xs font-medium ${decision.mode === 'accepted' ? 'bg-emerald-600 text-white' : 'bg-emerald-100 text-emerald-700 hover:bg-emerald-200'}`}
                              >
                                <CheckCircle2 className="w-3 h-3" /> Accept
                              </button>
                              <button
                                type="button"
                                onClick={() => setDecision(s.criterionCode, { mode: 'adjusted', chosenScore: clampScore(s.aiScore, max) })}
                                aria-pressed={decision.mode === 'adjusted'}
                                className={`flex items-center gap-1 px-3 py-2 rounded-md text-xs font-medium ${decision.mode === 'adjusted' ? 'bg-amber-600 text-white' : 'bg-amber-100 text-amber-700 hover:bg-amber-200'}`}
                              >
                                <Edit3 className="w-3 h-3" /> Adjust
                              </button>
                              <button
                                type="button"
                                onClick={() => setDecision(s.criterionCode, { mode: 'overridden', chosenScore: 0 })}
                                aria-pressed={decision.mode === 'overridden'}
                                className={`flex items-center gap-1 px-3 py-2 rounded-md text-xs font-medium ${decision.mode === 'overridden' ? 'bg-red-600 text-white' : 'bg-red-100 text-red-700 hover:bg-red-200'}`}
                              >
                                <XCircle className="w-3 h-3" /> Override
                              </button>
                            </div>
                            {(decision.mode === 'adjusted' || decision.mode === 'overridden') && (
                              <div className="mt-3 flex items-center gap-2">
                                <label className="text-xs text-muted" htmlFor={`score-${s.criterionCode}`}>Your score (0–{max})</label>
                                <input
                                  id={`score-${s.criterionCode}`}
                                  type="number"
                                  min={0}
                                  max={max}
                                  step={1}
                                  value={decision.chosenScore}
                                  onChange={(e) => setDecision(s.criterionCode, { mode: decision.mode, chosenScore: clampScore(Number(e.target.value), max) })}
                                  className="w-20 border rounded-lg px-2 py-1 text-sm"
                                />
                              </div>
                            )}
                            {decision.mode !== 'pending' && (
                              <p className="mt-2 text-xs text-muted">Will apply: <strong>{decision.chosenScore}</strong>{decision.mode === 'accepted' ? ' (AI score)' : ''}</p>
                            )}
                          </Card>
                        </MotionItem>
                      );
                    })}
                  </div>

                  <Card className="p-4 flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-sm font-medium">{decidedCount} of {data.suggestedScores.length} criteria decided</p>
                      <p className="text-xs text-muted">Applying merges the chosen scores into your saved review draft — comments already in the draft are kept.</p>
                    </div>
                    <button
                      type="button"
                      onClick={() => void applyToDraft()}
                      disabled={applying || decidedCount === 0}
                      className="px-4 py-2 bg-primary text-primary-foreground rounded-lg text-sm font-medium hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 disabled:opacity-50"
                    >
                      {applying ? 'Applying...' : 'Apply to review draft'}
                    </button>
                  </Card>

                  {applyError && <InlineAlert variant="error">{applyError}</InlineAlert>}

                  {applied && loadedReviewId && (
                    <InlineAlert variant="success" title="Draft updated">
                      <span className="mr-2">The chosen scores were saved to your review draft.</span>
                      <Link href={`/expert/review/${encodeURIComponent(loadedReviewId)}`} className="inline-flex items-center gap-1 font-medium underline">
                        Open review workspace <ArrowRight className="w-3.5 h-3.5" />
                      </Link>
                    </InlineAlert>
                  )}
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
