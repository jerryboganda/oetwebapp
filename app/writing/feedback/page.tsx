'use client';

import { useState, useEffect, useMemo } from 'react';
import { motion } from 'motion/react';
import {
  ChevronLeft,
  MessageSquare,
  MinusCircle,
  XCircle,
  Lightbulb,
  ArrowRight,
} from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { AppShell } from '@/components/layout/app-shell';
import { CriterionBreakdownCard } from '@/components/domain/criterion-breakdown-card';
import { WritingIssueList, type IssueType } from '@/components/domain/writing-issue-list';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult, CriterionFeedback, AnchoredComment } from '@/lib/mock-data';

/* --- Inline Highlight helper (needs activeComment state) --- */
function Highlight({ id, children, active, onToggle }: { id: string; children: React.ReactNode; active: boolean; onToggle: (id: string | null) => void }) {
  return (
    <span
      onClick={() => onToggle(active ? null : id)}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onToggle(active ? null : id); } }}
      role="button"
      tabIndex={0}
      aria-pressed={active}
      className={`cursor-pointer transition-colors rounded px-1 py-0.5 ${
        active ? 'bg-blue-200 border-b-2 border-blue-500' : 'bg-yellow-100 hover:bg-yellow-200 border-b border-transparent'
      }`}
    >
      {children}
    </span>
  );
}

export default function WritingDetailedFeedback() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? 'wr-001';
  const [result, setResult] = useState<WritingResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [activeComment, setActiveComment] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { content: 'feedback', resultId, subtest: 'writing' });
    fetchWritingResult(resultId).then(setResult).finally(() => setLoading(false));
  }, [resultId]);

  /* Flatten all issues from criteria for the WritingIssueList */
  const allIssues = useMemo(() => {
    if (!result) return [];
    const issues: { id: string; type: IssueType; criterion: string; text: string }[] = [];
    result.criteria.forEach((c) => {
      c.omissions.forEach((t, i) => issues.push({ id: `${c.name}-om-${i}`, type: 'omission', criterion: c.name, text: t }));
      c.unnecessaryDetails.forEach((t, i) => issues.push({ id: `${c.name}-un-${i}`, type: 'unnecessary', criterion: c.name, text: t }));
      c.revisionSuggestions.forEach((t, i) => issues.push({ id: `${c.name}-sg-${i}`, type: 'suggestion', criterion: c.name, text: t }));
    });
    return issues;
  }, [result]);

  if (loading) {
    return (
      <AppShell pageTitle="Detailed Feedback">
        <div className="flex min-h-[calc(100dvh-64px)] flex-col md:h-[calc(100dvh-64px)] md:flex-row">
          <div className="w-full md:w-1/2 p-6"><Skeleton className="h-full rounded-2xl" /></div>
          <div className="w-full md:w-1/2 p-6 space-y-4">{[1,2,3].map(i => <Skeleton key={i} className="h-40 rounded-2xl" />)}</div>
        </div>
      </AppShell>
    );
  }

  if (!result) return <AppShell pageTitle="Not Found"><div className="p-10 text-center text-muted">Result not found.</div></AppShell>;

  /* Build a map of anchored-comment id → AnchoredComment for quick lookup */
  const commentMap = new Map<string, AnchoredComment>();
  result.criteria.forEach(c => c.anchoredComments.forEach(ac => commentMap.set(ac.id, ac)));

  return (
    <AppShell pageTitle="Detailed Feedback" distractionFree>
      {/* Toolbar */}
      <header className="bg-white border-b border-gray-200 shrink-0 px-4 py-3 sm:px-6 flex flex-wrap items-center justify-between gap-3 z-10">
        <div className="flex min-w-0 items-center gap-3 sm:gap-4">
          <Link href={`/writing/result?id=${resultId}`} className="text-gray-500 hover:text-navy transition-colors"><ChevronLeft className="w-5 h-5" /></Link>
          <div className="min-w-0">
            <h1 className="font-bold text-lg text-navy leading-tight">Detailed Feedback</h1>
            <div className="truncate text-xs text-muted">{result.taskTitle}</div>
          </div>
        </div>
        <Link href={`/writing/player?taskId=${result.taskId}`}>
          <Button size="sm">Revise Submission</Button>
        </Link>
      </header>

      <main className="flex-1 min-h-0 flex flex-col overflow-y-auto md:flex-row md:overflow-hidden">
        {/* Left Pane: Submission Text (re-use the existing anchored-highlight inline pattern) */}
        <div className="w-full md:w-1/2 border-r border-gray-200 bg-white p-6 md:overflow-y-auto md:p-10">
          <div className="max-w-2xl mx-auto">
            <div className="flex items-center justify-between mb-8">
              <h2 className="text-sm font-bold text-muted uppercase tracking-wider">Your Submission</h2>
              <span className="text-xs font-medium bg-gray-100 text-gray-600 px-2 py-1 rounded">Click highlights to view comments</span>
            </div>
            {/* Render submission text with highlights for each anchored comment */}
            <div className="text-lg leading-relaxed text-gray-800 space-y-6 font-serif">
              {result.criteria.flatMap(c => c.anchoredComments).length > 0 ? (
                <>
                  {result.criteria.map(criterion =>
                    criterion.anchoredComments.map(ac => (
                      <div key={ac.id} className="mb-4">
                        <Highlight id={ac.id} active={activeComment === ac.id} onToggle={setActiveComment}>
                          &quot;{ac.text}&quot;
                        </Highlight>
                        {activeComment === ac.id && (
                          <motion.div initial={{ opacity: 0, y: -4 }} animate={{ opacity: 1, y: 0 }} className="mt-2 ml-4 p-3 bg-blue-50 border border-blue-200 rounded-lg text-sm text-blue-900">
                            <span className="font-semibold">{criterion.name}:</span> {ac.comment}
                          </motion.div>
                        )}
                      </div>
                    ))
                  )}
                </>
              ) : (
                <p className="text-muted italic">No anchored comments available.</p>
              )}
            </div>
          </div>
        </div>

        {/* Right Pane: Criteria Panels + Issue List */}
        <div className="w-full md:w-1/2 bg-gray-50 p-6 md:overflow-y-auto md:p-8">
          <div className="max-w-2xl mx-auto space-y-6">
            {result.criteria.map((criterion, index) => (
              <motion.div key={criterion.name} initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.08 }}>
                <Card className="p-6">
                  {/* Header with score */}
                  <div className="flex items-start justify-between mb-5 pb-5 border-b border-gray-100">
                    <div>
                      <h3 className="text-xl font-bold text-navy mb-1">{criterion.name}</h3>
                      <div className="text-sm text-muted">Criterion Score</div>
                    </div>
                    <Badge variant={criterion.score / criterion.maxScore >= 0.75 ? 'success' : criterion.score / criterion.maxScore >= 0.5 ? 'warning' : 'danger'}>
                      {criterion.score} / {criterion.maxScore}
                    </Badge>
                  </div>

                  {/* Explanation */}
                  <p className="text-gray-700 leading-relaxed mb-6">{criterion.explanation}</p>

                  {/* Anchored Comments */}
                  {criterion.anchoredComments.length > 0 && (
                    <div className="mb-6">
                      <h4 className="text-xs font-bold text-muted uppercase tracking-wider mb-3 flex items-center gap-2">
                        <MessageSquare className="w-4 h-4" /> Anchored Comments
                      </h4>
                      <div className="space-y-3">
                        {criterion.anchoredComments.map(comment => {
                          const isActive = activeComment === comment.id;
                          return (
                            <button key={comment.id} type="button" onClick={() => setActiveComment(isActive ? null : comment.id)}
                              className={`w-full text-left p-4 rounded-xl border cursor-pointer transition-all ${isActive ? 'bg-blue-50 border-blue-300 shadow-sm' : 'bg-gray-50 border-gray-200 hover:border-gray-300'}`}>
                              <div className="text-sm text-gray-500 mb-2 italic border-l-2 border-gray-300 pl-3">&quot;{comment.text}&quot;</div>
                              <div className="text-sm text-navy font-medium">{comment.comment}</div>
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  {/* Omissions & Unnecessary Details */}
                  {(criterion.omissions.length > 0 || criterion.unnecessaryDetails.length > 0) && (
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
                      {criterion.omissions.length > 0 && (
                        <div className="bg-red-50 rounded-xl p-4 border border-red-100">
                          <h4 className="text-xs font-bold text-red-700 uppercase tracking-wider mb-3 flex items-center gap-2"><MinusCircle className="w-4 h-4" /> Omissions</h4>
                          <ul className="space-y-2">{criterion.omissions.map((item, i) => (<li key={i} className="text-sm text-red-900 flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-red-400 mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                        </div>
                      )}
                      {criterion.unnecessaryDetails.length > 0 && (
                        <div className="bg-amber-50 rounded-xl p-4 border border-amber-100">
                          <h4 className="text-xs font-bold text-amber-700 uppercase tracking-wider mb-3 flex items-center gap-2"><XCircle className="w-4 h-4" /> Unnecessary</h4>
                          <ul className="space-y-2">{criterion.unnecessaryDetails.map((item, i) => (<li key={i} className="text-sm text-amber-900 flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-amber-400 mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Revision Suggestions */}
                  {criterion.revisionSuggestions.length > 0 && (
                    <div className="bg-green-50 rounded-xl p-4 border border-green-100">
                      <h4 className="text-xs font-bold text-green-700 uppercase tracking-wider mb-3 flex items-center gap-2"><Lightbulb className="w-4 h-4" /> Revision Suggestions</h4>
                      <ul className="space-y-3">{criterion.revisionSuggestions.map((item, i) => (<li key={i} className="text-sm text-green-900 flex items-start gap-2"><ArrowRight className="w-4 h-4 text-green-500 shrink-0 mt-0.5" /><span className="leading-snug">{item}</span></li>))}</ul>
                    </div>
                  )}
                </Card>
              </motion.div>
            ))}

            {/* Aggregated Issues (WritingIssueList) */}
            {allIssues.length > 0 && (
              <div className="pt-4">
                <h3 className="text-sm font-bold text-muted uppercase tracking-wider mb-3">All Issues Summary</h3>
                <WritingIssueList issues={allIssues} />
              </div>
            )}
          </div>
        </div>
      </main>
    </AppShell>
  );
}
