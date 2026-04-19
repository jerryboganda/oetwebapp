'use client';

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  FileText,
  Headphones,
  PenTool,
  Mic,
  MessageSquare,
  GitCompare,
  Send,
  Clock,
  CheckCircle2,
  AlertCircle,
  MoreVertical,
  EyeOff,
  Eye,
  Check,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { Submission, SubTest, ReviewStatus } from '@/lib/mock-data';
import { ScoreWithPassBadge } from './ScoreWithPassBadge';

const SUBTEST_STYLE: Record<SubTest, { icon: React.ElementType; badge: string }> = {
  Reading:   { icon: FileText,   badge: 'bg-blue-100 text-blue-700' },
  Listening: { icon: Headphones, badge: 'bg-indigo-100 text-indigo-700' },
  Writing:   { icon: PenTool,    badge: 'bg-rose-100 text-rose-700' },
  Speaking:  { icon: Mic,        badge: 'bg-purple-100 text-purple-700' },
};

const CONTEXT_LABEL: Record<string, { label: string; className: string }> = {
  practice:   { label: 'Practice',   className: 'bg-gray-100 text-gray-700' },
  mock:       { label: 'Mock',       className: 'bg-orange-100 text-orange-700' },
  diagnostic: { label: 'Diagnostic', className: 'bg-teal-100 text-teal-700' },
  revision:   { label: 'Revision',   className: 'bg-violet-100 text-violet-700' },
};

function formatAttemptDate(value: string): string {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  const includesTime = /T\d{2}:\d{2}/.test(value);
  return new Intl.DateTimeFormat(undefined, includesTime
    ? { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }
    : { year: 'numeric', month: 'short', day: 'numeric' }).format(parsed);
}

function ReviewBadge({ status }: { status: ReviewStatus }) {
  if (status === 'reviewed') return <Badge variant="success" size="sm"><CheckCircle2 className="w-3.5 h-3.5 mr-1" />Reviewed</Badge>;
  if (status === 'pending')  return <Badge variant="warning" size="sm"><Clock className="w-3.5 h-3.5 mr-1" />Pending</Badge>;
  return <Badge variant="muted" size="sm"><AlertCircle className="w-3.5 h-3.5 mr-1" />Not Requested</Badge>;
}

export interface SubmissionCardProps {
  submission: Submission;
  /** When true, the card enters compare-pick mode. */
  compareMode?: boolean;
  isComparePicked?: boolean;
  onTogglePick?: (id: string) => void;
  onHide?: (id: string) => void;
  onUnhide?: (id: string) => void;
  delayIndex?: number;
}

export function SubmissionCard({
  submission,
  compareMode,
  isComparePicked,
  onTogglePick,
  onHide,
  onUnhide,
  delayIndex = 0,
}: SubmissionCardProps) {
  const router = useRouter();
  const [menuOpen, setMenuOpen] = useState(false);
  const meta = SUBTEST_STYLE[submission.subTest] ?? SUBTEST_STYLE.Writing;
  const Icon = meta.icon;
  const context = submission.context ? CONTEXT_LABEL[submission.context.toLowerCase()] : undefined;
  const isRevision = (submission.revisionDepth ?? 0) > 0;
  const isEvaluating = submission.state === 'evaluating';
  const isFailed = submission.state === 'failed';

  function handleClickRow() {
    if (compareMode) {
      onTogglePick?.(submission.id);
      return;
    }
    if (submission.actions.reopenFeedbackRoute) {
      router.push(submission.actions.reopenFeedbackRoute);
    }
  }

  return (
    <div
      role={compareMode ? 'button' : undefined}
      tabIndex={compareMode ? 0 : undefined}
      aria-pressed={compareMode ? isComparePicked : undefined}
      onClick={compareMode ? handleClickRow : undefined}
      className={cn(
        'bg-surface rounded-[24px] border p-5 sm:p-6 shadow-sm transition-colors',
        'flex flex-col md:flex-row gap-6 justify-between',
        isComparePicked
          ? 'border-primary ring-2 ring-primary/25'
          : submission.isHidden
            ? 'border-dashed border-gray-300 opacity-70'
            : 'border-gray-200 hover:border-gray-300',
      )}
      style={{ animationDelay: `${delayIndex * 60}ms` }}
    >
      <div className="flex-1 space-y-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className={cn('inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-black uppercase tracking-widest', meta.badge)}>
            <Icon className="w-4 h-4" />
            {submission.subTest}
          </span>
          {context ? (
            <span className={cn('inline-flex items-center px-2 py-1 rounded-md text-[10px] font-black uppercase tracking-widest', context.className)}>
              {context.label}
            </span>
          ) : null}
          {isRevision ? (
            <Badge variant="info" size="sm">R{submission.revisionDepth}</Badge>
          ) : null}
          {submission.isHidden ? (
            <Badge variant="muted" size="sm"><EyeOff className="w-3 h-3 mr-1" />Hidden</Badge>
          ) : null}
          {isEvaluating ? (
            <Badge variant="warning" size="sm"><Clock className="w-3 h-3 mr-1" />Evaluating</Badge>
          ) : null}
          {isFailed ? (
            <Badge variant="danger" size="sm">Evaluation failed</Badge>
          ) : null}
          <span className="text-sm text-muted font-medium">{formatAttemptDate(submission.attemptDate)}</span>
        </div>
        <h2 className="text-lg font-bold text-navy leading-tight">{submission.taskName}</h2>

        <div className="flex items-center gap-3 pt-2 border-t border-gray-50 flex-wrap">
          <span className="text-sm font-medium text-muted">Review Status:</span>
          <ReviewBadge status={submission.reviewStatus} />
          <ScoreWithPassBadge
            scaledScore={submission.scaledScore ?? null}
            scoreLabel={submission.scoreEstimate}
            passState={submission.passState}
            passLabel={submission.passLabel}
            grade={submission.grade}
            density="compact"
            className="ml-auto items-end"
          />
        </div>
      </div>

      <div className="flex flex-col gap-2 md:w-52 shrink-0 border-t md:border-t-0 md:border-l border-gray-100 pt-4 md:pt-0 md:pl-6 justify-center">
        {compareMode ? (
          <Button
            variant={isComparePicked ? 'primary' : 'outline'}
            fullWidth
            onClick={(e) => { e.stopPropagation(); onTogglePick?.(submission.id); }}
          >
            {isComparePicked ? <Check className="w-4 h-4" /> : <GitCompare className="w-4 h-4" />}
            {isComparePicked ? 'Selected' : 'Select to compare'}
          </Button>
        ) : (
          <>
            <Button
              variant="outline"
              fullWidth
              onClick={() => submission.actions.reopenFeedbackRoute && router.push(submission.actions.reopenFeedbackRoute)}
              disabled={!submission.actions.reopenFeedbackRoute}
            >
              <MessageSquare className="w-4 h-4" />
              Reopen Feedback
            </Button>
            <Button
              variant="outline"
              fullWidth
              onClick={() => submission.actions.compareRoute && router.push(submission.actions.compareRoute)}
              disabled={!submission.actions.compareRoute}
            >
              <GitCompare className="w-4 h-4" />
              Compare Attempts
            </Button>
            <Button
              variant="primary"
              fullWidth
              onClick={() => submission.actions.requestReviewRoute && router.push(submission.actions.requestReviewRoute)}
              disabled={!submission.canRequestReview || !submission.actions.requestReviewRoute}
            >
              <Send className="w-4 h-4" />
              Request Review
            </Button>
            <div className="relative self-end">
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); setMenuOpen((v) => !v); }}
                aria-label="More actions"
                aria-expanded={menuOpen}
                className="p-2 rounded-full hover:bg-gray-100"
              >
                <MoreVertical className="w-4 h-4 text-muted" />
              </button>
              {menuOpen ? (
                <div
                  role="menu"
                  className="absolute right-0 top-full mt-1 z-10 min-w-[180px] rounded-xl border border-gray-200 bg-white shadow-lg py-1 text-sm"
                >
                  {submission.isHidden ? (
                    <button
                      role="menuitem"
                      type="button"
                      onClick={(e) => { e.stopPropagation(); setMenuOpen(false); onUnhide?.(submission.id); }}
                      className="w-full text-left px-3 py-2 hover:bg-gray-50 flex items-center gap-2"
                    >
                      <Eye className="w-4 h-4" /> Restore to History
                    </button>
                  ) : (
                    <button
                      role="menuitem"
                      type="button"
                      onClick={(e) => { e.stopPropagation(); setMenuOpen(false); onHide?.(submission.id); }}
                      className="w-full text-left px-3 py-2 hover:bg-gray-50 flex items-center gap-2"
                    >
                      <EyeOff className="w-4 h-4" /> Hide from History
                    </button>
                  )}
                </div>
              ) : null}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
