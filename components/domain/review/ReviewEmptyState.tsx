'use client';

import Link from 'next/link';
import { Brain, BookMarked, Layers, ArrowRight } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

/**
 * Calm empty-state surface for /review when the learner has no active cards.
 * Matches DESIGN.md §7 — "Show explicit empty states when data is missing".
 */
export function ReviewEmptyState() {
  return (
    <Card className="rounded-3xl border border-dashed border-border bg-surface px-6 py-10 text-center shadow-sm">
      <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-lavender/40 text-primary">
        <Brain className="h-7 w-7" />
      </div>
      <h3 className="mt-4 text-lg font-semibold text-navy">No review items yet</h3>
      <p className="mx-auto mt-2 max-w-md text-sm text-muted">
        Review items are created automatically every time you miss a reading or listening
        question, submit a writing or speaking evaluation, complete a grammar exercise,
        practise pronunciation, or save a vocabulary term. Start with one of the paths below.
      </p>

      <div className="mt-6 grid gap-3 sm:grid-cols-2">
        <Link
          href="/vocabulary"
          className="flex items-center justify-between rounded-2xl border border-border bg-background-light px-4 py-3 text-left transition-colors hover:border-primary"
        >
          <div className="flex items-center gap-3">
            <span className="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-lavender/60 text-primary">
              <Layers className="h-5 w-5" />
            </span>
            <div>
              <p className="text-sm font-semibold text-navy">Start with vocabulary</p>
              <p className="text-xs text-muted">Save clinical terms to your word bank.</p>
            </div>
          </div>
          <ArrowRight className="h-4 w-4 text-muted" />
        </Link>
        <Link
          href="/grammar"
          className="flex items-center justify-between rounded-2xl border border-border bg-background-light px-4 py-3 text-left transition-colors hover:border-primary"
        >
          <div className="flex items-center gap-3">
            <span className="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-indigo-50 text-indigo-700">
              <BookMarked className="h-5 w-5" />
            </span>
            <div>
              <p className="text-sm font-semibold text-navy">Complete a grammar lesson</p>
              <p className="text-xs text-muted">Every wrong answer becomes a review card.</p>
            </div>
          </div>
          <ArrowRight className="h-4 w-4 text-muted" />
        </Link>
      </div>

      <div className="mt-4 flex flex-wrap items-center justify-center gap-2 text-xs text-muted">
        <span>Or tackle a diagnostic task in</span>
        <Link href="/writing" className="font-semibold text-primary hover:underline">Writing</Link>
        <span>·</span>
        <Link href="/speaking" className="font-semibold text-primary hover:underline">Speaking</Link>
        <span>·</span>
        <Link href="/reading" className="font-semibold text-primary hover:underline">Reading</Link>
        <span>·</span>
        <Link href="/listening" className="font-semibold text-primary hover:underline">Listening</Link>
      </div>
    </Card>
  );
}

/**
 * Compact "nothing due today" state — used when there are cards in the bank
 * but the queue is quiet. Different to <ReviewEmptyState /> above.
 */
export function ReviewNothingDueCard({ totalItems }: { totalItems: number }) {
  return (
    <Card className="rounded-3xl border border-border bg-surface px-6 py-10 text-center shadow-sm">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-success/10 text-success">
        <Brain className="h-6 w-6" />
      </div>
      <h3 className="mt-3 text-lg font-semibold text-navy">You are caught up</h3>
      <p className="mx-auto mt-2 max-w-md text-sm text-muted">
        {totalItems} item{totalItems === 1 ? '' : 's'} are tracked in your bank. Come back tomorrow, or warm
        up a few upcoming items below.
      </p>
      <div className="mt-4 flex items-center justify-center">
        <Button variant="outline" onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}>
          Back to overview
        </Button>
      </div>
    </Card>
  );
}
