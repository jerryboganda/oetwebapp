'use client';

import { useState, useEffect, Suspense } from 'react';
import { useParams } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
  CheckCircle2,
  XCircle,
  ChevronDown,
  ChevronUp,
  ArrowRight,
  Target,
  Quote,
  AlertTriangle,
  AlertCircle,
  Loader2
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchListeningResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ListeningResult } from '@/lib/mock-data';

function ListeningResultsContent() {
  const params = useParams();
  const id = params?.id as string;

  const [result, setResult] = useState<ListeningResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [expandedItems, setExpandedItems] = useState<Record<string, boolean>>({});
  const [revealedTranscripts, setRevealedTranscripts] = useState<Record<string, boolean>>({});

  useEffect(() => {
    fetchListeningResult(id)
      .then(r => {
        setResult(r);
        // Expand incorrect by default
        const expanded: Record<string, boolean> = {};
        r.questions.forEach(q => { expanded[q.id] = !q.isCorrect; });
        setExpandedItems(expanded);
        analytics.track('evaluation_viewed', { subtest: 'listening', taskId: id });
      })
      .finally(() => setLoading(false));
  }, [id]);

  const toggleItem = (itemId: string) => {
    setExpandedItems(prev => ({ ...prev, [itemId]: !prev[itemId] }));
  };

  const toggleTranscript = (itemId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setRevealedTranscripts(prev => ({ ...prev, [itemId]: !prev[itemId] }));
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Listening Results" backHref="/listening">
        <div className="space-y-6">
          <Skeleton className="h-64 rounded-[32px]" />
          <Skeleton className="h-32 rounded-[24px]" />
          <Skeleton className="h-48 rounded-[24px]" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!result) {
    return (
      <LearnerDashboardShell pageTitle="Listening Results" backHref="/listening">
        <div className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-4">
          <AlertCircle className="w-12 h-12 text-rose-500" />
          <h2 className="text-xl font-black text-navy">Result not found</h2>
          <Link href="/listening"><Button variant="ghost">Back to Listening</Button></Link>
        </div>
      </LearnerDashboardShell>
    );
  }

  const percentage = Math.round((result.score / result.total) * 100);

  return (
    <LearnerDashboardShell pageTitle="Listening Results" subtitle={result.title} backHref="/listening">
      <div className="space-y-8 pb-24">

        {/* Score Overview */}
        <motion.section
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="bg-surface rounded-[32px] border border-gray-200 p-8 sm:p-10 text-center shadow-sm flex flex-col items-center"
        >
          <div className="relative w-32 h-32 mb-6">
            <svg
              className="w-full h-full transform -rotate-90"
              viewBox="0 0 100 100"
              role="img"
              aria-label={`Score ${result.score} out of ${result.total}, ${percentage}%`}
            >
              <circle cx="50" cy="50" r="40" fill="transparent" stroke="#f3f4f6" strokeWidth="8" />
              <circle
                cx="50" cy="50" r="40" fill="transparent"
                stroke={percentage >= 80 ? '#10b981' : percentage >= 50 ? '#f59e0b' : '#ef4444'}
                strokeWidth="8"
                strokeDasharray={`${2 * Math.PI * 40}`}
                strokeDashoffset={`${2 * Math.PI * 40 * (1 - percentage / 100)}`}
                strokeLinecap="round"
                className="transition-all duration-1000 ease-out"
              />
            </svg>
            <div className="absolute inset-0 flex flex-col items-center justify-center">
              <span className="text-3xl font-black text-navy">{result.score}</span>
              <span className="text-xs font-bold text-muted uppercase tracking-widest border-t border-gray-200 pt-1 mt-1 w-12 text-center">
                OF {result.total}
              </span>
            </div>
          </div>
          <h2 className="text-xl font-black text-navy mb-2">
            {percentage >= 80 ? 'Excellent Listening!' : percentage >= 50 ? 'Good Effort' : 'Needs More Practice'}
          </h2>
          <p className="text-sm text-muted max-w-md">
            Review your answers below. Pay close attention to the distractor explanations to avoid common traps in the future.
          </p>
        </motion.section>

        {/* Recommended Next Drill */}
        {result.recommendedDrill && (
          <motion.section
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1 }}
          >
            <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Recommended Next Step</h2>
            <Link href={`/listening/drills/${result.recommendedDrill.id}`} className="block bg-indigo-50 rounded-[24px] border border-indigo-100 p-6 hover:shadow-md hover:border-indigo-200 transition-all group">
              <div className="flex items-start gap-4">
                <div className="w-12 h-12 rounded-2xl bg-indigo-100 flex items-center justify-center shrink-0">
                  <Target className="w-6 h-6 text-indigo-600" />
                </div>
                <div className="flex-1">
                  <h3 className="text-lg font-black text-indigo-900 mb-1 group-hover:text-indigo-700 transition-colors">
                    {result.recommendedDrill.title}
                  </h3>
                  <p className="text-sm text-indigo-700/80 mb-4 leading-relaxed">
                    {result.recommendedDrill.description}
                  </p>
                  <span className="inline-flex items-center gap-2 text-sm font-bold text-indigo-600 bg-white px-4 py-2 rounded-xl shadow-sm group-hover:bg-indigo-600 group-hover:text-white transition-colors">
                    Start Drill <ArrowRight className="w-4 h-4" />
                  </span>
                </div>
              </div>
            </Link>
          </motion.section>
        )}

        {/* Item-by-Item Review */}
        <motion.section
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2 }}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Detailed Review</h2>
          <div className="space-y-4">
            {result.questions.map((q) => {
              const isExpanded = expandedItems[q.id];
              return (
                <div key={q.id} className="bg-surface rounded-[24px] border border-gray-200 overflow-hidden shadow-sm">
                  <button
                    onClick={() => toggleItem(q.id)}
                    aria-expanded={isExpanded}
                    className="w-full flex items-start gap-4 p-5 sm:p-6 text-left hover:bg-gray-50 transition-colors"
                  >
                    <div className="shrink-0 mt-0.5">
                      {q.isCorrect ? (
                        <CheckCircle2 className="w-6 h-6 text-green-500" />
                      ) : (
                        <XCircle className="w-6 h-6 text-rose-500" />
                      )}
                    </div>
                    <div className="flex-1 pr-4">
                      <span className="text-xs font-black text-muted uppercase tracking-widest block mb-1">Question {q.number}</span>
                      <h3 className="text-base font-medium text-navy leading-relaxed">{q.text}</h3>
                    </div>
                    <div className="shrink-0 text-muted">
                      {isExpanded ? <ChevronUp className="w-5 h-5" /> : <ChevronDown className="w-5 h-5" />}
                    </div>
                  </button>

                  <AnimatePresence>
                    {isExpanded && (
                      <motion.div
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        className="border-t border-gray-100"
                      >
                        <div className="p-5 sm:p-6 space-y-6 bg-gray-50/50">
                          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                            <div className={`p-4 rounded-xl border ${q.isCorrect ? 'bg-green-50 border-green-100' : 'bg-rose-50 border-rose-100'}`}>
                              <span className={`text-[10px] font-black uppercase tracking-widest block mb-2 ${q.isCorrect ? 'text-green-600' : 'text-rose-600'}`}>
                                Your Answer
                              </span>
                              <p className={`text-sm font-medium ${q.isCorrect ? 'text-green-900' : 'text-rose-900'}`}>
                                {q.userAnswer}
                              </p>
                            </div>
                            {!q.isCorrect && (
                              <div className="p-4 rounded-xl border bg-green-50 border-green-100">
                                <span className="text-[10px] font-black uppercase tracking-widest block mb-2 text-green-600">
                                  Correct Answer
                                </span>
                                <p className="text-sm font-medium text-green-900">{q.correctAnswer}</p>
                              </div>
                            )}
                          </div>

                          {!q.isCorrect && q.distractorExplanation && (
                            <div className="flex items-start gap-3 p-4 rounded-xl bg-amber-50 border border-amber-100">
                              <AlertTriangle className="w-5 h-5 text-amber-500 shrink-0 mt-0.5" />
                              <div>
                                <span className="text-xs font-black text-amber-700 uppercase tracking-widest block mb-1">Distractor Trap</span>
                                <p className="text-sm text-amber-900 leading-relaxed">{q.distractorExplanation}</p>
                              </div>
                            </div>
                          )}

                          <div>
                            <span className="text-xs font-black text-muted uppercase tracking-widest block mb-2">Explanation</span>
                            <p className="text-sm text-gray-700 leading-relaxed">{q.explanation}</p>
                          </div>

                          {q.allowTranscriptReveal && q.transcriptExcerpt && (
                            <div className="pt-2">
                              {revealedTranscripts[q.id] ? (
                                <div className="p-4 rounded-xl bg-surface border border-gray-200 relative">
                                  <Quote className="w-8 h-8 text-gray-100 absolute top-2 left-2" />
                                  <p className="text-sm text-gray-700 italic relative z-10 pl-6 border-l-2 border-primary">
                                    {q.transcriptExcerpt}
                                  </p>
                                  <button
                                    onClick={(e) => toggleTranscript(q.id, e)}
                                    className="text-xs font-bold text-muted hover:text-gray-600 mt-3 uppercase tracking-widest"
                                  >
                                    Hide Transcript
                                  </button>
                                </div>
                              ) : (
                                <button
                                  onClick={(e) => toggleTranscript(q.id, e)}
                                  className="inline-flex items-center gap-2 text-sm font-bold text-primary bg-primary/5 hover:bg-primary/10 px-4 py-2 rounded-lg transition-colors"
                                >
                                  <Quote className="w-4 h-4" /> Reveal Transcript Excerpt
                                </button>
                              )}
                            </div>
                          )}
                        </div>
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>
              );
            })}
          </div>
        </motion.section>

      </div>
    </LearnerDashboardShell>
  );
}

export default function ListeningResults() {
  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Listening Results" backHref="/listening">
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </LearnerDashboardShell>
    }>
      <ListeningResultsContent />
    </Suspense>
  );
}
