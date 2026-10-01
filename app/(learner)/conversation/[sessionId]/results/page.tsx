'use client';

import { useEffect, useState } from 'react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import {
  MessageSquare, BarChart3, Target, ChevronRight, RotateCcw, ArrowLeft,
  Star, AlertTriangle, CheckCircle2, Volume2, Download,
} from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerSurfaceSectionHeader } from '@/components/domain';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { ProgressBar } from '@/components/ui/progress';
import { analytics } from '@/lib/analytics';
import { downloadConversationTranscript, getConversationEvaluation } from '@/lib/api';
import { resolveApiMediaUrl } from '@/lib/media-url';
import type { ConversationEvaluationResponse } from '@/lib/types/conversation';
import {
  conversationProjectedScaled,
  formatScaledScore,
  gradeSpeaking,
  oetGradeLabel,
  type OetGrade,
} from '@/lib/scoring';

const GRADE_TONES: Record<string, 'success' | 'info' | 'warning' | 'danger'> = {
  A: 'success',
  B: 'info',
  'C+': 'warning',
  C: 'warning',
  D: 'danger',
  E: 'danger',
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
      <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading">
        <Skeleton aria-hidden className="h-48 rounded-2xl" />
        <Skeleton aria-hidden className="h-64 rounded-2xl" />
        <Skeleton aria-hidden className="h-48 rounded-2xl" />
      </div>
    );
  }

  if (error) {
    return <InlineAlert variant="error">{error}</InlineAlert>;
  }

  if (evaluation && !evaluation.ready) {
    return (
      <MotionSection>
        <Card padding="lg" role="status" className="flex flex-col items-center py-12 text-center sm:py-16">
          <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-primary/10">
            <BarChart3 className="h-8 w-8 text-primary motion-safe:animate-pulse" aria-hidden />
          </div>
          <h1 className="mb-2 text-xl font-bold text-navy">Evaluating conversation…</h1>
          <p className="max-w-sm text-sm text-muted">This usually takes a few seconds. The page will update automatically.</p>
        </Card>
      </MotionSection>
    );
  }

  if (!evaluation) return null;

  // Never invent a grade/score: missing values render as unavailable, not "0/500 · Grade E".
  const scaled = evaluation.scaledScore ?? null;
  const grade: OetGrade | null = (evaluation.overallGrade as OetGrade | null | undefined) ?? (scaled != null ? gradeSpeaking(scaled).grade : null);
  const gradeTone = grade ? GRADE_TONES[grade] : undefined;
  const criteria = evaluation.criteria ?? [];
  const strengths = evaluation.strengths ?? [];
  const improvements = evaluation.improvements ?? [];
  const suggested = evaluation.suggestedPractice ?? [];
  const annotations = evaluation.turnAnnotations ?? [];
  const turns = evaluation.turns ?? [];
  const appliedRuleIds = evaluation.appliedRuleIds ?? [];
  const passScaled = evaluation.passScaled ?? gradeSpeaking(scaled ?? 0).requiredScaled;
  const passBadge = evaluation.passed == null
    ? null
    : evaluation.passed
      ? { label: `Above pass mark (${formatScaledScore(passScaled)})`, tone: 'success' as const }
      : { label: `Below pass mark (${formatScaledScore(passScaled)})`, tone: 'warning' as const };
  const sessionMeta = [
    `${evaluation.turnCount ?? 0} turns`,
    formatDuration(evaluation.durationSeconds ?? 0),
    ...(evaluation.rulebookVersion ? [`Rulebook v${evaluation.rulebookVersion}`] : []),
  ].join(' • ');

  return (
    <>
      <div>
        <Button asChild variant="outline" size="sm">
          <Link href="/conversation">
            <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Conversations
          </Link>
        </Button>
      </div>

      {/* The shared results header: the grade is the page's h1, the real scaled score counts up in the gauge. */}
      <ResultsScorePanel
        eyebrow="OET Speaking practice · Scaled score"
        icon={MessageSquare}
        title={grade ? oetGradeLabel(grade) : 'Score unavailable'}
        subtitle={sessionMeta}
        gaugeValue={scaled != null ? (scaled / 500) * 100 : 0}
        gaugeCenter={(
          <span className="text-2xl font-black text-navy">
            {scaled != null ? <CountUp value={scaled} /> : '—'}
          </span>
        )}
        gaugeLabel={scaled != null ? '/ 500' : undefined}
        gaugeColor={gradeTone ? `var(--color-${gradeTone})` : undefined}
        grade={passBadge}
        chartSlot={evaluation.advisory ? <p className="text-xs text-muted">{evaluation.advisory}</p> : undefined}
      />

      {criteria.length > 0 && (
        <MotionSection>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader icon={Target} title="OET Speaking Rubric" className="mb-4" />
            <div className="space-y-4">
              {criteria.map((criterion, i) => {
                const max = criterion.maxScore || 6;
                const pct = (criterion.score06 / max) * 100;
                const criterionPassed = gradeSpeaking(conversationProjectedScaled(criterion.score06)).passed;
                const label = humanName(criterion.id);
                return (
                  <MotionItem key={criterion.id} delayIndex={Math.min(i, 5)}>
                    <div className="mb-1 flex items-center justify-between gap-3">
                      <span className="text-sm font-semibold text-navy">{label}</span>
                      <span className="text-sm font-bold tabular-nums text-primary">
                        {criterion.score06.toFixed(1)} / {max.toFixed(0)}
                      </span>
                    </div>
                    <ProgressBar
                      value={criterion.score06}
                      max={max}
                      color={criterionPassed ? 'success' : pct >= 50 ? 'warning' : 'danger'}
                      ariaLabel={`${label}: ${criterion.score06.toFixed(1)} of ${max.toFixed(0)}`}
                      className="mb-1.5"
                    />
                    {criterion.evidence && (<p className="max-w-3xl text-xs text-muted">{criterion.evidence}</p>)}
                    {criterion.quotes && criterion.quotes.length > 0 && (
                      <ul className="mt-1 max-w-3xl space-y-0.5 ps-4 text-xs italic text-muted">
                        {criterion.quotes.slice(0, 3).map((q, qi) => (<li key={qi}>&ldquo;{q}&rdquo;</li>))}
                      </ul>
                    )}
                  </MotionItem>
                );
              })}
            </div>
          </Card>
        </MotionSection>
      )}

      {(strengths.length > 0 || improvements.length > 0) && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {strengths.length > 0 && (
            <MotionSection delayIndex={1}>
              <Card padding="md" className="h-full">
                <h2 className="mb-3 flex items-center gap-2 text-sm font-bold text-success-strong">
                  <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> Strengths
                </h2>
                <ul className="space-y-2">
                  {strengths.map((s, i) => (
                    <li key={i} className="flex items-start gap-2 text-sm text-navy">
                      <Star className="mt-0.5 h-3.5 w-3.5 shrink-0 text-success-strong" aria-hidden="true" />{s}
                    </li>
                  ))}
                </ul>
              </Card>
            </MotionSection>
          )}
          {improvements.length > 0 && (
            <MotionSection delayIndex={2}>
              <Card padding="md" className="h-full">
                <h2 className="mb-3 flex items-center gap-2 text-sm font-bold text-warning-strong">
                  <AlertTriangle className="h-4 w-4" aria-hidden="true" /> Areas to improve
                </h2>
                <ul className="space-y-2">
                  {improvements.map((s, i) => (
                    <li key={i} className="flex items-start gap-2 text-sm text-navy">
                      <ChevronRight className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning-strong rtl:rotate-180" aria-hidden="true" />{s}
                    </li>
                  ))}
                </ul>
              </Card>
            </MotionSection>
          )}
        </div>
      )}

      {/* A transcript can be long: each turn reveals on its own, never the whole card at once. */}
      {turns.length > 0 && (
        <Card padding="lg">
          <LearnerSurfaceSectionHeader
            icon={MessageSquare}
            title="Transcript"
            className="mb-4"
            action={(
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => void handleExport('txt')} disabled={exporting !== null}>
                  <Download className="h-4 w-4" aria-hidden="true" /> TXT
                </Button>
                <Button variant="outline" size="sm" onClick={() => void handleExport('pdf')} disabled={exporting !== null}>
                  <Download className="h-4 w-4" aria-hidden="true" /> PDF
                </Button>
              </div>
            )}
          />
          <div className="space-y-3">
            {turns.map((t, index) => {
              const turnAnnotations = annotations.filter((a) => a.turnNumber === t.turnNumber);
              const audioUrl = resolveApiMediaUrl(t.audioUrl);
              return (
                <MotionItem key={t.turnNumber} delayIndex={Math.min(index, 5)} className="rounded-xl border border-border p-3">
                  <div className="mb-2 flex items-center justify-between gap-3 text-xs font-semibold text-muted">
                    <span>Turn {t.turnNumber} · {t.role === 'learner' ? 'You' : 'AI Partner'}</span>
                    {t.confidence != null && t.role === 'learner' && (
                      <span className="text-3xs tabular-nums">ASR conf {(t.confidence * 100).toFixed(0)}%</span>
                    )}
                  </div>
                  <p className="max-w-3xl break-words text-sm text-navy">{t.content}</p>
                  {audioUrl && (
                    <audio controls preload="none" src={audioUrl} className="mt-2 w-full">
                      <Volume2 className="h-3 w-3" />
                    </audio>
                  )}
                  {turnAnnotations.length > 0 && (
                    <div className="mt-2 space-y-1.5">
                      {turnAnnotations.map((a) => (
                        <div key={a.id} className="flex items-start gap-2">
                          <span className={`tile-label shrink-0 rounded-md px-1.5 py-0.5 ${
                            a.type === 'strength'
                              ? 'bg-success/10 text-success-strong'
                              : a.type === 'error'
                              ? 'bg-danger/10 text-danger-strong'
                              : 'bg-warning/10 text-warning-strong'
                          }`}>{a.type}</span>
                          <div className="min-w-0 flex-1 text-sm text-navy">
                            {a.evidence}
                            {a.ruleId && (<span className="ms-2 font-mono text-3xs text-primary">{a.ruleId}</span>)}
                            {a.suggestion && (<div className="mt-0.5 text-xs text-primary">💡 {a.suggestion}</div>)}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </MotionItem>
              );
            })}
          </div>
        </Card>
      )}

      {suggested.length > 0 && (
        <MotionSection>
          <Card padding="md" className="border-primary/30 bg-primary/5">
            <h2 className="mb-3 text-sm font-bold text-primary">Practice suggestions</h2>
            <ul className="list-disc space-y-2 ps-5 text-sm text-navy marker:text-primary">
              {suggested.map((s, i) => (<li key={i}>{s}</li>))}
            </ul>
          </Card>
        </MotionSection>
      )}

      {appliedRuleIds.length > 0 && (
        <p className="text-center text-xs text-muted">Rules applied: {appliedRuleIds.join(', ')}</p>
      )}

      <div className="flex flex-wrap items-center justify-center gap-3">
        <Button asChild>
          <Link href="/conversation">
            <RotateCcw className="h-4 w-4" aria-hidden="true" /> Practice again
          </Link>
        </Button>
        <Button variant="outline" asChild>
          <Link href="/review">Open Review</Link>
        </Button>
        <Button variant="outline" asChild>
          <Link href="/speaking">Back to Speaking</Link>
        </Button>
      </div>
    </>
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
