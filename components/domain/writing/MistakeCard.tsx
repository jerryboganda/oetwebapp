'use client';

import Link from 'next/link';
import { ArrowUpRight, AlertCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
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
 * Provides a wrong/right example pair plus a deep-link to the canon
 * rule the mistake violates (if any).
 */
export function MistakeCard({ mistake, personalStat, className }: MistakeCardProps) {
  return (
    <Card padding="md" className={cn(className)} aria-label={`Common mistake: ${mistake.summary}`}>
      <CardContent>
        <header className="flex items-start justify-between gap-2 mb-2">
          <div className="flex items-center gap-2 min-w-0">
            <AlertCircle className="w-4 h-4 text-amber-600 shrink-0" aria-hidden="true" />
            <h3 className="font-extrabold text-sm truncate">{mistake.summary}</h3>
          </div>
          <Badge variant="muted" size="sm">{mistake.category}</Badge>
        </header>

        <dl className="grid grid-cols-1 md:grid-cols-2 gap-2 mt-2">
          <div className="rounded border border-red-200/60 bg-red-50/60 dark:bg-red-950/40 p-2">
            <dt className="text-[10px] uppercase tracking-wider font-bold text-red-700 dark:text-red-300 mb-0.5">
              Wrong
            </dt>
            <dd className="text-xs leading-snug">{mistake.exampleWrong}</dd>
          </div>
          <div className="rounded border border-emerald-200/60 bg-emerald-50/60 dark:bg-emerald-950/40 p-2">
            <dt className="text-[10px] uppercase tracking-wider font-bold text-emerald-700 dark:text-emerald-300 mb-0.5">
              Right
            </dt>
            <dd className="text-xs leading-snug">{mistake.exampleRight}</dd>
          </div>
        </dl>

        <footer className="mt-3 flex items-center justify-between gap-2 flex-wrap">
          <div className="flex items-center gap-2 text-xs text-muted">
            {mistake.relatedSubSkill ? <Badge variant="violet" size="sm">{mistake.relatedSubSkill}</Badge> : null}
            {mistake.canonRuleId ? (
              <Link
                href={`/writing/canon/${encodeURIComponent(mistake.canonRuleId)}`}
                className="inline-flex items-center gap-1 font-bold underline text-primary"
              >
                {mistake.canonRuleId}
                <ArrowUpRight className="w-3 h-3" aria-hidden="true" />
              </Link>
            ) : null}
          </div>
          {personalStat ? (
            <div className="text-xs text-muted font-bold">
              {personalStat.occurrenceCount}× in your letters
              {personalStat.lastOccurredAt ? ` · last ${formatRelative(personalStat.lastOccurredAt)}` : ''}
            </div>
          ) : null}
        </footer>
      </CardContent>
    </Card>
  );
}
