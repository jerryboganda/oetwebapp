'use client';

import Link from 'next/link';
import { ArrowUpRight, AlertCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { plainCategoryLabel } from '@/lib/writing/candidate-text';
import type { WritingCommonMistakeDto, WritingLearnerMistakeStatDto } from '@/lib/writing/types';

export interface MistakeCardProps {
  mistake: WritingCommonMistakeDto;
  personalStat?: WritingLearnerMistakeStatDto | null;
  className?: string;
}

function formatRelative(iso: string | null | undefined): string {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    const diff = (Date.now() - d.getTime()) / 1000;
    if (diff < 60) return 'just now';
    if (diff < 3600) return `${Math.round(diff / 60)}m ago`;
    if (diff < 86400) return `${Math.round(diff / 3600)}h ago`;
    if (diff < 86400 * 30) return `${Math.round(diff / 86400)}d ago`;
    return d.toLocaleDateString();
  } catch {
    return '';
  }
}

/**
 * Single common-mistake card. Used in:
 *   - /writing/common-mistakes (library; no `personalStat`)
 *   - /writing/common-mistakes/mine (personal stats highlighted)
 *
 * Provides a wrong/right example pair plus a deep-link to the guidance
 * for the rule the mistake breaks (if any), without showing the rule id.
 */
export function MistakeCard({ mistake, personalStat, className }: MistakeCardProps) {
  return (
    <Card padding="md" className={cn('h-full', className)} aria-label={`Common mistake: ${mistake.summary}`}>
      <header className="mb-2 flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-start gap-2">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-warning-strong" aria-hidden="true" />
          <h3 className="min-w-0 text-sm font-bold text-navy">{mistake.summary}</h3>
        </div>
        <Badge variant="muted" size="sm" className="shrink-0">{plainCategoryLabel(mistake.category)}</Badge>
      </header>

      <dl className="mt-2 grid grid-cols-1 gap-2 md:grid-cols-2">
        <div className="min-w-0 rounded-lg border border-danger/30 bg-danger/10 p-2">
          <dt className="tile-label mb-0.5 text-danger-strong">
            Wrong
          </dt>
          <dd className="text-xs leading-snug text-navy">{mistake.exampleWrong}</dd>
        </div>
        <div className="min-w-0 rounded-lg border border-success/30 bg-success/10 p-2">
          <dt className="tile-label mb-0.5 text-success-strong">
            Right
          </dt>
          <dd className="text-xs leading-snug text-navy">{mistake.exampleRight}</dd>
        </div>
      </dl>

      <footer className="mt-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-xs text-muted">
          {mistake.relatedSubSkill ? <Badge variant="violet" size="sm">{mistake.relatedSubSkill}</Badge> : null}
          {/* The link text is plain: the internal rule id is only part of the URL, never shown. */}
          {mistake.canonRuleId ? (
            <Link
              href={`/writing/canon/${encodeURIComponent(mistake.canonRuleId)}`}
              className="inline-flex items-center gap-1 rounded font-bold text-primary underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              Read the guidance
              <ArrowUpRight className="h-3 w-3 rtl:-scale-x-100" aria-hidden="true" />
            </Link>
          ) : null}
        </div>
        {personalStat ? (
          <div className="text-xs font-bold tabular-nums text-muted">
            {personalStat.occurrenceCount}× in your letters
            {personalStat.lastOccurredAt ? ` · last ${formatRelative(personalStat.lastOccurredAt)}` : ''}
          </div>
        ) : null}
      </footer>
    </Card>
  );
}
