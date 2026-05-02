'use client';

import Link from 'next/link';
import { Brain, ChevronRight, Lock, Volume2 } from 'lucide-react';
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card';

/**
 * Dashboard Recalls audio tile — mounted in the learner dashboard.
 *
 * Phase 2 consolidates the standalone pronunciation entry point into Recalls:
 * learners click words in their recall queue to hear the paid British audio.
 * Uses the canonical DESIGN.md Card primitive (rounded-2xl, border + shadow,
 * Surface White, Manrope, navy headings, lavender icon chip).
 */
export function PronunciationDashboardTile() {
  return (
    <Link
      href="/recalls/words"
      aria-label="Open Recalls audio practice"
      className="group block rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
    >
      <Card hoverable padding="md" className="h-full">
        <CardHeader className="mb-3 flex items-center justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-lavender text-primary">
              <Brain className="h-4.5 w-4.5" aria-hidden />
            </span>
            <CardTitle className="truncate">Recalls Audio</CardTitle>
          </div>
          <ChevronRight
            className="h-4 w-4 shrink-0 text-muted transition group-hover:translate-x-0.5 group-hover:text-primary"
            aria-hidden
          />
        </CardHeader>

        <CardContent className="space-y-2">
          <p className="text-sm text-muted">
            Open your recall queue and click any vocabulary word to hear British clinical pronunciation.
          </p>
          <div className="flex flex-wrap gap-1.5 pt-1 text-xs font-medium">
            <span className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-1 text-primary">
              <Volume2 className="h-3.5 w-3.5" /> Click-to-hear
            </span>
            <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-1 text-amber-800">
              <Lock className="h-3.5 w-3.5" /> Paid candidates
            </span>
          </div>
        </CardContent>
      </Card>
    </Link>
  );
}
