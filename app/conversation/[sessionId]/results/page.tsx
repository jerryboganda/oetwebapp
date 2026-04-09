'use client';

import { useEffect, useState } from 'react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { MessageSquare, BarChart3, Target, ChevronRight, RotateCcw, ArrowLeft, Star, AlertTriangle, CheckCircle2 } from 'lucide-react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { getConversationEvaluation } from '@/lib/api';

type CriterionScore = {
  criterionCode: string;
  criterionName: string;
  score: number;
  maxScore: number;
  explanation: string;
  confidenceBand: string;
};

type TurnAnnotation = {
  turnNumber: number;
  role: string;
  annotations: Array<{ type: string; text: string; suggestion: string | null }>;
};

type Evaluation = {
  sessionId: string;
  state: string;
  ready: boolean;
  overallScore: number;
  overallGrade: string;
  criterionScores: CriterionScore[];
  turnAnnotations: TurnAnnotation[];
  strengths: string[];
  improvements: string[];
  suggestions: string[];
  turnCount: number;
  durationSeconds: number;
  evaluatedAt: string | null;
};

const GRADE_COLORS: Record<string, string> = {
  'A': 'from-emerald-400 to-green-500',
  'B': 'from-violet-400 to-purple-500',
  'B+': 'from-violet-400 to-purple-500',
  'C+': 'from-yellow-400 to-amber-500',
  'C': 'from-orange-400 to-amber-500',
  'D': 'from-red-400 to-rose-500',
};

export default function ConversationResultsPage() {
  const params = useParams();
  const router = useRouter();
  const sessionId = params.sessionId as string;

  const [evaluation, setEvaluation] = useState<Evaluation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('conversation_results_viewed', { sessionId });
    const poll = async () => {
      try {
        const data = await getConversationEvaluation(sessionId) as Evaluation;
        setEvaluation(data);
        if (!data.ready && data.state === 'evaluating') {
          setTimeout(poll, 3000);
        }
      } catch {
        setError('Failed to load evaluation results.');
      } finally {
        setLoading(false);
      }
    };
    poll();
  }, [sessionId]);

  const formatDuration = (s: number) => s >= 60 ? `${Math.floor(s / 60)}m ${s % 60}s` : `${s}s`;

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
          <div className="w-16 h-16 rounded-full bg-purple-100 dark:bg-purple-900/30 flex items-center justify-center mx-auto mb-4">
            <BarChart3 className="w-8 h-8 text-purple-500 animate-pulse" />
          </div>
          <h2 className="text-xl font-bold text-gray-900 dark:text-white mb-2">Evaluating conversation...</h2>
          <p className="text-gray-500 text-sm">This usually takes a few seconds. The page will update automatically.</p>
        </MotionSection>
      </LearnerDashboardShell>
    );
  }

  if (!evaluation) return null;

  const gradeGradient = GRADE_COLORS[evaluation.overallGrade] ?? 'from-gray-400 to-gray-500';

  return (
    <LearnerDashboardShell>
      <div className="max-w-3xl mx-auto">
        {/* Back link */}
        <Link href="/conversation" className="inline-flex items-center gap-1 text-sm text-gray-500 hover:text-purple-600 mb-4 transition-colors">
          <ArrowLeft className="w-4 h-4" /> Back to Conversations
        </Link>

        {/* Overall Score */}
        <MotionSection
          className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6 mb-6 text-center"
        >
          <div className="text-sm font-bold uppercase tracking-wider text-gray-500 mb-3">
            Conversation Performance
          </div>
          <div className={`inline-flex items-center justify-center w-24 h-24 rounded-full bg-gradient-to-br ${gradeGradient} text-white text-3xl font-bold mb-3 shadow-lg`}>
            {evaluation.overallGrade}
          </div>
          <div className="text-2xl font-bold text-gray-900 dark:text-white mb-1">
            {Math.round(evaluation.overallScore)}%
          </div>
          <div className="flex items-center justify-center gap-4 text-sm text-gray-500">
            <span>{evaluation.turnCount} turns</span>
            <span>•</span>
            <span>{formatDuration(evaluation.durationSeconds)}</span>
          </div>
        </MotionSection>

        {/* Criterion Scores */}
        <MotionSection
          delayIndex={1}
          className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6 mb-6"
        >
          <h2 className="text-lg font-bold text-gray-900 dark:text-white mb-4 flex items-center gap-2">
            <Target className="w-5 h-5 text-purple-500" /> Criterion Breakdown
          </h2>
          <div className="space-y-4">
            {evaluation.criterionScores.map((criterion, i) => {
              const pct = (criterion.score / criterion.maxScore) * 100;
              return (
                <MotionItem
                  key={criterion.criterionCode}
                  delayIndex={i}
                >
                  <div className="flex items-center justify-between mb-1">
                    <span className="text-sm font-semibold text-gray-900 dark:text-white">{criterion.criterionName}</span>
                    <span className="text-sm font-bold text-purple-600 dark:text-purple-400">
                      {criterion.score} / {criterion.maxScore}
                    </span>
                  </div>
                  <div className="w-full bg-gray-100 dark:bg-gray-700 rounded-full h-2.5 mb-1.5">
                    <div
                      className={`h-2.5 rounded-full transition-all duration-700 ${pct >= 70 ? 'bg-green-500' : pct >= 50 ? 'bg-yellow-500' : 'bg-red-500'}`}
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                  <p className="text-xs text-gray-500">{criterion.explanation}</p>
                </MotionItem>
              );
            })}
          </div>
        </MotionSection>

        {/* Strengths & Improvements */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-6">
          <MotionSection
            delayIndex={2}
            className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-5"
          >
            <h3 className="text-sm font-bold text-green-600 dark:text-green-400 mb-3 flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4" /> Strengths
            </h3>
            <ul className="space-y-2">
              {evaluation.strengths.map((s, i) => (
                <li key={i} className="text-sm text-gray-700 dark:text-gray-300 flex items-start gap-2">
                  <Star className="w-3.5 h-3.5 text-green-400 mt-0.5 shrink-0" />
                  {s}
                </li>
              ))}
            </ul>
          </MotionSection>

          <MotionSection
            delayIndex={3}
            className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-5"
          >
            <h3 className="text-sm font-bold text-amber-600 dark:text-amber-400 mb-3 flex items-center gap-2">
              <AlertTriangle className="w-4 h-4" /> Areas to Improve
            </h3>
            <ul className="space-y-2">
              {evaluation.improvements.map((s, i) => (
                <li key={i} className="text-sm text-gray-700 dark:text-gray-300 flex items-start gap-2">
                  <ChevronRight className="w-3.5 h-3.5 text-amber-400 mt-0.5 shrink-0" />
                  {s}
                </li>
              ))}
            </ul>
          </MotionSection>
        </div>

        {/* Turn Annotations */}
        {evaluation.turnAnnotations.length > 0 && (
          <MotionSection
            delayIndex={4}
            className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6 mb-6"
          >
            <h2 className="text-lg font-bold text-gray-900 dark:text-white mb-4 flex items-center gap-2">
              <MessageSquare className="w-5 h-5 text-purple-500" /> Turn-by-Turn Feedback
            </h2>
            <div className="space-y-3">
              {evaluation.turnAnnotations.map((turn) => (
                <div key={turn.turnNumber} className="border border-gray-100 dark:border-gray-700 rounded-xl p-3">
                  <div className="text-xs font-semibold text-gray-400 mb-2">
                    Turn {turn.turnNumber} · {turn.role === 'learner' ? 'Your response' : 'AI Partner'}
                  </div>
                  {turn.annotations.map((a, i) => (
                    <div key={i} className="flex items-start gap-2 mb-1.5 last:mb-0">
                      <span className={`text-xs font-bold px-1.5 py-0.5 rounded ${
                        a.type === 'strength' ? 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300'
                        : a.type === 'error' ? 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300'
                        : 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300'
                      }`}>
                        {a.type}
                      </span>
                      <div className="text-sm text-gray-700 dark:text-gray-300">
                        {a.text}
                        {a.suggestion && (
                          <span className="block text-xs text-purple-500 mt-0.5">💡 {a.suggestion}</span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
          </MotionSection>
        )}

        {/* Suggestions */}
        {evaluation.suggestions.length > 0 && (
          <MotionSection
            delayIndex={5}
            className="bg-gradient-to-br from-purple-50 to-indigo-50 dark:from-purple-950/30 dark:to-indigo-950/30 rounded-2xl border border-purple-200/60 dark:border-purple-800/40 p-5 mb-6"
          >
            <h3 className="text-sm font-bold text-purple-700 dark:text-purple-300 mb-3">
              📋 Practice Suggestions
            </h3>
            <ul className="space-y-2">
              {evaluation.suggestions.map((s, i) => (
                <li key={i} className="text-sm text-gray-700 dark:text-gray-300">• {s}</li>
              ))}
            </ul>
          </MotionSection>
        )}

        {/* Actions */}
        <div className="flex items-center justify-center gap-4 py-6">
          <Link
            href="/conversation"
            className="px-6 py-2.5 bg-purple-600 hover:bg-purple-700 text-white rounded-xl font-semibold flex items-center gap-2 transition-colors"
          >
            <RotateCcw className="w-4 h-4" /> Practice Again
          </Link>
          <Link
            href="/speaking"
            className="px-6 py-2.5 bg-gray-100 hover:bg-gray-200 dark:bg-gray-700 dark:hover:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-xl font-semibold transition-colors"
          >
            Back to Speaking
          </Link>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
