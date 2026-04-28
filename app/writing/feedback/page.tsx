'use client';

import { useState, useEffect, useMemo } from 'react';
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
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { WritingIssueList, type IssueType } from '@/components/domain/writing-issue-list';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult, AnchoredComment } from '@/lib/mock-data';

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
        active ? 'bg-info/20 border-b-2 border-info' : 'bg-warning/10 hover:bg-warning/20 border-b border-transparent'
      }`}
    >
      {children}
    </span>
  );
}

export default function WritingDetailedFeedback() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? '';
  const [result, setResult] = useState<WritingResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [activeComment, setActiveComment] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { content: 'feedback', resultId, subtest: 'writing' });
    if (!resultId) {
      return;
    }
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

  if (!resultId) {
    return <AppShell pageTitle="Not Found"><div className="p-10 text-center text-muted">Open feedback from a completed writing result.</div></AppShell>;
  }

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
  const heroHighlights = [
    { icon: MessageSquare, label: 'Criteria', value: `${result.criteria.length}` },
    { icon: Lightbulb, label: 'Commentary', value: `${result.criteria.reduce((sum, c) => sum + c.anchoredComments.length, 0)}` },
    { icon: ArrowRight, label: 'Revision', value: 'Actionable' },
  ];

  return (
    <AppShell pageTitle="Detailed Feedback" distractionFree>
      <div className="mx-auto flex h-full w-full max-w-[1440px] flex-col gap-6 px-4 py-6 sm:px-6 lg:px-8">
      <LearnerPageHero
        eyebrow="Writing"
        icon={MessageSquare}
        title="Detailed Feedback"
        description="Review your submission, score breakdown, and revision prompts in one place."
        highlights={heroHighlights}
      />

      {/* Toolbar */}
      <header className="shrink-0 rounded-3xl border border-border bg-surface px-4 py-3 shadow-sm sm:px-6 flex flex-wrap items-center justify-between gap-3 z-10">
        <div className="flex min-w-0 items-center gap-3 sm:gap-4">
          <Link href={`/writing/result?id=${resultId}`} className="text-muted transition-colors hover:text-navy p-2 -m-2 touch-target"><ChevronLeft className="w-5 h-5" /></Link>
          <div className="min-w-0">
            <h1 className="font-bold text-lg leading-tight text-navy">Detailed Feedback</h1>
            <div className="truncate text-xs text-muted">{result.taskTitle}</div>
          </div>
        </div>
        <Link href={`/writing/player?taskId=${result.taskId}`}>
          <Button size="sm">Revise Submission</Button>
        </Link>
      </header>

      <main className="flex min-h-0 flex-1 flex-col overflow-y-auto md:flex-row md:gap-6 md:overflow-hidden">
        {/* Left Pane: Submission Text (re-use the existing anchored-highlight inline pattern) */}
        <div className="w-full rounded-3xl border border-border bg-surface p-6 shadow-sm md:w-1/2 md:overflow-y-auto md:p-10">
          <div className="mx-auto max-w-2xl">
            <div className="flex items-center justify-between mb-8">
              <h2 className="text-sm font-bold uppercase tracking-wider text-muted">Your Submission</h2>
              <span className="rounded bg-background-light px-2 py-1 text-xs font-medium text-muted">Click highlights to view comments</span>
            </div>
            {/* Render submission text with highlights for each anchored comment */}
            <div className="space-y-6 font-serif text-lg leading-relaxed text-navy/80">
              {result.criteria.flatMap(c => c.anchoredComments).length > 0 ? (
                <>
                  {result.criteria.map(criterion =>
                    criterion.anchoredComments.map(ac => (
                      <div key={ac.id} className="mb-4">
                        <Highlight id={ac.id} active={activeComment === ac.id} onToggle={setActiveComment}>
                          &quot;{ac.text}&quot;
                        </Highlight>
                        {activeComment === ac.id && (
                          <MotionItem className="mt-2 ml-4 p-3 bg-info/10 border border-info/30 rounded-lg text-sm text-info">
                            <span className="font-semibold">{criterion.name}:</span> {ac.comment}
                          </MotionItem>
                        )}
                      </div>
                    ))
                  )}
                </>
              ) : (
                <p className="italic text-muted">No anchored comments available.</p>
              )}
            </div>
          </div>
        </div>

        {/* Right Pane: Criteria Panels + Issue List */}
        <div className="w-full rounded-3xl border border-border bg-background-light p-6 shadow-sm md:w-1/2 md:overflow-y-auto md:p-8">
          <div className="mx-auto max-w-2xl space-y-6">
            <LearnerSurfaceSectionHeader
              eyebrow="Feedback breakdown"
              title="Score details and revision guidance"
              description="A quick summary first, then the detailed breakdown below."
            />
            {result.criteria.map((criterion, index) => (
              <MotionItem key={criterion.name} delayIndex={index}>
                <Card className="p-6 shadow-sm">
                  {/* Header with score */}
                  <div className="mb-5 flex items-start justify-between border-b border-border pb-5">
                    <div>
                      <h3 className="text-xl font-bold text-navy mb-1">{criterion.name}</h3>
                      <div className="text-sm text-muted">Criterion Score</div>
                    </div>
                    <Badge variant={criterion.score / criterion.maxScore >= 0.75 ? 'success' : criterion.score / criterion.maxScore >= 0.5 ? 'warning' : 'danger'}>
                      {criterion.score} / {criterion.maxScore}
                    </Badge>
                  </div>

                  {/* Explanation */}
                  <p className="mb-6 leading-relaxed text-navy/80">{criterion.explanation}</p>

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
                              className={`w-full cursor-pointer rounded-2xl border p-4 text-left transition-all ${isActive ? 'border-primary/20 bg-primary/5 shadow-sm' : 'border-border bg-background-light hover:border-border-hover'}`}>
                               <div className="mb-2 border-l-2 border-border pl-3 text-sm italic text-muted">&quot;{comment.text}&quot;</div>
                               <div className="text-sm font-medium text-navy">{comment.comment}</div>
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
                        <div className="rounded-2xl border border-danger/30 bg-danger/10 p-4">
                          <h4 className="text-xs font-bold text-danger uppercase tracking-wider mb-3 flex items-center gap-2"><MinusCircle className="w-4 h-4" /> Omissions</h4>
                          <ul className="space-y-2">{criterion.omissions.map((item, i) => (<li key={i} className="text-sm text-danger flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-danger mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                        </div>
                      )}
                      {criterion.unnecessaryDetails.length > 0 && (
                        <div className="rounded-2xl border border-warning/30 bg-warning/10 p-4">
                          <h4 className="text-xs font-bold text-warning uppercase tracking-wider mb-3 flex items-center gap-2"><XCircle className="w-4 h-4" /> Unnecessary</h4>
                          <ul className="space-y-2">{criterion.unnecessaryDetails.map((item, i) => (<li key={i} className="text-sm text-warning flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-warning mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Revision Suggestions */}
                  {criterion.revisionSuggestions.length > 0 && (
                    <div className="rounded-2xl border border-success/30 bg-success/10 p-4">
                      <h4 className="text-xs font-bold text-success uppercase tracking-wider mb-3 flex items-center gap-2"><Lightbulb className="w-4 h-4" /> Revision Suggestions</h4>
                      <ul className="space-y-3">{criterion.revisionSuggestions.map((item, i) => (<li key={i} className="text-sm text-success flex items-start gap-2"><ArrowRight className="w-4 h-4 text-success shrink-0 mt-0.5" /><span className="leading-snug">{item}</span></li>))}</ul>
                    </div>
                  )}
                </Card>
              </MotionItem>
            ))}

            {/* Aggregated Issues (WritingIssueList) */}
            {allIssues.length > 0 && (
              <div className="pt-4">
                <h3 className="mb-3 text-sm font-bold uppercase tracking-wider text-muted">All Issues Summary</h3>
                <WritingIssueList issues={allIssues} />
              </div>
            )}
          </div>
        </div>
      </main>
      </div>
    </AppShell>
  );
}
