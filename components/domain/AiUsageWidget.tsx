'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Cpu } from 'lucide-react';
import { fetchMyAiUsage, type AiUserPolicySnapshot } from '@/lib/ai-management-api';

/**
 * Small "AI credits remaining this month" widget intended for /dashboard.
 * Safe-fail: if the backend errors or the user has no plan, renders nothing.
 */
export function AiUsageWidget() {
  const [snap, setSnap] = useState<AiUserPolicySnapshot | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await fetchMyAiUsage();
        if (!cancelled) setSnap(data);
      } catch {
        if (!cancelled) setFailed(true);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  if (failed || !snap) return null;
  if (snap.monthlyTokenCap <= 0) return null;

  const pct = Math.min(100, Math.round((snap.tokensUsedThisMonth / snap.monthlyTokenCap) * 100));
  const remaining = Math.max(0, snap.monthlyTokenCap - snap.tokensUsedThisMonth);
  const barClass =
    pct > 85 ? 'bg-danger' : pct > 60 ? 'bg-warning' : 'bg-success';

  return (
    <Link
      href="/settings/ai"
      className="block rounded-2xl border border-border bg-surface p-4 transition-colors hover:border-border-hover hoverable:shadow-clinical"
    >
      <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-navy">
        <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
          <Cpu className="h-3.5 w-3.5" aria-hidden="true" />
        </span>
        <span className="min-w-0 truncate">AI credits · {snap.planName}</span>
      </div>
      <div className="mb-2 h-1.5 w-full overflow-hidden rounded-full bg-background-light">
        <div className={`h-full transition-[width,background-color] duration-300 ${barClass}`} style={{ width: `${pct}%` }} />
      </div>
      <div className="flex items-center justify-between text-xs text-muted">
        <span className="tabular-nums font-semibold text-navy">{remaining.toLocaleString()} tokens left</span>
        <span className="tabular-nums">{pct}% used</span>
      </div>
      {snap.killSwitchActive && (
        <div className="mt-2 text-xs font-semibold text-danger">Platform AI is temporarily paused by an administrator.</div>
      )}
    </Link>
  );
}
