'use client';

import { Badge } from '@/components/ui/badge';

/**
 * 2026-05-27 audit fix — at-a-glance compliance dashboard for the rulebook
 * surface. Renders one chip per rulebook with a green / amber / red status:
 *
 *   green  — rulebook exists, every CRITICAL/MAJOR rule has a deterministic
 *            enforcer OR an explicit enforcement marker.
 *   amber  — rulebook exists, but at least one rule is AI-grounded /
 *            human-review-only (acceptable but worth surfacing).
 *   red    — rulebook missing entirely, or contains rules that are silently
 *            unenforced (no checkId, no enforcement marker).
 *
 * This component is rendered by the [RulebookCompliance.stories.tsx]
 * Storybook story as an exec dashboard, and can also be embedded into the
 * admin shell directly.
 */

export type ComplianceStatus = 'green' | 'amber' | 'red';

export interface RulebookComplianceChip {
  kind: string;
  profession: string;
  status: ComplianceStatus;
  totalRules: number;
  criticalRules: number;
  unenforcedCount: number;
  aiGroundedCount: number;
  humanReviewCount: number;
  note?: string;
}

export interface RulebookComplianceChipsProps {
  chips: RulebookComplianceChip[];
  title?: string;
}

const STATUS_LABEL: Record<ComplianceStatus, string> = {
  green: 'Compliant',
  amber: 'AI / human review',
  red: 'Gap',
};

const STATUS_VARIANT: Record<ComplianceStatus, 'success' | 'warning' | 'danger'> = {
  green: 'success',
  amber: 'warning',
  red: 'danger',
};

export function RulebookComplianceChips({ chips, title = 'Rulebook compliance' }: RulebookComplianceChipsProps) {
  const order: ComplianceStatus[] = ['red', 'amber', 'green'];
  const sorted = [...chips].sort((a, b) => {
    const pa = order.indexOf(a.status);
    const pb = order.indexOf(b.status);
    if (pa !== pb) return pa - pb;
    if (a.kind === b.kind) return a.profession.localeCompare(b.profession);
    return a.kind.localeCompare(b.kind);
  });
  const totals = sorted.reduce(
    (acc, c) => {
      acc[c.status]++;
      return acc;
    },
    { green: 0, amber: 0, red: 0 } as Record<ComplianceStatus, number>,
  );
  return (
    <section className="rounded-2xl border border-border bg-surface p-4 space-y-3" data-testid="rulebook-compliance-chips">
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <h3 className="text-sm font-black uppercase tracking-widest text-navy">{title}</h3>
        <div className="flex items-center gap-2 text-xs">
          <Badge variant="success" size="sm">{totals.green} green</Badge>
          <Badge variant="warning" size="sm">{totals.amber} amber</Badge>
          <Badge variant="danger" size="sm">{totals.red} red</Badge>
        </div>
      </div>
      <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {sorted.map((chip) => (
          <li
            key={`${chip.kind}:${chip.profession}`}
            className="rounded-xl border border-border bg-background-light p-3 text-xs"
            data-status={chip.status}
          >
            <div className="flex items-center justify-between gap-2 mb-1.5">
              <span className="font-bold text-navy">
                {chip.kind} <span className="text-muted">/</span> {chip.profession}
              </span>
              <Badge variant={STATUS_VARIANT[chip.status]} size="sm">{STATUS_LABEL[chip.status]}</Badge>
            </div>
            <p className="text-[11px] text-muted">
              {chip.totalRules} rules · {chip.criticalRules} critical · {chip.unenforcedCount} unenforced ·{' '}
              {chip.aiGroundedCount} AI-grounded · {chip.humanReviewCount} human-review
            </p>
            {chip.note ? <p className="mt-1 text-[11px] text-warning">{chip.note}</p> : null}
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * Compute a chip status from a (rules, gaps) summary. The function is pure
 * so a Storybook story can pass either fixture data or live registry data.
 */
export function computeChipStatus(args: { unenforcedCount: number; aiGroundedCount: number; humanReviewCount: number }): ComplianceStatus {
  if (args.unenforcedCount > 0) return 'red';
  if (args.aiGroundedCount > 0 || args.humanReviewCount > 0) return 'amber';
  return 'green';
}
