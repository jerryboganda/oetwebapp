'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ChevronRight, PenLine, RotateCcw } from 'lucide-react';
import { getWritingMyWork } from '@/lib/writing/api';
import type { WritingMyWorkItemDto } from '@/lib/writing/types';
import { cn } from '@/lib/utils';

// Writing hub pointer to the newest letter that needs the learner: an active
// draft to resume, or a saved letter whose grading can be retried. It links to
// the server-computed action route (Retry happens on that letter's grading
// page). Renders nothing when there is nothing to resume or the lookup fails.

interface ResumeTarget {
  item: WritingMyWorkItemDto;
  href: string;
}

function findTarget(items: WritingMyWorkItemDto[]): ResumeTarget | null {
  for (const item of items) {
    const kind = item.state === 'draft' ? 'resume' : item.state === 'failed' && item.canRetry ? 'retry' : null;
    const action = kind ? item.actions.find((a) => a.kind === kind) : undefined;
    if (action) return { item, href: action.href };
  }
  return null;
}

/** Time left on the paused exam clock; the writing window still counts while reading. */
function secondsLeft(item: WritingMyWorkItemDto): number | null {
  if (item.phase === 'writing') return item.writingSecondsRemaining;
  if (item.phase === 'reading' && item.readingSecondsRemaining != null && item.writingSecondsRemaining != null) {
    return item.readingSecondsRemaining + item.writingSecondsRemaining;
  }
  return null;
}

function clock(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  return `${String(Math.floor(safe / 60)).padStart(2, '0')}:${String(safe % 60).padStart(2, '0')}`;
}

export function ResumeWritingBanner() {
  const t = useTranslations();
  const [target, setTarget] = useState<ResumeTarget | null>(null);

  useEffect(() => {
    let cancelled = false;
    getWritingMyWork()
      .then((page) => {
        if (!cancelled) setTarget(findTarget(page.items));
      })
      .catch(() => {
        // A bonus pointer: Past submissions shows the honest error state.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!target) return null;

  const { item, href } = target;
  const draft = item.state === 'draft';
  const left = draft ? secondsLeft(item) : null;
  const message = !draft
    ? t('writing.myWork.banner.failed')
    : left == null
      ? t('writing.myWork.banner.resumeNoTime', { count: item.wordCount })
      : t('writing.myWork.banner.resume', { count: item.wordCount, time: clock(left) });
  const Icon = draft ? PenLine : RotateCcw;

  return (
    <Link
      href={href}
      data-testid="resume-writing-banner"
      data-state={item.state}
      className={cn(
        'group flex w-full items-center gap-3 rounded-2xl border px-4 py-3.5 text-start shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
        draft ? 'border-primary/20 bg-lavender/40 hover:border-primary/30' : 'border-warning/30 bg-warning/10 hover:border-warning/50',
      )}
    >
      <span
        className={cn(
          'flex h-10 w-10 shrink-0 items-center justify-center rounded-xl',
          draft ? 'bg-lavender text-primary-dark' : 'bg-surface text-warning-strong',
        )}
        aria-hidden
      >
        <Icon className="h-5 w-5" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-bold text-navy">{message}</span>
        {/* Scenario titles are OET-authored English content. */}
        <span className="mt-0.5 block truncate text-xs text-muted" dir="auto">{item.title}</span>
      </span>
      <ChevronRight
        className="h-4 w-4 shrink-0 text-muted transition-transform group-hover:translate-x-0.5 rtl:rotate-180 rtl:group-hover:-translate-x-0.5"
        aria-hidden
      />
    </Link>
  );
}
