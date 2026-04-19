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
  Lightbulb,
  AlertCircle,
  Loader2
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchReadingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ReadingResult } from '@/lib/mock-data';

function ReadingResultsContent({ id }: { id: string }) {
  const [result, setResult] = useState<ReadingResult | null>(null);
  const [loading, setLoading] = useState(() => Boolean(id));
  const [expandedItems, setExpandedItems] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    fetchReadingResult(id)
      .then(r => {
        if (cancelled) return;
        if (!r) return;
        setResult(r);
        analytics.track('evaluation_viewed', { subtest: 'reading', taskId: id });
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  const toggleItem = (itemId: string) => {
    setExpandedItems(prev => ({ ...prev, [itemId]: !prev[itemId] }));
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Reading Results" backHref="/reading">
        <div className="space-y-6">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <Skeleton className="h-48 rounded-[24px]" />
            <Skeleton className="h-48 rounded-[24px] md:col-span-2" />
          </div>
          <Skeleton className="h-40 rounded-[24px]" />
          <Skeleton className="h-64 rounded-[24px]" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!result) {
    return (
      <LearnerDashboardShell pageTitle="Reading Results" backHref="/reading">
        <div className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-4">
          <AlertCircle className="w-12 h-12 text-rose-500" />
          <h2 className="text-xl font-black text-navy">Result not found</h2>
          <Link href="/reading"><Button variant="ghost">Back to Reading</Button></Link>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={`Results: ${result.title}`} backHref="/reading">
      <div className="space-y-8 pb-20">

        {/* Top Section: Score & Recommendation */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">

          {/* Score Card */}
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className="bg-surface rounded-[24px] border border-gray-200 p-8 shadow-sm flex flex-col items-center justify-center text-center md:col-span-1"
          >
            <div className="w-24 h-24 rounded-full border-8 border-primary/20 flex items-center justify-center mb-4 relative">
              <svg
                className="absolute inset-0 w-full h-full -rotate-90"
                viewBox="0 0 100 100"
                role="img"
                aria-label={`Score ${result.score} out of ${result.totalQuestions}, ${result.percentage}%`}
              >
                <circle
                  cx="50"
                  cy="50"
                  r="46"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="8"
                  className="text-primary"
                  strokeDasharray={`${result.percentage * 2.89} 289`}
                  strokeLinecap="round"
                />
              </svg>
              <span className="text-3xl font-black text-navy">
                {result.score}
                <span className="text-lg text-muted">/{result.totalQuestions}</span>
              </span>
            </div>
            <h2 className="text-sm font-bold text-muted uppercase tracking-widest mb-1">Total Score</h2>
            <div className="text-2xl font-black text-navy">{result.percentage}%</div>
          </motion.div>

          {/* Recommended Next Drill */}
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1 }}
            className="bg-navy rounded-[24px] p-8 shadow-md text-white md:col-span-2 flex flex-col justify-center relative overflow-hidden"
          >
            <div className="absolute top-0 right-0 w-64 h-64 bg-white/5 rounded-full blur-3xl -mr-20 -mt-20 pointer-events-none" />
            <div className="flex items-center gap-2 mb-4">
              <Lightbulb className="w-5 h-5 text-yellow-400" />
              <h2 className="text-sm font-bold text-white/70 uppercase tracking-widest">Recommended Next Step</h2>
            </div>
            <h3 className="text-2xl font-black mb-2">Practice Inference Questions</h3>
            <p className="text-white/80 leading-relaxed mb-6 max-w-lg">
              Focus on reading between the lines and understanding implied meaning in clinical texts.
            </p>
            <div>
              <Link
                href="/reading"
                className="inline-flex items-center gap-2 bg-white text-navy px-6 py-3 rounded-xl text-sm font-black hover:bg-gray-100 transition-colors"
              >
                Back to Reading <ArrowRight className="w-4 h-4" />
              </Link>
            </div>
          </motion.div>
        </div>

        {/* Error-Type Clustering */}
        {result.errorClusters && result.errorClusters.length > 0 && (
          <motion.section
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2 }}
          >
            <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Error Pattern Analysis</h2>
            <div className="bg-surface rounded-[24px] border border-gray-200 p-6 shadow-sm">
              <div className="space-y-6">
                {result.errorClusters.map((cluster, idx) => (
                  <div key={idx}>
                    <div className="flex items-center justify-between mb-2">
                      <span className="text-sm font-bold text-navy">{cluster.type}</span>
                      <span className="text-sm font-bold text-muted">
                        {cluster.total - cluster.count} / {cluster.total} Correct
                      </span>
                    </div>
                    <div className="w-full h-3 bg-gray-100 rounded-full overflow-hidden">
                      <div
                        className={`h-full rounded-full ${cluster.percentage === 100 ? 'bg-green-500' : cluster.percentage >= 50 ? 'bg-amber-500' : 'bg-rose-500'}`}
                        style={{ width: `${cluster.percentage}%` }}
                      />
                    </div>
                    {cluster.count > 0 && (
                      <p className="text-xs text-rose-600 font-medium mt-2 flex items-center gap-1">
                        <AlertCircle className="w-3 h-3" />
                        You missed {cluster.count} question{cluster.count > 1 ? 's' : ''} of this type.
                      </p>
                    )}
                  </div>
                ))}
              </div>
            </div>
          </motion.section>
        )}

        {/* Item-by-Item Review */}
        <motion.section
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.3 }}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Item-by-Item Review</h2>
          <div className="space-y-4">
            {result.items.map((item) => {
              const isExpanded = expandedItems[item.id];
              return (
                <div
                  key={item.id}
                  className={`bg-surface rounded-[24px] border transition-all overflow-hidden ${
                    item.isCorrect ? 'border-gray-200' : 'border-rose-200 shadow-sm'
                  }`}
                >
                  <button
                    onClick={() => toggleItem(item.id)}
                    aria-expanded={expandedItems[item.id] ?? false}
                    className="w-full flex items-start gap-4 p-6 text-left hover:bg-gray-50 transition-colors"
                  >
                    <div className="shrink-0 mt-0.5">
                      {item.isCorrect ? (
                        <CheckCircle2 className="w-6 h-6 text-green-500" />
                      ) : (
                        <XCircle className="w-6 h-6 text-rose-500" />
                      )}
                    </div>
                    <div className="flex-1">
                      <div className="flex items-center gap-3 mb-2">
                        <span className="text-xs font-black text-muted uppercase tracking-widest">Question {item.number}</span>
                        <span className="text-[10px] font-bold bg-gray-100 text-gray-600 px-2 py-0.5 rounded-md">{item.errorType}</span>
                      </div>
                      <h3 className="text-base font-medium text-navy leading-relaxed pr-8">{item.text}</h3>
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
                        className="border-t border-gray-100 bg-gray-50/50"
                      >
                        <div className="p-6 space-y-6">
                          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                            <div className={`p-4 rounded-xl border ${item.isCorrect ? 'bg-green-50 border-green-100' : 'bg-rose-50 border-rose-100'}`}>
                              <span className={`text-[10px] font-black uppercase tracking-widest block mb-1 ${item.isCorrect ? 'text-green-700' : 'text-rose-700'}`}>
                                Your Answer
                              </span>
                              <p className={`text-sm font-medium ${item.isCorrect ? 'text-green-900' : 'text-rose-900'}`}>
                                {item.userAnswer}
                              </p>
                            </div>
                            {!item.isCorrect && (
                              <div className="p-4 rounded-xl border bg-green-50 border-green-100">
                                <span className="text-[10px] font-black uppercase tracking-widest text-green-700 block mb-1">
                                  Correct Answer
                                </span>
                                <p className="text-sm font-medium text-green-900">{item.correctAnswer}</p>
                              </div>
                            )}
                          </div>
                          <div className="bg-surface p-5 rounded-xl border border-gray-200">
                            <div className="flex items-center gap-2 mb-2">
                              <Lightbulb className="w-4 h-4 text-amber-500" />
                              <span className="text-xs font-black text-navy uppercase tracking-widest">Explanation</span>
                            </div>
                            <p className="text-sm text-gray-600 leading-relaxed">{item.explanation}</p>
                          </div>
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

export default function ReadingResults() {
  const params = useParams();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] : rawId ?? '';

  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Reading Results" backHref="/reading">
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </LearnerDashboardShell>
    }>
      <ReadingResultsContent key={id || 'missing'} id={id} />
    </Suspense>
  );
}
