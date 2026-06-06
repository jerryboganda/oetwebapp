'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { FileText, Plus, RefreshCcw, Trash2 } from 'lucide-react';
import {
  listStudyPlanTemplates,
  bulkStudyPlanTemplateAction,
  type StudyPlanTemplateListItem,
} from '@/lib/study-plan-admin-api';
// New admin DS imports
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Checkbox } from '@/components/admin/ui/checkbox';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Label } from '@/components/admin/ui/label';
import { toast as adminToast } from '@/components/admin/ui/toaster';

type Filter = { tier: string; active: 'all' | 'active' | 'inactive' };

export default function StudyPlanTemplatesAdminPage() {
  const router = useRouter();
  const [rows, setRows] = useState<StudyPlanTemplateListItem[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>({ tier: '', active: 'all' });

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listStudyPlanTemplates({
        tier: filter.tier || undefined,
        active: filter.active === 'all' ? undefined : filter.active === 'active',
      });
      setRows(data);
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Failed to load templates.');
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const toggleSelect = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const selectAll = () => {
    if (selected.size === rows.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(rows.map((r) => r.id)));
    }
  };

  const runBulk = async (action: 'activate' | 'deactivate' | 'duplicate' | 'soft-delete') => {
    if (selected.size === 0) return;
    if (action === 'soft-delete' && !confirm(`Soft-delete ${selected.size} template(s)?`)) return;
    try {
      const result = await bulkStudyPlanTemplateAction(action, Array.from(selected));
      adminToast.success(`${action}: ${result.processed} template(s) processed.`);
      setSelected(new Set());
      await reload();
    } catch (e: any) {
      const message = e?.userMessage ?? e?.message ?? 'Bulk action failed.';
      setError(message);
      adminToast.error(message);
    }
  };

  const banner = (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 shadow-admin-sm">
        <div className="flex flex-col gap-1.5 min-w-[160px]">
          <Label htmlFor="filter-tier">Tier</Label>
          <Select value={filter.tier || '__all__'} onValueChange={(v) => setFilter((f) => ({ ...f, tier: v === '__all__' ? '' : v }))}>
            <SelectTrigger id="filter-tier">
              <SelectValue placeholder="All tiers" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All tiers</SelectItem>
              <SelectItem value="free">Free</SelectItem>
              <SelectItem value="premium">Premium</SelectItem>
              <SelectItem value="elite">Elite</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1.5 min-w-[160px]">
          <Label htmlFor="filter-status">Status</Label>
          <Select value={filter.active} onValueChange={(v) => setFilter((f) => ({ ...f, active: v as Filter['active'] }))}>
            <SelectTrigger id="filter-status">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="active">Active only</SelectItem>
              <SelectItem value="inactive">Inactive only</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <Button variant="outline" onClick={() => void reload()} startIcon={<RefreshCcw className="h-4 w-4" />}>
          Refresh
        </Button>
      </div>

      {selected.size > 0 && (
        <div className="flex flex-wrap items-center gap-2 rounded-admin-lg border border-[var(--admin-primary)] bg-[var(--admin-primary-tint)] px-4 py-3">
          <span className="text-sm font-medium text-admin-fg-strong">{selected.size} selected</span>
          <span className="text-admin-fg-muted" aria-hidden="true">·</span>
          <Button size="sm" onClick={() => runBulk('activate')}>Activate</Button>
          <Button size="sm" variant="outline" onClick={() => runBulk('deactivate')}>Deactivate</Button>
          <Button size="sm" variant="secondary" onClick={() => runBulk('duplicate')}>Duplicate</Button>
          <Button size="sm" variant="destructive" onClick={() => runBulk('soft-delete')} startIcon={<Trash2 className="h-3.5 w-3.5" />}>
            Soft-delete
          </Button>
          <Button size="sm" variant="ghost" className="ml-auto" onClick={() => setSelected(new Set())}>
            Clear
          </Button>
        </div>
      )}

      {error && (
        <div role="alert" className="rounded-admin-lg border border-[var(--admin-danger)] bg-[var(--admin-danger-tint)] px-4 py-3 text-sm text-[var(--admin-danger)]">
          {error}
        </div>
      )}
    </div>
  );

  return (
    <>
      <AdminTableLayout
        title="Study Plan Templates"
        description="Admin-authored skeletons the planner picks from for each learner. Tier-gated and profession-aware."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Study Plan Templates' }]}
        actions={
          <Button asChild startIcon={<Plus className="h-4 w-4" />}>
            <Link href="/admin/study-plan-templates/new">New Template</Link>
          </Button>
        }
        banner={banner}
      >
        {loading ? (
          <div className="p-6 space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-10 rounded-admin" />
            ))}
          </div>
        ) : rows.length === 0 ? (
          <div className="p-8">
            <EmptyState
              illustration={<FileText aria-hidden="true" />}
              title="No templates match these filters"
              description="Adjust the filters above or create your first template to get started."
              primaryAction={{ label: 'Create your first template', href: '/admin/study-plan-templates/new' }}
            />
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-admin-bg-subtle">
                <tr>
                  <th scope="col" className="w-10 px-3 py-2.5 text-left">
                    <Checkbox
                      checked={selected.size === rows.length && rows.length > 0}
                      onCheckedChange={selectAll}
                      aria-label="Select all templates"
                    />
                  </th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Name</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Slug</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Weeks</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Tiers</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Profession</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Band</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Status</th>
                  <th scope="col" className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">v</th>
                  <th scope="col" className="px-3 py-2.5"></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id} className="border-t border-admin-border transition-colors hover:bg-[var(--admin-state-hover)]">
                    <td className="px-3 py-3 align-middle">
                      <Checkbox
                        checked={selected.has(r.id)}
                        onCheckedChange={() => toggleSelect(r.id)}
                        aria-label={`Select ${r.name}`}
                      />
                    </td>
                    <td className="px-3 py-3 align-middle">
                      <button
                        type="button"
                        onClick={() => router.push(`/admin/study-plan-templates/${r.id}`)}
                        className="text-left text-admin-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] rounded-admin-sm"
                      >
                        {r.name}
                      </button>
                      {r.description && (
                        <div className="text-xs text-admin-fg-muted mt-1">{r.description}</div>
                      )}
                    </td>
                    <td className="px-3 py-3 align-middle font-mono text-xs text-admin-fg-muted">{r.slug}</td>
                    <td className="px-3 py-3 align-middle">{r.minWeeks}–{r.maxWeeks}</td>
                    <td className="px-3 py-3 align-middle">
                      {r.tierCodes.length === 0 ? (
                        <span className="text-admin-fg-muted">none</span>
                      ) : (
                        <div className="flex flex-wrap gap-1">
                          {r.tierCodes.map((t) => (
                            <Badge key={t} variant="default" size="sm">{t}</Badge>
                          ))}
                        </div>
                      )}
                    </td>
                    <td className="px-3 py-3 align-middle">{r.professionId ?? <span className="text-admin-fg-muted">any</span>}</td>
                    <td className="px-3 py-3 align-middle">{r.targetBand ?? <span className="text-admin-fg-muted">any</span>}</td>
                    <td className="px-3 py-3 align-middle">
                      <Badge variant={r.isActive ? 'success' : 'secondary'}>
                        {r.isActive ? 'Active' : 'Inactive'}
                      </Badge>
                    </td>
                    <td className="px-3 py-3 align-middle text-admin-fg-muted">{r.version}</td>
                    <td className="px-3 py-3 align-middle">
                      <Button variant="link" size="sm" asChild>
                        <Link href={`/admin/study-plan-templates/${r.id}`}>Edit</Link>
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </AdminTableLayout>
    </>
  );
}
