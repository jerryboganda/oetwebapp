'use client';

import { useEffect, useMemo, useState } from 'react';
import { DollarSign } from 'lucide-react';

import { TutorRouteHero, TutorRouteSectionHeader, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { EarningsChart } from '@/components/tutor/EarningsChart';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/stat-card';
import { fetchTutorEarnings, type TutorEarnings } from '@/lib/api';

function formatUsd(v: number) {
  return `$${v.toFixed(2)}`;
}

function formatDate(iso: string) {
  return new Intl.DateTimeFormat('en-AU', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(iso));
}

export default function TutorEarningsPage() {
  const [data, setData] = useState<TutorEarnings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    // Last 6 months window for the chart.
    const to = new Date();
    const from = new Date(to.getFullYear(), to.getMonth() - 6, 1);
    fetchTutorEarnings(from.toISOString(), to.toISOString())
      .then((res) => {
        if (!cancelled) setData(res);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load earnings.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const perClass = useMemo(() => {
    const map = new Map<string, { title: string; sessions: number; gross: number; net: number }>();
    for (const line of data?.lines ?? []) {
      const existing = map.get(line.liveClassId);
      if (existing) {
        existing.sessions += 1;
        existing.gross += line.grossUsd;
        existing.net += line.netUsd;
      } else {
        map.set(line.liveClassId, {
          title: line.classTitle,
          sessions: 1,
          gross: line.grossUsd,
          net: line.netUsd,
        });
      }
    }
    return Array.from(map.entries()).map(([id, v]) => ({ id, ...v }));
  }, [data]);

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        title="Earnings"
        description="Net and gross USD earnings over the last 6 months, plus a per-class breakdown."
        icon={DollarSign}
      />

      {error ? (
        <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
          <span>{error}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}

      {loading ? (
        <div className="grid gap-3 sm:grid-cols-3">
          <Skeleton className="h-24 rounded-2xl" />
          <Skeleton className="h-24 rounded-2xl" />
          <Skeleton className="h-24 rounded-2xl" />
        </div>
      ) : data ? (
        <>
          <div className="grid gap-3 sm:grid-cols-3">
            <StatCard
              label="Gross (last 6mo)"
              value={formatUsd(data.grossUsd)}
              hint="Before platform share."
              tone="info"
            />
            <StatCard
              label="Net (last 6mo)"
              value={formatUsd(data.netUsd)}
              hint="After platform share."
              tone="success"
            />
            <StatCard
              label="Revenue share"
              value={`${data.revenueSharePercent.toFixed(0)}%`}
              hint="Your share of gross."
              tone="default"
            />
          </div>

          <section className="space-y-3">
            <TutorRouteSectionHeader
              eyebrow="Trend"
              title="Monthly breakdown"
              description="Gross vs net earnings over the last 6 months."
            />
            <EarningsChart lines={data.lines} />
          </section>

          <section className="space-y-3">
            <TutorRouteSectionHeader
              eyebrow="By class"
              title="Revenue by class"
              description="Sessions in the window grouped by class."
            />
            {perClass.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-border bg-surface p-8 text-center text-sm text-muted">
                No completed sessions in this window.
              </div>
            ) : (
              <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
                <table className="min-w-full divide-y divide-border text-sm">
                  <thead className="bg-background-light text-left">
                    <tr>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Class</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Sessions</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Gross</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Net</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {perClass.map((row) => (
                      <tr key={row.id} className="hover:bg-background-light">
                        <td className="px-4 py-3 font-medium text-navy">{row.title}</td>
                        <td className="px-4 py-3 text-muted">{row.sessions}</td>
                        <td className="px-4 py-3 text-muted">{formatUsd(row.gross)}</td>
                        <td className="px-4 py-3 text-navy">{formatUsd(row.net)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="space-y-3">
            <TutorRouteSectionHeader
              eyebrow="Pending settlement"
              title="Recent sessions"
              description="Each completed session that contributed to this window’s earnings."
            />
            {data.lines.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-border bg-surface p-8 text-center text-sm text-muted">
                No sessions in this window.
              </div>
            ) : (
              <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
                <table className="min-w-full divide-y divide-border text-sm">
                  <thead className="bg-background-light text-left">
                    <tr>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Date</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Class</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Attendees</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-navy">Net</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {data.lines.slice(0, 25).map((line) => (
                      <tr key={line.classSessionId} className="hover:bg-background-light">
                        <td className="px-4 py-3 text-muted">{formatDate(line.scheduledStartAt)}</td>
                        <td className="px-4 py-3 font-medium text-navy">{line.classTitle}</td>
                        <td className="px-4 py-3 text-muted">{line.attendedCount}</td>
                        <td className="px-4 py-3 text-navy">{formatUsd(line.netUsd)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      ) : null}
    </TutorRouteWorkspace>
  );
}
