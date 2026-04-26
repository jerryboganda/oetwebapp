'use client';

import { useEffect, useState } from 'react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import {
  MessageSquare, BarChart3, Target, ChevronRight, RotateCcw, ArrowLeft,
  Star, AlertTriangle, CheckCircle2, Volume2, Download,
} from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { analytics } from '@/lib/analytics';
import { downloadConversationTranscript, getConversationEvaluation } from '@/lib/api';
import { resolveApiMediaUrl } from '@/lib/media-url';
import type { ConversationEvaluationResponse } from '@/lib/types/conversation';
import { formatScaledScore, oetGradeLabel, type OetGrade } from '@/lib/scoring';

const GRADE_COLORS: Record<string, string> = {
  A: 'from-emerald-400 to-green-500',
  B: 'from-violet-400 to-purple-500',
  'C+': 'from-yellow-400 to-amber-500',
  C: 'from-orange-400 to-amber-500',
  D: 'from-red-400 to-rose-500',
  E: 'from-red-500 to-rose-600',
};

export default function ConversationResultsPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId as string;

  const [evaluation, setEvaluation] = useState<ConversationEvaluationResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState<'txt' | 'pdf' | null>(null);

  useEffect(() => {
    if (!sessionId) return;
    analytics.track('conversation_results_viewed', { sessionId });
    let cancelled = false;
    const poll = async () => {
      try {
        const data = (await getConversationEvaluation(sessionId)) as ConversationEvaluationResponse;
        if (cancelled) return;
        setEvaluation(data);
        if (!data.ready && (data.state === 'evaluating' || data.state === 'completed')) {
          setTimeout(poll, 3000);
        }
      } catch {
        if (!cancelled) setError('Failed to load evaluation results.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    poll();
    return () => { cancelled = true; };
  }, [sessionId]);

  const formatDuration = (s: number) => (s >= 60 ? `${Math.floor(s / 60)}m ${s % 60}s` : `${s}s`);

  const handleExport = async (format: 'txt' | 'pdf') => {
    setExporting(format);
    try {
      const blob = await downloadConversationTranscript(sessionId, format);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `conversation-${sessionId}-transcript.${format}`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      analytics.track('conversation_transcript_exported', { sessionId, format });
    } catch {
      setError('Failed to export transcript.');
    } finally {
      setExporting(null);
    }
  };

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="max-w-3xl mx-auto space-y-4">
          <Skeleton className="h-48 rounded-2xl" />
          <Skeleton className="h-64 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="error">{error}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  if (evaluation && !evaluation.ready) {
    return (
      <LearnerDashboardShell>
        <MotionSection className="max-w-md mx-auto text-center py-16">
          <div className="w-16 h-16 rounded-full bg-primary/10 flex items-center justify-center mx-auto mb-4">
            <BarChart3 className="w-8 h-8 text-primary animate-pulse" />
          </div>
          <h2 className="text-xl font-bold text-navy mb-2">Evaluating conversation…</h2>
          <p className="text-muted text-sm">This usually takes a few seconds. The page will update automatically.</p>
        </MotionSection>
      </LearnerDashboardShell>
    );
  }

  if (!evaluation) return null;

  const grade: OetGrade = ((evaluation.overallGrade as OetGrade) ?? 'E');
  const gradeGradient = GRADE_COLORS[grade] ?? 'from-gray-400 to-gray-500';
  const scaled = evaluation.scaledScore ?? 0;
  const criteria = evaluation.criteria ?? [];
  const strengths = evaluation.strengths ?? [];
  const improvements = evaluation.improvements ?? [];
  const suggested = evaluation.suggestedPractice ?? [];
  const annotations = evaluation.turnAnnotations ?? [];
  const turns = evaluation.turns ?? [];
  const appliedRuleIds = evaluation.appliedRuleIds ?? [];

  return (
    <LearnerDashboardShell>
      <div className="max-w-3xl mx-auto">
        <Link href="/conversation" className="mb-4 inline-flex items-center gap-1 text-sm text-muted transition-colors hover:text-primary">
          <ArrowLeft className="h-4 w-4" /> Back to Conversations
        </Link>

        <MotionSection className="mb-6 rounded-2xl border border-border bg-surface p-6 text-center">
          <div className="mb-3 text-sm font-bold uppercase tracking-wider text-muted">
            OET Speaking practice · Scaled score
          </div>
          <div className={`mx-auto mb-3 inline-flex h-24 w-24 items-center justify-center rounded-full bg-gradient-to-br ${gradeGradient} text-3xl font-bold text-white shadow-lg`}>
            {grade}
          </div>
          <div className="mb-1 text-3xl font-bold text-navy">
            {formatScaledScore(scaled)} · {oetGradeLabel(grade)}
          </div>
          <div className="mb-3 flex items-center justify-center gap-2 text-sm">
            {evaluation.passed ? (
              <Badge variant="success">Above pass mark (350/500)</Badge>
            ) : (
              <Badge variant="warning">Below pass mark (350/500)</Badge>
            )}
          </div>
          <div className="flex items-center justify-center gap-4 text-sm text-muted">
            <span>{evaluation.turnCount ?? 0} turns</span>
            <span>•</span>
            <span>{formatDuration(evaluation.durationSeconds ?? 0)}</span>
            {evaluation.rulebookVersion && (<><span>•</span><span>Rulebook v{evaluation.rulebookVersion}</span></>)}
          </div>
          {evaluation.advisory && (<p className="mt-4 text-xs text-muted/60">{evaluation.advisory}</p>)}
        </MotionSection>

        {criteria.length > 0 && (
          <MotionSection delayIndex={1} className="mb-6 rounded-2xl border border-border bg-surface p-6">
            <h2 className="mb-4 flex items-center gap-2 text-lg font-bold text-navy">
              <Target className="h-5 w-5 text-primary" /> OET Speaking Rubric
            </h2>
            <div className="space-y-4">
              {criteria.map((criterion, i) => {
                const max = criterion.maxScore || 6;
                const pct = (criterion.score06 / max) * 100;
                return (
                  <MotionItem key={criterion.id} delayIndex={i}>
                    <div className="mb-1 flex items-center justify-between">
                      <span className="text-sm font-semibold text-navy">{humanName(criterion.id)}</span>
                      <span className="text-sm font-bold text-primary">
                        {criterion.score06.toFixed(1)} / {max.toFixed(0)}
                      </span>
                    </div>
                    <div className="mb-1.5 h-2.5 w-full rounded-full bg-background-light">
                      <div className={`h-2.5 rounded-full transition-all duration-700 ${
                        pct >= 70 ? 'bg-success' : pct >= 50 ? 'bg-warning' : 'bg-danger'}`}
                        style={{ width: `${pct}%` }} />
                    </div>
                    {criterion.evidence && (<p className="text-xs text-muted">{criterion.evidence}</p>)}
                    {criterion.quotes && criterion.quotes.length > 0 && (
                      <ul className="mt-1 space-y-0.5 pl-4 text-xs italic text-muted">
                        {criterion.quotes.slice(0, 3).map((q, qi) => (<li key={qi}>&ldquo;{q}&rdquo;</li>))}
                      </ul>
                    )}
                  </MotionItem>
                );
              })}
            </div>
          </MotionSection>
        )}

        <div className="mb-6 grid grid-cols-1 gap-4 md:grid-cols-2">
          {strengths.length > 0 && (
            <MotionSection delayIndex={2} className="rounded-2xl border border-border bg-surface p-5">
              <h3 className="mb-3 flex items-center gap-2 text-sm font-bold text-success">
                <CheckCircle2 className="h-4 w-4" /> Strengths
              </h3>
              <ul className="space-y-2">
                {strengths.map((s, i) => (
                  <li key={i} className="flex items-start gap-2 text-sm text-navy">
                    <Star className="mt-0.5 h-3.5 w-3.5 shrink-0 text-success" />{s}
                  </li>
                ))}
              </ul>
            </MotionSection>
          )}
          {improvements.length > 0 && (
            <MotionSection delayIndex={3} className="rounded-2xl border border-border bg-surface p-5">
              <h3 className="mb-3 flex items-center gap-2 text-sm font-bold text-warning">
                <AlertTriangle className="h-4 w-4" /> Areas to improve
              </h3>
              <ul className="space-y-2">
                {improvements.map((s, i) => (
                  <li key={i} className="flex items-start gap-2 text-sm text-navy">
                    <ChevronRight className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />{s}
                  </li>
                ))}
              </ul>
            </MotionSection>
          )}
        </div>

        {turns.length > 0 && (
          <MotionSection delayIndex={4} className="mb-6 rounded-2xl border border-border bg-surface p-6">
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
              <h2 className="flex items-center gap-2 text-lg font-bold text-navy">
                <MessageSquare className="h-5 w-5 text-primary" /> Transcript
              </h2>
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => void handleExport('txt')} disabled={exporting !== null}>
                  <Download className="mr-1.5 h-4 w-4" /> TXT
                </Button>
                <Button variant="outline" size="sm" onClick={() => void handleExport('pdf')} disabled={exporting !== null}>
                  <Download className="mr-1.5 h-4 w-4" /> PDF
                </Button>
              </div>
            </div>
            <div className="space-y-3">
              {turns.map((t) => {
                const turnAnnotations = annotations.filter((a) => a.turnNumber === t.turnNumber);
                const audioUrl = resolveApiMediaUrl(t.audioUrl);
                return (
                  <div key={t.turnNumber} className="rounded-xl border border-border p-3">
                    <div className="mb-2 flex items-center justify-between text-xs font-semibold text-muted/60">
                      <span>Turn {t.turnNumber} · {t.role === 'learner' ? 'You' : 'AI Partner'}</span>
                      {t.confidence != null && t.role === 'learner' && (
                        <span className="text-[10px]">ASR conf {(t.confidence * 100).toFixed(0)}%</span>
                      )}
                    </div>
                    <p className="text-sm text-navy">{t.content}</p>
                    {audioUrl && (
                      <audio controls preload="none" src={audioUrl} className="mt-2 w-full">
                        <Volume2 className="h-3 w-3" />
                      </audio>
                    )}
                    {turnAnnotations.length > 0 && (
                      <div className="mt-2 space-y-1.5">
                        {turnAnnotations.map((a) => (
                          <div key={a.id} className="flex items-start gap-2">
                            <span className={`rounded px-1.5 py-0.5 text-[10px] font-bold uppercase ${
                              a.type === 'strength'
                                ? 'bg-success/10 text-success'
                                : a.type === 'error'
                                ? 'bg-danger/10 text-danger'
                                : 'bg-warning/10 text-warning'
                            }`}>{a.type}</span>
                            <div className="flex-1 text-sm text-navy">
                              {a.evidence}
                              {a.ruleId && (<span className="ml-2 text-[10px] font-mono text-primary">{a.ruleId}</span>)}
                              {a.suggestion && (<div className="mt-0.5 text-xs text-primary">💡 {a.suggestion}</div>)}
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </MotionSection>
        )}

        {suggested.length > 0 && (
          <MotionSection delayIndex={5} className="mb-6 rounded-2xl border border-primary/30 bg-primary/5 p-5">
            <h3 className="mb-3 text-sm font-bold text-primary">Practice suggestions</h3>
            <ul className="space-y-2">
              {suggested.map((s, i) => (<li key={i} className="text-sm text-navy">• {s}</li>))}
            </ul>
          </MotionSection>
        )}

        {appliedRuleIds.length > 0 && (
          <p className="mb-6 text-center text-xs text-muted/60">Rules applied: {appliedRuleIds.join(', ')}</p>
        )}

        <div className="flex flex-wrap items-center justify-center gap-4 py-6">
          <Link href="/conversation"
            className="flex items-center gap-2 rounded-xl bg-primary px-6 py-2.5 font-semibold text-white transition-colors hover:bg-primary/90">
            <RotateCcw className="h-4 w-4" /> Practice again
          </Link>
          <Link href="/review"
            className="rounded-xl bg-background-light px-6 py-2.5 font-semibold text-navy transition-colors hover:bg-border">
            Open Review
          </Link>
          <Link href="/speaking"
            className="rounded-xl bg-background-light px-6 py-2.5 font-semibold text-navy transition-colors hover:bg-border">
            Back to Speaking
          </Link>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}

function humanName(id: string): string {
  switch (id.toLowerCase()) {
    case 'intelligibility': return 'Intelligibility';
    case 'fluency': return 'Fluency';
    case 'appropriateness': return 'Appropriateness of Language';
    case 'grammar_expression': return 'Grammar & Expression';
    default: return id.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
  }
}
