'use client';

import { useState } from 'react';
import Link from 'next/link';
import { AlertOctagon, AlertTriangle, Info, ArrowUpRight, Flag } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import type { WritingCanonViolationDto, WritingSeverity } from '@/lib/writing/types';

export interface CanonViolationCardProps {
  violation: WritingCanonViolationDto;
  onDispute?: (ruleId: string, violationId: string) => void | Promise<void>;
  className?: string;
}

const SEVERITY_META: Record<WritingSeverity, { label: string; icon: typeof AlertOctagon; tone: string; ring: string }> = {
  high: {
    label: 'High severity',
    icon: AlertOctagon,
    tone: 'text-danger',
    ring: 'border-danger/30 bg-danger/10',
  },
  medium: {
    label: 'Medium severity',
    icon: AlertTriangle,
    tone: 'text-warning',
    ring: 'border-warning/30 bg-warning/10',
  },
  low: {
    label: 'Low severity',
    icon: Info,
    tone: 'text-info',
    ring: 'border-info/30 bg-info/10',
  },
};

/**
 * Single canon violation card. Used in:
 *   - Submission results page (list of violations)
 *   - Revision editor (inline below the editor)
 *   - Canon library "your violations" tab
 *
 * Each card deep-links to /writing/canon/{ruleId} so the learner can
 * read the full rule + examples. The "Mark as incorrect" button
 * surfaces a one-tap dispute action (spec §13.8). Disputed state is
 * reflected in the UI immediately for responsive feel — parent is
 * responsible for actual mutation.
 */
export function CanonViolationCard({ violation, onDispute, className }: CanonViolationCardProps) {
  const [optimisticDisputed, setOptimisticDisputed] = useState(false);
  const meta = SEVERITY_META[violation.severity];
  const Icon = meta.icon;
  const isDisputed = violation.disputed || optimisticDisputed;

  const handleDispute = async () => {
    if (isDisputed) return;
    setOptimisticDisputed(true);
    try {
      await onDispute?.(violation.ruleId, violation.id);
    } catch {
      setOptimisticDisputed(false);
    }
  };

  return (
    <Card
      padding="md"
      className={cn('border', meta.ring, className)}
      role="article"
      aria-label={`Canon violation ${violation.ruleId}`}
    >
      <CardContent>
        <div className="flex items-start gap-3">
          <Icon className={cn('w-5 h-5 mt-0.5 shrink-0', meta.tone)} aria-hidden="true" />
          <div className="flex-1 min-w-0">
            <div className="flex items-center justify-between gap-2 mb-1">
              <Link
                href={`/writing/canon/${encodeURIComponent(violation.ruleId)}`}
                className="font-bold text-sm hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded inline-flex items-center gap-1"
              >
                {violation.ruleId}
                <ArrowUpRight className="w-3 h-3" aria-hidden="true" />
              </Link>
              <span className={cn('text-[10px] uppercase tracking-wider font-bold', meta.tone)}>
                {meta.label}
              </span>
            </div>
            <p className="text-sm text-navy dark:text-white leading-snug">{violation.ruleText}</p>
            <div className="mt-2 rounded px-3 py-1 text-xs italic text-muted bg-background-light dark:bg-navy/20">
              Line {violation.lineNumber}: &ldquo;{violation.snippet}&rdquo;
            </div>
            {violation.suggestedFix ? (
              <div className="mt-2 text-xs">
                <span className="font-bold">Suggested fix:</span>{' '}
                <span>{violation.suggestedFix}</span>
              </div>
            ) : null}
            <div className="mt-3 flex items-center justify-end">
              {isDisputed ? (
                <span className="text-xs font-bold text-muted inline-flex items-center gap-1">
                  <Flag className="w-3 h-3" aria-hidden="true" /> Flagged for review
                </span>
              ) : (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={handleDispute}
                  aria-label="Mark this detection as incorrect"
                >
                  <Flag className="w-3.5 h-3.5 mr-1" aria-hidden="true" />
                  Mark this detection as incorrect
                </Button>
              )}
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
