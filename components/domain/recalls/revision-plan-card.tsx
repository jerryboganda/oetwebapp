'use client';

import { useEffect, useState } from 'react';
import { Sparkles, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchRecallsRevisionPlan, type RecallsRevisionPlanResponse } from '@/lib/api';

/**
 * AI personal revision plan card (spec §12). Hits `/v1/recalls/revision-plan`
 * which routes through `IAiGatewayService.BuildGroundedPrompt` with feature
 * code `RecallsRevisionPlan`. Always renders the deterministic step list;
 * the AI narrative is enhancement-only and may be absent.
 */
export function RevisionPlanCard() {
  const [plan, setPlan] = useState<RecallsRevisionPlanResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchRecallsRevisionPlan()
      .then((p) => {
        if (!cancelled) setPlan(p);
      })
      .catch(() => {
        if (!cancelled) setError('Could not load revision plan.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setPlan(await fetchRecallsRevisionPlan());
    } catch {
      setError('Could not refresh plan.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card padding="md">
      <div className="flex items-center gap-2">
        <Sparkles className="h-4 w-4 text-primary" />
        <h3 className="text-sm font-semibold text-navy">Today’s revision plan</h3>
      </div>

      {loading && !plan ? (
        <Skeleton className="mt-3 h-24 rounded-xl" />
      ) : error ? (
        <div className="mt-3 text-xs text-warning">{error}</div>
      ) : plan ? (
        <div className="mt-3 space-y-3 text-sm">
          <p className="font-medium text-navy">{plan.headline}</p>
          <ul className="space-y-1 text-muted">
            {plan.steps.map((s, i) => (
              <li key={i}>• {s}</li>
            ))}
          </ul>
          {plan.aiNarrative && (
            <div className="rounded-lg border border-info/30 bg-info/5 p-3 text-xs text-navy">
              {plan.aiNarrative}
            </div>
          )}
          <div>
            <Button onClick={refresh} disabled={loading} variant="primary" className="text-xs">
              {loading ? <Loader2 className="h-3 w-3 animate-spin" /> : 'Refresh'}
            </Button>
          </div>
        </div>
      ) : null}
    </Card>
  );
}
