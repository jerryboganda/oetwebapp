'use client';

import { useState } from 'react';
import { AlertOctagon, AlertTriangle, Info, Flag } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { cleanCandidateText, severityLabel } from '@/lib/writing/candidate-text';
import type { WritingCanonViolationDto, WritingSeverity } from '@/lib/writing/types';

export interface CanonViolationCardProps {
  violation: WritingCanonViolationDto;
  onDispute?: (ruleId: string, violationId: string) => void | Promise<void>;
  className?: string;
}

// The stored severities are high / medium / low; the candidate sees Critical / Major / Minor.
const SEVERITY_META: Record<WritingSeverity, { icon: typeof AlertOctagon; tone: string; ring: string }> = {
  high: {
    icon: AlertOctagon,
    tone: 'text-danger-strong',
    ring: 'border-danger/30 bg-danger/10',
  },
  medium: {
    icon: AlertTriangle,
    tone: 'text-warning-strong',
    ring: 'border-warning/30 bg-warning/10',
  },
  low: {
    icon: Info,
    tone: 'text-info',
    ring: 'border-info/30 bg-info/10',
  },
};

/**
 * Single legacy style-check card. Used in:
 *   - Submission results page (older results that have no v1.1 corrections)
 *   - Mock results page (list of violations)
 *
 * The rule id never reaches the candidate: the card shows the severity, the
 * plain-English explanation, the flagged wording and the suggested fix. The
 * "Mark as incorrect" button surfaces a one-tap dispute action (spec §13.8);
 * the id is only passed back to the parent for the API call, never rendered.
 * Disputed state is reflected in the UI immediately for responsive feel —
 * parent is responsible for actual mutation.
 */
export function CanonViolationCard({ violation, onDispute, className }: CanonViolationCardProps) {
  const [optimisticDisputed, setOptimisticDisputed] = useState(false);
  const meta = SEVERITY_META[violation.severity] ?? SEVERITY_META.low;
  const Icon = meta.icon;
  const isDisputed = violation.disputed || optimisticDisputed;
  const label = severityLabel(violation.severity);
  const explanation = cleanCandidateText(violation.ruleText);
  const suggestedFix = cleanCandidateText(violation.suggestedFix);

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
      aria-label={`${label} correction`}
    >
      <CardContent>
        <div className="flex items-start gap-3">
          <Icon className={cn('w-5 h-5 mt-0.5 shrink-0', meta.tone)} aria-hidden="true" />
          <div className="flex-1 min-w-0">
            <div className="flex items-center justify-between gap-2 mb-1">
              <span className={cn('text-sm font-bold', meta.tone)}>{label}</span>
            </div>
            {explanation ? <p className="text-sm text-navy dark:text-white leading-snug">{explanation}</p> : null}
            <div className="mt-2 rounded px-3 py-1 text-xs italic text-muted bg-background-light dark:bg-navy/20">
              Line {violation.lineNumber}: &ldquo;{violation.snippet}&rdquo;
            </div>
            {suggestedFix ? (
              <div className="mt-2 text-xs">
                <span className="font-bold">Suggested fix:</span>{' '}
                <span>{suggestedFix}</span>
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
