'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Plus, Sparkles, Archive, Mic, Search } from 'lucide-react';
import {
  fetchAdminPronunciationDrills,
  archiveAdminPronunciationDrill,
  forceDeleteAdminPronunciationDrill,
} from '@/lib/api';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

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

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Pronunciation' },
];

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

  const handleForceDelete = useCallback(async (drillId: string) => {
    if (!confirm('Permanently delete this archived drill AND all learner attempts/assessments for it? This cannot be undone.')) return;
    try {
      await forceDeleteAdminPronunciationDrill(drillId);
      setToast({ variant: 'success', message: 'Drill permanently deleted' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to delete' });
    }
  }, [load]);

  return (
    <>
      <AdminTableLayout
        title="Pronunciation CMS"
        description="Manage pronunciation drills. Each drill is grounded in the pronunciation rulebook; publishing requires phoneme, tips, and at least 3 example words plus 1 sentence."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" asChild>
              <Link href="/admin/content/pronunciation/ai-draft">
                <Sparkles className="mr-2 h-4 w-4" /> AI draft
              </Link>
            </Button>
            <Button size="sm" asChild>
              <Link href="/admin/content/pronunciation/new">
                <Plus className="mr-2 h-4 w-4" /> New drill
              </Link>
            </Button>
          </div>
        }
        banner={
          <div className="grid grid-cols-1 gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 shadow-admin-sm md:grid-cols-4">
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-admin-fg-muted">
              Search
              <div className="relative mt-1">
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Label or phoneme…"
                  aria-label="Search drills by label or phoneme"
                  className="w-full rounded-admin border border-admin-border bg-admin-bg-surface pl-9 pr-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                />
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-admin-fg-muted" aria-hidden />
              </div>
            </label>
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-admin-fg-muted">
              Profession
              <select
                value={profession}
                onChange={(e) => setProfession(e.target.value)}
                aria-label="Filter by profession"
                className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
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
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-admin-fg-muted">
              Difficulty
              <select
                value={difficulty}
                onChange={(e) => setDifficulty(e.target.value)}
                aria-label="Filter by difficulty"
                className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              >
                <option value="">All</option>
                <option value="easy">Easy</option>
                <option value="medium">Medium</option>
                <option value="hard">Hard</option>
              </select>
            </label>
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-admin-fg-muted">
              Status
              <select
                value={status}
                onChange={(e) => setStatus(e.target.value)}
                aria-label="Filter by status"
                className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              >
                <option value="">All</option>
                <option value="draft">Draft</option>
                <option value="active">Active</option>
                <option value="archived">Archived</option>
              </select>
            </label>
          </div>
        }
      >
        <div className="p-4 sm:p-5">
          <div className="mb-3 text-sm text-admin-fg-muted">{total} drill{total === 1 ? '' : 's'}</div>

          {loading ? (
            <div className="space-y-2">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-14 rounded-admin" />
              ))}
            </div>
          ) : rows.length === 0 ? (
            <EmptyState
              title="No drills match these filters"
              description="Adjust the filters or create a new drill to populate the library."
              primaryAction={{ label: 'New drill', href: '/admin/content/pronunciation/new' }}
            />
          ) : (
            <Card className="overflow-hidden">
              <table className="min-w-full text-sm">
                <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-[0.15em] text-admin-fg-muted">
                  <tr>
                    <th scope="col" className="px-4 py-2">Label</th>
                    <th scope="col" className="px-4 py-2">Phoneme</th>
                    <th scope="col" className="px-4 py-2">Rule</th>
                    <th scope="col" className="px-4 py-2">Focus</th>
                    <th scope="col" className="px-4 py-2">Profession</th>
                    <th scope="col" className="px-4 py-2">Difficulty</th>
                    <th scope="col" className="px-4 py-2">Status</th>
                    <th scope="col" className="px-4 py-2">Audio</th>
                    <th scope="col" className="px-4 py-2 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id} className="border-t border-admin-border hover:bg-admin-bg-subtle/40">
                      <td className="px-4 py-2">
                        <Link
                          href={`/admin/content/pronunciation/${r.id}`}
                          className="font-medium text-[var(--admin-primary)] hover:underline"
                        >
                          {r.label}
                        </Link>
                      </td>
                      <td className="px-4 py-2 font-mono text-xs">/{r.targetPhoneme}/</td>
                      <td className="px-4 py-2 font-mono text-xs text-admin-fg-muted">{r.primaryRuleId ?? '-'}</td>
                      <td className="px-4 py-2 text-xs capitalize">{r.focus}</td>
                      <td className="px-4 py-2 text-xs capitalize">{r.profession.replace('-', ' ')}</td>
                      <td className="px-4 py-2 text-xs capitalize">{r.difficulty}</td>
                      <td className="px-4 py-2">
                        <Badge variant={statusToTone(r.status)}>
                          {r.status}
                        </Badge>
                      </td>
                      <td className="px-4 py-2">
                        {r.audioModelUrl ? (
                          <span className="text-xs text-[var(--admin-success)]">ok</span>
                        ) : (
                          <span className="text-xs text-admin-fg-muted">-</span>
                        )}
                      </td>
                      <td className="px-4 py-2 text-right">
                        <div className="flex justify-end gap-1">
                          <Button variant="ghost" size="sm" aria-label={`Edit ${r.label}`} asChild>
                            <Link href={`/admin/content/pronunciation/${r.id}`}>
                              <Mic className="h-3.5 w-3.5" />
                            </Link>
                          </Button>
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
                          {r.status === 'archived' && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-red-600"
                              onClick={() => handleForceDelete(r.id)}
                              aria-label={`Force delete ${r.label}`}
                            >
                              Force delete
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
        </div>
      </AdminTableLayout>

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </>
  );
}
