'use client';

import { useEffect, useState, useCallback } from 'react';
import { ChevronLeft, ChevronRight, Star, Send, Save, FileText, Mic, AlertCircle, CheckCircle2, Clock } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

/* ── types ─────────────────────────────────────── */
interface QueueItem {
  assignmentId: string; reviewRequestId: string; attemptId: string; subtestCode: string;
  priority: string; reasons: string[]; daysToExam: number | null; slaRemainingHours: number;
  isResubmission: boolean; turnaround: string; hoursWaiting: number; createdAt: string;
}

interface QueueData {
  items: QueueItem[];
  summary: { total: number; critical: number; high: number; normal: number };
}

interface CriterionScore { code: string; label: string; score: number | null; maxScore: number }

/* ── api helper ───────────────────────────────── */
async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

/* ── writing criteria ────────────────────────── */
const WRITING_CRITERIA: Omit<CriterionScore, 'score'>[] = [
  { code: 'purpose', label: 'Overall Task Fulfilment', maxScore: 7 },
  { code: 'content', label: 'Appropriateness of Content', maxScore: 7 },
  { code: 'conciseness', label: 'Conciseness & Clarity', maxScore: 7 },
  { code: 'genre', label: 'Genre & Style', maxScore: 7 },
  { code: 'organization', label: 'Organisation & Layout', maxScore: 7 },
  { code: 'language', label: 'Language', maxScore: 7 },
];

const SPEAKING_CRITERIA: Omit<CriterionScore, 'score'>[] = [
  { code: 'intelligibility', label: 'Intelligibility', maxScore: 6 },
  { code: 'fluency', label: 'Fluency', maxScore: 6 },
  { code: 'appropriateness', label: 'Appropriateness', maxScore: 6 },
  { code: 'grammar', label: 'Grammar & Expression', maxScore: 6 },
  { code: 'clinical', label: 'Clinical Communication', maxScore: 6 },
];

/* ── step enum ───────────────────────────────── */
type ReviewStep = 'queue' | 'read' | 'score' | 'comment' | 'confirm';

export default function ExpertMobileReviewPage() {
  /* state */
  const [queue, setQueue] = useState<QueueData | null>(null);
  const [loading, setLoading] = useState(true);
  const [step, setStep] = useState<ReviewStep>('queue');
  const [activeItem, setActiveItem] = useState<QueueItem | null>(null);
  const [scores, setScores] = useState<Record<string, number | null>>({});
  const [comment, setComment] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  /* load queue */
  useEffect(() => {
    analytics.track('expert_mobile_review_opened');
    apiRequest<QueueData>('/v1/expert/queue-priority').then(setQueue).catch(() => {}).finally(() => setLoading(false));
  }, []);

  /* pick an item to review */
  const startReview = useCallback((item: QueueItem) => {
    setActiveItem(item);
    const criteria = item.subtestCode.startsWith('S') ? SPEAKING_CRITERIA : WRITING_CRITERIA;
    const initial: Record<string, number | null> = {};
    criteria.forEach(c => { initial[c.code] = null; });
    setScores(initial);
    setComment('');
    setSubmitted(false);
    setStep('read');
  }, []);

  /* score helpers */
  const criteria = activeItem?.subtestCode.startsWith('S') ? SPEAKING_CRITERIA : WRITING_CRITERIA;
  const allScored = Object.values(scores).every(v => v !== null);

  /* submit */
  const handleSubmit = async () => {
    if (!activeItem || !allScored) return;
    setSubmitting(true);
    try {
      const subtestType = activeItem.subtestCode.startsWith('S') ? 'speaking' : 'writing';
      const scorePayload = criteria.map(c => ({ criterionCode: c.code, band: scores[c.code] }));
      await apiRequest(`/v1/expert/reviews/${activeItem.reviewRequestId}/${subtestType}/submit`, {
        method: 'POST',
        body: JSON.stringify({ scores: scorePayload, overallComment: comment }),
      });
      setSubmitted(true);
      setStep('confirm');
      analytics.track('expert_mobile_review_submitted', { subtestCode: activeItem.subtestCode });
    } catch { /* handled by UI state */ }
    setSubmitting(false);
  };

  /* save draft */
  const handleSaveDraft = async () => {
    if (!activeItem) return;
    const subtestType = activeItem.subtestCode.startsWith('S') ? 'speaking' : 'writing';
    const scorePayload = criteria.map(c => ({ criterionCode: c.code, band: scores[c.code] }));
    try {
      await apiRequest(`/v1/expert/reviews/${activeItem.reviewRequestId}/${subtestType}/draft`, {
        method: 'PUT',
        body: JSON.stringify({ scores: scorePayload, overallComment: comment }),
      });
    } catch { /* silent */ }
  };

  /* back to queue */
  const backToQueue = () => { setStep('queue'); setActiveItem(null); };

  /* ── render ────────────────────────────────── */
  if (loading) {
    return (
      <div className="min-h-screen bg-background p-4 space-y-4">
        <Skeleton className="h-8 w-48" /><Skeleton className="h-24 w-full" /><Skeleton className="h-24 w-full" /><Skeleton className="h-24 w-full" />
      </div>
    );
  }

  /* ── STEP: Queue ──────────────────────────── */
  if (step === 'queue') {
    return (
      <div className="min-h-screen bg-background safe-area-inset">
        <div className="px-4 py-6 max-w-lg mx-auto">
          <h1 className="text-xl font-bold mb-1">Mobile Review</h1>
          <p className="text-sm text-muted-foreground mb-6">Tap a submission to start reviewing</p>

          {/* summary strip */}
          {queue && (
            <div className="flex gap-2 mb-6 overflow-x-auto pb-1">
              {[{ label: 'All', count: queue.summary.total, cls: 'bg-muted' },
                { label: 'Critical', count: queue.summary.critical, cls: 'bg-red-100 dark:bg-red-950 text-red-700' },
                { label: 'High', count: queue.summary.high, cls: 'bg-amber-100 dark:bg-amber-950 text-amber-700' },
                { label: 'Normal', count: queue.summary.normal, cls: 'bg-muted' },
              ].map(s => (
                <Badge key={s.label} variant="outline" className={`${s.cls} whitespace-nowrap px-3 py-1.5 text-xs`}>
                  {s.label}: {s.count}
                </Badge>
              ))}
            </div>
          )}

          <MotionSection className="space-y-3">
            {queue?.items.map(item => {
              const isWriting = !item.subtestCode.startsWith('S');
              return (
                <MotionItem key={item.assignmentId}>
                  <Card
                    className="p-4 active:scale-[0.98] transition-transform cursor-pointer"
                    onClick={() => startReview(item)}
                  >
                    <div className="flex items-center justify-between mb-2">
                      <div className="flex items-center gap-2">
                        {isWriting ? <FileText className="h-4 w-4 text-blue-500" /> : <Mic className="h-4 w-4 text-purple-500" />}
                        <span className="font-medium text-sm">{item.subtestCode}</span>
                        {item.isResubmission && <Badge variant="secondary" className="text-[10px] px-1.5">Re-sub</Badge>}
                      </div>
                      <Badge variant={item.priority === 'critical' ? 'destructive' : item.priority === 'high' ? 'default' : 'outline'} className="text-[10px]">
                        {item.priority}
                      </Badge>
                    </div>
                    <div className="flex items-center gap-3 text-xs text-muted-foreground">
                      <span className="flex items-center gap-1"><Clock className="h-3 w-3" />{Math.round(item.slaRemainingHours)}h left</span>
                      {item.turnaround === 'express' && <Badge variant="outline" className="text-[10px] border-amber-300 text-amber-600">Express</Badge>}
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
            {(!queue || queue.items.length === 0) && (
              <div className="text-center py-12 text-muted-foreground">
                <CheckCircle2 className="h-10 w-10 mx-auto mb-3 opacity-40" />
                <p className="text-sm">No reviews in queue</p>
              </div>
            )}
          </MotionSection>
        </div>
      </div>
    );
  }

  /* ── STEP: Read (learner response) ────────── */
  if (step === 'read' && activeItem) {
    const isWriting = !activeItem.subtestCode.startsWith('S');
    return (
      <div className="min-h-screen bg-background safe-area-inset">
        <div className="sticky top-0 z-10 bg-background/95 backdrop-blur border-b px-4 py-3 flex items-center justify-between">
          <button onClick={backToQueue} className="flex items-center gap-1 text-sm text-muted-foreground"><ChevronLeft className="h-4 w-4" />Queue</button>
          <span className="text-sm font-medium">{activeItem.subtestCode}</span>
          <Badge variant={activeItem.priority === 'critical' ? 'destructive' : 'outline'} className="text-[10px]">{activeItem.priority}</Badge>
        </div>
        <div className="px-4 py-6 max-w-lg mx-auto space-y-4">
          <Card className="p-4">
            <h2 className="font-semibold text-sm mb-2">{isWriting ? 'Learner Response' : 'Audio & Transcript'}</h2>
            <div className="bg-muted/50 rounded-lg p-4">
              {isWriting ? (
                <p className="text-sm leading-relaxed text-muted-foreground">
                  The learner&apos;s written response will load here from the review request data.
                  Scroll through the full response before proceeding to score.
                </p>
              ) : (
                <div className="space-y-3">
                  <div className="bg-background rounded-lg p-3 flex items-center gap-3">
                    <Mic className="h-5 w-5 text-purple-500" />
                    <div className="flex-1">
                      <div className="h-2 bg-purple-200 dark:bg-purple-800 rounded-full" />
                    </div>
                    <span className="text-xs text-muted-foreground">0:00</span>
                  </div>
                  <p className="text-sm leading-relaxed text-muted-foreground">
                    Transcript will appear here with timestamps for inline commenting.
                  </p>
                </div>
              )}
            </div>
          </Card>
          <Button className="w-full" size="lg" onClick={() => setStep('score')}>
            Proceed to Scoring <ChevronRight className="h-4 w-4 ml-1" />
          </Button>
        </div>
      </div>
    );
  }

  /* ── STEP: Score ──────────────────────────── */
  if (step === 'score' && activeItem) {
    return (
      <div className="min-h-screen bg-background safe-area-inset">
        <div className="sticky top-0 z-10 bg-background/95 backdrop-blur border-b px-4 py-3 flex items-center justify-between">
          <button onClick={() => setStep('read')} className="flex items-center gap-1 text-sm text-muted-foreground"><ChevronLeft className="h-4 w-4" />Response</button>
          <span className="text-sm font-medium">Score</span>
          <button onClick={handleSaveDraft} className="text-sm text-primary flex items-center gap-1"><Save className="h-3.5 w-3.5" />Save</button>
        </div>
        <div className="px-4 py-6 max-w-lg mx-auto space-y-4">
          <MotionSection className="space-y-3">
            {criteria.map(c => (
              <MotionItem key={c.code}>
                <Card className="p-4">
                  <p className="text-sm font-medium mb-3">{c.label}</p>
                  <div className="flex flex-wrap gap-2">
                    {Array.from({ length: c.maxScore + 1 }, (_, i) => (
                      <button
                        key={i}
                        onClick={() => setScores(prev => ({ ...prev, [c.code]: i }))}
                        className={`w-10 h-10 rounded-lg text-sm font-medium border transition-all
                          ${scores[c.code] === i
                            ? 'bg-primary text-primary-foreground border-primary shadow-sm'
                            : 'bg-muted/30 border-border hover:border-primary/50'
                          }`}
                      >
                        {i}
                      </button>
                    ))}
                  </div>
                </Card>
              </MotionItem>
            ))}
          </MotionSection>

          <Button className="w-full" size="lg" disabled={!allScored} onClick={() => setStep('comment')}>
            Add Comment <ChevronRight className="h-4 w-4 ml-1" />
          </Button>
        </div>
      </div>
    );
  }

  /* ── STEP: Comment ────────────────────────── */
  if (step === 'comment' && activeItem) {
    return (
      <div className="min-h-screen bg-background safe-area-inset">
        <div className="sticky top-0 z-10 bg-background/95 backdrop-blur border-b px-4 py-3 flex items-center justify-between">
          <button onClick={() => setStep('score')} className="flex items-center gap-1 text-sm text-muted-foreground"><ChevronLeft className="h-4 w-4" />Scores</button>
          <span className="text-sm font-medium">Comment</span>
          <button onClick={handleSaveDraft} className="text-sm text-primary flex items-center gap-1"><Save className="h-3.5 w-3.5" />Save</button>
        </div>
        <div className="px-4 py-6 max-w-lg mx-auto space-y-4">
          {/* score summary */}
          <Card className="p-4">
            <p className="text-xs text-muted-foreground mb-2">Score Summary</p>
            <div className="flex flex-wrap gap-2">
              {criteria.map(c => (
                <Badge key={c.code} variant="outline" className="text-xs">
                  {c.label.split(' ')[0]}: <span className="font-bold ml-1">{scores[c.code] ?? '—'}</span>
                </Badge>
              ))}
            </div>
          </Card>

          <Card className="p-4">
            <label htmlFor="overall-comment" className="text-sm font-medium block mb-2">Overall Comment</label>
            <textarea
              id="overall-comment"
              value={comment}
              onChange={e => setComment(e.target.value)}
              placeholder="Provide feedback for the learner…"
              rows={6}
              className="w-full rounded-lg border bg-muted/30 p-3 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-primary/50"
            />
            <p className="text-xs text-muted-foreground mt-2">{comment.length} characters</p>
          </Card>

          <Button className="w-full" size="lg" disabled={submitting} onClick={handleSubmit}>
            {submitting ? 'Submitting…' : <><Send className="h-4 w-4 mr-2" />Submit Review</>}
          </Button>
        </div>
      </div>
    );
  }

  /* ── STEP: Confirm ────────────────────────── */
  if (step === 'confirm') {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center safe-area-inset">
        <div className="text-center px-6 max-w-sm">
          <div className="inline-flex items-center justify-center h-16 w-16 rounded-full bg-green-100 dark:bg-green-950 mb-4">
            <CheckCircle2 className="h-8 w-8 text-green-600" />
          </div>
          <h2 className="text-lg font-bold mb-1">Review Submitted</h2>
          <p className="text-sm text-muted-foreground mb-6">Your scores and feedback have been saved.</p>
          <Button className="w-full" size="lg" onClick={backToQueue}>Review Next</Button>
        </div>
      </div>
    );
  }

  /* fallback */
  return null;
}
