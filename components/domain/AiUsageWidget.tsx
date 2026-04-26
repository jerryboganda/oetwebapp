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
    pct > 85 ? 'bg-red-500' : pct > 60 ? 'bg-amber-500' : 'bg-emerald-500';

  return (
    <Link
      href="/settings/ai"
      className="block rounded-[20px] border border-border bg-surface p-4 hover:border-primary transition-colors"
    >
      <div className="flex items-center gap-2 text-sm font-medium text-navy mb-2">
        <Cpu className="w-4 h-4" /> AI credits · {snap.planName}
      </div>
      <div className="w-full h-1.5 bg-background-light rounded-full overflow-hidden mb-2">
        <div className={`h-full transition-all ${barClass}`} style={{ width: `${pct}%` }} />
      </div>
      <div className="flex items-center justify-between text-xs text-muted">
        <span>{remaining.toLocaleString()} tokens left</span>
        <span>{pct}% used</span>
      </div>
      {snap.killSwitchActive && (
        <div className="mt-2 text-xs text-red-600">Platform AI is temporarily paused by an administrator.</div>
      )}
    </Link>
  );
}
