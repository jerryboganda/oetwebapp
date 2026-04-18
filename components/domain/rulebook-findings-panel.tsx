'use client';

import Link from 'next/link';
import { useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, FileWarning, Info, ShieldAlert } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { cn } from '@/lib/utils';
import type { LintFinding, RuleSeverity } from '@/lib/rulebook';

type FilterMode = 'all' | 'critical';

export interface RulebookFindingsPanelProps {
  title: string;
  subtitle: string;
  findings: LintFinding[];
  className?: string;
  inactiveMessage?: string;
  ruleHref?: (ruleId: string) => string;
}

const severityBadge: Record<RuleSeverity, { label: string; variant: 'danger' | 'warning' | 'success' | 'muted'; icon: typeof ShieldAlert }> = {
  critical: { label: 'Critical', variant: 'danger', icon: ShieldAlert },
  major: { label: 'Major', variant: 'warning', icon: AlertTriangle },
  minor: { label: 'Minor', variant: 'muted', icon: FileWarning },
  info: { label: 'Info', variant: 'muted', icon: Info },
};

export function RulebookFindingsPanel({
  title,
  subtitle,
  findings,
  className,
  inactiveMessage,
  ruleHref,
}: RulebookFindingsPanelProps) {
  const [filter, setFilter] = useState<FilterMode>('all');

  const counts = useMemo(() => ({
    critical: findings.filter((f) => f.severity === 'critical').length,
    major: findings.filter((f) => f.severity === 'major').length,
    minor: findings.filter((f) => f.severity === 'minor').length,
    info: findings.filter((f) => f.severity === 'info').length,
  }), [findings]);

  const visible = useMemo(
    () => (filter === 'critical' ? findings.filter((f) => f.severity === 'critical') : findings),
    [filter, findings],
  );

  return (
    <Card className={cn('border-gray-200 bg-surface p-4', className)}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex items-center gap-2">
            <ShieldAlert className="h-4 w-4 text-primary" />
            <h3 className="text-sm font-black uppercase tracking-widest text-navy">{title}</h3>
          </div>
          <p className="mt-2 text-sm leading-6 text-muted">{subtitle}</p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant={filter === 'all' ? 'primary' : 'outline'}
            onClick={() => setFilter('all')}
          >
            All ({findings.length})
          </Button>
          <Button
            type="button"
            size="sm"
            variant={filter === 'critical' ? 'primary' : 'outline'}
            onClick={() => setFilter('critical')}
          >
            Critical ({counts.critical})
          </Button>
        </div>
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <Badge variant={counts.critical > 0 ? 'danger' : 'success'} size="sm">Critical {counts.critical}</Badge>
        <Badge variant={counts.major > 0 ? 'warning' : 'muted'} size="sm">Major {counts.major}</Badge>
        <Badge variant="muted" size="sm">Minor {counts.minor}</Badge>
        <Badge variant="muted" size="sm">Info {counts.info}</Badge>
      </div>

      {inactiveMessage ? (
        <div className="mt-4">
          <InlineAlert variant="info">{inactiveMessage}</InlineAlert>
        </div>
      ) : findings.length === 0 ? (
        <div className="mt-4 rounded-2xl border border-emerald-200 bg-emerald-50/80 p-4">
          <div className="flex items-start gap-3">
            <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
            <div>
              <p className="text-sm font-bold text-emerald-900">No rulebook issues detected</p>
              <p className="mt-1 text-sm leading-6 text-emerald-800">
                This draft currently respects the active rules that the engine can check automatically.
              </p>
            </div>
          </div>
        </div>
      ) : (
        <div className="mt-4 max-h-[24rem] space-y-3 overflow-auto pr-1">
          {visible.map((finding) => {
            const severity = severityBadge[finding.severity];
            const Icon = severity.icon;

            const ruleLabel = ruleHref ? (
              <Link
                href={ruleHref(finding.ruleId)}
                className="text-xs font-black uppercase tracking-widest text-primary underline-offset-4 hover:underline"
              >
                {finding.ruleId}
              </Link>
            ) : (
              <span className="text-xs font-black uppercase tracking-widest text-primary">{finding.ruleId}</span>
            );

            return (
              <div key={`${finding.ruleId}-${finding.message}-${finding.quote ?? ''}`} className="rounded-2xl border border-gray-100 bg-background-light p-4">
                <div className="flex items-start gap-3">
                  <Icon className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      {ruleLabel}
                      <Badge variant={severity.variant} size="sm">{severity.label}</Badge>
                    </div>
                    <p className="mt-2 text-sm font-medium leading-6 text-navy">{finding.message}</p>
                    {finding.quote ? (
                      <div className="mt-3 rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm text-muted">
                        “{finding.quote}”
                      </div>
                    ) : null}
                    {finding.fixSuggestion ? (
                      <p className="mt-3 text-sm leading-6 text-muted">
                        <span className="font-bold text-navy">Suggested fix:</span> {finding.fixSuggestion}
                      </p>
                    ) : null}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </Card>
  );
}
