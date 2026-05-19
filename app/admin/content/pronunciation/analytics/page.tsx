'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, BarChart3, RotateCcw } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  adminFetchPronunciationAnalytics,
  type AdminPronunciationAnalytics,
} from '@/lib/api';

type ToastState = { variant: 'error'; message: string } | null;

const WINDOW_OPTIONS = [
  { value: '7', label: 'Last 7 days' },
  { value: '30', label: 'Last 30 days' },
  { value: '90', label: 'Last 90 days' },
];

function tbdPlaceholder(): AdminPronunciationAnalytics {
  return {
    totalAttempts: 0,
    averageScore: null,
    topPhonemes: [],
    weakestPhonemes: [],
    source: 'tbd',
  };
}

export default function PronunciationAnalyticsPage() {
  const [data, setData] = useState<AdminPronunciationAnalytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [windowDays, setWindowDays] = useState('30');
  const [toast, setToast] = useState<ToastState>(null);
  const [missingEndpoint, setMissingEndpoint] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setMissingEndpoint(false);
    try {
      const res = await adminFetchPronunciationAnalytics({ windowDays: Number(windowDays) });
      setData(res);
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      // Heuristic: a 404 / not-implemented response is the expected case until the
      // backend endpoint ships. Surface a clear TBD UI instead of an angry error.
      if (msg.match(/404|not found|not implemented|405/i)) {
        setMissingEndpoint(true);
        setData(tbdPlaceholder());
      } else {
        setToast({ variant: 'error', message: msg || 'Failed to load analytics.' });
        setData(tbdPlaceholder());
        setMissingEndpoint(true);
      }
    } finally {
      setLoading(false);
    }
  }, [windowDays]);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  return (
    <AdminRouteWorkspace role="main" aria-label="Pronunciation analytics">
      <Link
        href="/admin/content/pronunciation"
        className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
      >
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation drills
      </Link>

      <AdminRouteHero
        eyebrow="Analytics"
        icon={BarChart3}
        accent="navy"
        title="Pronunciation analytics"
        description="Aggregate attempt counts, average scores, and top vs. weakest phonemes across all learners."
      />

      {missingEndpoint && (
        <InlineAlert variant="warning" title="Backend endpoint not yet implemented">
          The page expects{' '}
          <code className="rounded bg-background-light px-1.5 py-0.5 font-mono text-xs">
            GET /v1/admin/pronunciation/analytics
          </code>{' '}
          to return aggregate metrics. Until that ships, the layout below is a placeholder so the rest of the
          admin UI can ship now. The component will switch to live data automatically once the endpoint exists.
        </InlineAlert>
      )}

      <AdminRoutePanel>
        <div className="flex flex-wrap items-end gap-3">
          <Select
            value={windowDays}
            onChange={(e) => setWindowDays(e.target.value)}
            label="Window"
            options={WINDOW_OPTIONS}
          />
          <Button
            variant="outline"
            size="sm"
            onClick={() => void load()}
            className="inline-flex items-center gap-2"
          >
            <RotateCcw className="h-4 w-4" /> Reload
          </Button>
        </div>
      </AdminRoutePanel>

      {loading ? (
        <Skeleton className="h-48 w-full rounded-2xl" />
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            <Card className="p-4">
              <div className="text-xs uppercase text-muted">Total attempts</div>
              <div className="mt-2 text-3xl font-semibold">{data?.totalAttempts ?? 0}</div>
            </Card>
            <Card className="p-4">
              <div className="text-xs uppercase text-muted">Average score</div>
              <div className="mt-2 text-3xl font-semibold">
                {data?.averageScore == null ? '—' : data.averageScore.toFixed(1)}
              </div>
              <div className="text-xs text-muted">out of 100 (advisory)</div>
            </Card>
            <Card className="p-4">
              <div className="text-xs uppercase text-muted">Data source</div>
              <div className="mt-2">
                <Badge variant={data?.source === 'live' ? 'success' : 'muted'}>
                  {data?.source ?? 'tbd'}
                </Badge>
              </div>
            </Card>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <AdminRoutePanel title="Top phonemes" description="Highest average scores in the window.">
              <Card className="overflow-hidden">
                <table className="min-w-full text-sm">
                  <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
                    <tr>
                      <th className="px-4 py-2">Phoneme</th>
                      <th className="px-4 py-2 text-right">Attempts</th>
                      <th className="px-4 py-2 text-right">Avg score</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(data?.topPhonemes ?? []).length === 0 ? (
                      <tr>
                        <td colSpan={3} className="px-4 py-6 text-center text-xs text-muted">
                          No data yet.
                        </td>
                      </tr>
                    ) : (
                      data!.topPhonemes.map((p) => (
                        <tr key={p.phoneme} className="border-t border-border">
                          <td className="px-4 py-2 font-mono">{p.phoneme}</td>
                          <td className="px-4 py-2 text-right text-xs">{p.attempts}</td>
                          <td className="px-4 py-2 text-right">
                            {p.averageScore == null ? '—' : p.averageScore.toFixed(1)}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </Card>
            </AdminRoutePanel>

            <AdminRoutePanel title="Weakest phonemes" description="Lowest average scores — review candidates.">
              <Card className="overflow-hidden">
                <table className="min-w-full text-sm">
                  <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
                    <tr>
                      <th className="px-4 py-2">Phoneme</th>
                      <th className="px-4 py-2 text-right">Attempts</th>
                      <th className="px-4 py-2 text-right">Avg score</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(data?.weakestPhonemes ?? []).length === 0 ? (
                      <tr>
                        <td colSpan={3} className="px-4 py-6 text-center text-xs text-muted">
                          No data yet.
                        </td>
                      </tr>
                    ) : (
                      data!.weakestPhonemes.map((p) => (
                        <tr key={p.phoneme} className="border-t border-border">
                          <td className="px-4 py-2 font-mono">{p.phoneme}</td>
                          <td className="px-4 py-2 text-right text-xs">{p.attempts}</td>
                          <td className="px-4 py-2 text-right">
                            {p.averageScore == null ? '—' : p.averageScore.toFixed(1)}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </Card>
            </AdminRoutePanel>
          </div>
        </>
      )}

      {toast && (
        <Toast variant="error" message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
