'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Plus, Sparkles, Archive, Mic, Search } from 'lucide-react';
import {
  fetchAdminPronunciationDrills,
  archiveAdminPronunciationDrill,
} from '@/lib/api';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

type DrillRow = {
  id: string;
  label: string;
  targetPhoneme: string;
  profession: string;
  focus: string;
  primaryRuleId: string | null;
  difficulty: string;
  status: string;
  audioModelUrl: string | null;
  orderIndex: number;
  updatedAt: string;
};

type ListResponse = {
  total: number;
  page: number;
  pageSize: number;
  items: DrillRow[];
};

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AdminPronunciationDashboard() {
  const [rows, setRows] = useState<DrillRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);
  const [profession, setProfession] = useState('');
  const [difficulty, setDifficulty] = useState('');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetchAdminPronunciationDrills({
        profession: profession || undefined,
        difficulty: difficulty || undefined,
        status: status || undefined,
        search: search || undefined,
        pageSize: 100,
      }) as ListResponse;
      setRows(res.items ?? []);
      setTotal(res.total ?? 0);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load drills' });
    } finally {
      setLoading(false);
    }
  }, [profession, difficulty, status, search]);

  useEffect(() => { void load(); }, [load]);

  const handleArchive = useCallback(async (drillId: string) => {
    if (!confirm('Archive this drill? Learners will no longer see it.')) return;
    try {
      await archiveAdminPronunciationDrill(drillId);
      setToast({ variant: 'success', message: 'Drill archived' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to archive' });
    }
  }, [load]);

  return (
    <AdminRouteWorkspace role="main" aria-label="Pronunciation CMS">
      <AdminRouteHero
        eyebrow="CMS"
        icon={Mic}
        accent="navy"
        title="Pronunciation CMS"
        description="Manage pronunciation drills. Each drill is grounded in the pronunciation rulebook; publishing requires phoneme, tips, and at least 3 example words plus 1 sentence."
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap gap-2">
              <Link href="/admin/content/pronunciation/ai-draft">
                <Button variant="secondary" className="gap-2">
                  <Sparkles className="h-4 w-4" /> AI draft
                </Button>
              </Link>
              <Link href="/admin/content/pronunciation/new">
                <Button variant="primary" className="gap-2">
                  <Plus className="h-4 w-4" /> New drill
                </Button>
              </Link>
            </div>
          </div>
        )}
      />

      <div className="space-y-4">
        <div className="text-sm text-muted">{total} drill{total === 1 ? '' : 's'}</div>

        <AdminRoutePanel>
          <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-muted">
              Search
              <div className="relative mt-1">
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Label or phoneme…"
                  aria-label="Search drills by label or phoneme"
                  className="w-full rounded-xl border border-border bg-surface pl-9 pr-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary dark:text-white"
                />
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted" aria-hidden />
              </div>
            </label>
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-muted">
              Profession
              <select
                value={profession}
                onChange={(e) => setProfession(e.target.value)}
                aria-label="Filter by profession"
                className="mt-1 rounded-xl border border-border bg-surface px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary dark:text-white"
              >
                <option value="">All</option>
                <option value="all">All / generic</option>
                <option value="medicine">Medicine</option>
                <option value="nursing">Nursing</option>
                <option value="dentistry">Dentistry</option>
                <option value="pharmacy">Pharmacy</option>
                <option value="physiotherapy">Physiotherapy</option>
                <option value="occupational-therapy">Occupational therapy</option>
                <option value="speech-pathology">Speech pathology</option>
              </select>
            </label>
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-muted">
              Difficulty
              <select
                value={difficulty}
                onChange={(e) => setDifficulty(e.target.value)}
                aria-label="Filter by difficulty"
                className="mt-1 rounded-xl border border-border bg-surface px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary dark:text-white"
              >
                <option value="">All</option>
                <option value="easy">Easy</option>
                <option value="medium">Medium</option>
                <option value="hard">Hard</option>
              </select>
            </label>
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-muted">
              Status
              <select
                value={status}
                onChange={(e) => setStatus(e.target.value)}
                aria-label="Filter by status"
                className="mt-1 rounded-xl border border-border bg-surface px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary dark:text-white"
              >
                <option value="">All</option>
                <option value="draft">Draft</option>
                <option value="active">Active</option>
                <option value="archived">Archived</option>
              </select>
            </label>
          </div>
        </AdminRoutePanel>

        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-14 rounded-xl" />
            ))}
          </div>
        ) : rows.length === 0 ? (
          <Card className="p-8 text-center text-sm text-muted">
            No drills match these filters.
          </Card>
        ) : (
          <Card className="overflow-hidden">
            <table className="min-w-full text-sm">
              <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
                <tr>
                  <th className="px-4 py-2">Label</th>
                  <th className="px-4 py-2">Phoneme</th>
                  <th className="px-4 py-2">Rule</th>
                  <th className="px-4 py-2">Focus</th>
                  <th className="px-4 py-2">Profession</th>
                  <th className="px-4 py-2">Difficulty</th>
                  <th className="px-4 py-2">Status</th>
                  <th className="px-4 py-2">Audio</th>
                  <th className="px-4 py-2 text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id} className="border-t border-border hover:bg-background-light/40">
                    <td className="px-4 py-2">
                      <Link
                        href={`/admin/content/pronunciation/${r.id}`}
                        className="font-medium text-primary hover:underline"
                      >
                        {r.label}
                      </Link>
                    </td>
                    <td className="px-4 py-2 font-mono text-xs">/{r.targetPhoneme}/</td>
                    <td className="px-4 py-2 font-mono text-xs text-muted">{r.primaryRuleId ?? '—'}</td>
                    <td className="px-4 py-2 text-xs capitalize">{r.focus}</td>
                    <td className="px-4 py-2 text-xs capitalize">{r.profession.replace('-', ' ')}</td>
                    <td className="px-4 py-2 text-xs capitalize">{r.difficulty}</td>
                    <td className="px-4 py-2">
                      <Badge
                        variant={r.status === 'active' ? 'success' : r.status === 'archived' ? 'muted' : 'warning'}
                      >
                        {r.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-2">
                      {r.audioModelUrl ? (
                        <span className="text-xs text-success">✓</span>
                      ) : (
                        <span className="text-xs text-muted">—</span>
                      )}
                    </td>
                    <td className="px-4 py-2 text-right">
                      <div className="flex justify-end gap-1">
                        <Link href={`/admin/content/pronunciation/${r.id}`}>
                          <Button variant="ghost" size="sm" aria-label={`Edit ${r.label}`}>
                            <Mic className="h-3.5 w-3.5" />
                          </Button>
                        </Link>
                        {r.status !== 'archived' && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleArchive(r.id)}
                            aria-label={`Archive ${r.label}`}
                          >
                            <Archive className="h-3.5 w-3.5" />
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}

        {toast && (
          <Toast
            variant={toast.variant === 'error' ? 'error' : 'success'}
            message={toast.message}
            onClose={() => setToast(null)}
          />
        )}
      </div>
    </AdminRouteWorkspace>
  );
}
