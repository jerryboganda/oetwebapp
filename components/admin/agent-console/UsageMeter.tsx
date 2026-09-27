'use client';

import { Coins } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { UsageTotals } from '@/lib/owner-agent/event-reducer';
import type { SessionUsage } from '@/lib/owner-agent/types';

const numberFormat = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

function formatTokens(value: number): string {
  return numberFormat.format(Math.max(0, value || 0));
}

function formatCost(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return 'n/a';
  return `$${value.toFixed(value < 1 ? 4 : 2)}`;
}

export interface UsageMeterProps {
  /** Totals folded from this session's `usage` events (replayed from seq 1). */
  live?: UsageTotals | null;
  /** Server summary (SessionSummary.usage) used until events arrive. */
  summary?: SessionUsage | null;
  className?: string;
}

/**
 * Tokens + engine-reported cost (`total_cost_usd`). On subscription plans the
 * reported cost is informational — it shows exposure if billing ever moves to
 * a metered pool.
 */
export function UsageMeter({ live, summary, className }: UsageMeterProps) {
  const hasLive = Boolean(live && (live.inputTokens > 0 || live.outputTokens > 0 || live.costUsd !== null));
  const input = hasLive ? live!.inputTokens : summary?.inputTokens ?? 0;
  const output = hasLive ? live!.outputTokens : summary?.outputTokens ?? 0;
  const cacheRead = hasLive ? live!.cacheReadTokens : 0;
  const cost = hasLive ? live!.costUsd : summary?.costUsd ?? null;
  const models = hasLive ? Object.entries(live!.byModel) : [];

  return (
    <div className={cn('inline-flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-admin-fg-muted', className)} data-testid="usage-meter">
      <Coins className="h-3.5 w-3.5" aria-hidden="true" />
      <span>
        in <strong className="text-admin-fg-default">{formatTokens(input)}</strong>
      </span>
      <span>
        out <strong className="text-admin-fg-default">{formatTokens(output)}</strong>
      </span>
      {cacheRead > 0 ? (
        <span>
          cache <strong className="text-admin-fg-default">{formatTokens(cacheRead)}</strong>
        </span>
      ) : null}
      <span title="Engine-reported cost (total_cost_usd). Subscription usage is billed by plan, not per token.">
        reported cost <strong className="text-admin-fg-default">{formatCost(cost)}</strong>
      </span>
      {models.length > 1 ? (
        <span className="w-full">
          {models.map(([model, bucket]) => (
            <span key={model} className="mr-3 font-mono">
              {model}: {formatTokens(bucket.inputTokens + bucket.outputTokens)} tok · {formatCost(bucket.costUsd)}
            </span>
          ))}
        </span>
      ) : null}
    </div>
  );
}
