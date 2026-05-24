'use client';

/**
 * OET Speaking - Phase 11 P11.5 - admin drill bank list page.
 *
 * Lists drills curated by status (draft / in-review / published / archived)
 * and lets the admin create / publish / archive / delete. Detail editing
 * lives at `[id]/page.tsx`.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Sparkles } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

import { InlineAlert } from '@/components/ui/alert';
import { Input, Select } from '@/components/ui/form-controls';
import {
  archiveAdminDrill,
  createAdminDrill,
  deleteAdminDrill,
  listAdminDrills,
  publishAdminDrill,
  SPEAKING_DRILL_KINDS,
  type AdminDrillCreateInput,
  type AdminDrillSummary,
  type SpeakingDrillKind,
} from '@/lib/api/speaking-drills';

const STATUS_VARIANT: Record<string, 'default' | 'success' | 'warning' | 'info'> = {
  draft: 'default',
  inreview: 'warning',
  published: 'success',
  archived: 'default',
};

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Drill bank' },
];

export default function AdminSpeakingDrillsPage() {
  const [drills, setDrills] = useState<AdminDrillSummary[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [kindFilter, setKindFilter] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);

  const [draft, setDraft] = useState<AdminDrillCreateInput>({
    drillKind: 'fluency_relay',
    title: '',
    instructionText: '',
    targetCriteria: [],
    professionId: null,
  });

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await listAdminDrills({
        status: (statusFilter || undefined) as 'draft' | 'inreview' | 'published' | 'archived' | undefined,
        drillKind: kindFilter || undefined,
      });
      setDrills(res.drills ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load drills.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter, kindFilter]);

  useEffect(() => {
    reload();
  }, [reload]);

  const visible = useMemo(() => drills, [drills]);

  async function publish(id: string) {
    setBusyId(id);
    try {
      await publishAdminDrill(id);
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Publish failed.');
    } finally {
      setBusyId(null);
    }
  }

  async function archive(id: string) {
    setBusyId(id);
    try {
      await archiveAdminDrill(id);
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Archive failed.');
    } finally {
      setBusyId(null);
    }
  }

  async function destroy(id: string) {
    if (typeof window !== 'undefined' && !window.confirm('Delete this drill? This cannot be undone.')) return;
    setBusyId(id);
    try {
      await deleteAdminDrill(id);
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusyId(null);
    }
  }

  async function submitDraft(e: React.FormEvent) {
    e.preventDefault();
    if (!draft.title.trim() || !draft.instructionText.trim()) return;
    setCreating(true);
    setError(null);
    try {
      const targetCriteria = draft.targetCriteria
        .map((s) => s.trim())
        .filter((s) => s.length > 0);
      await createAdminDrill({ ...draft, targetCriteria });
      setDraft({
        drillKind: 'fluency_relay',
        title: '',
        instructionText: '',
        targetCriteria: [],
        professionId: null,
      });
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create drill.');
    } finally {
      setCreating(false);
    }
  }

  const filtersNode = (
    <div className="flex flex-wrap items-end gap-2">
      <div className="w-40">
        <Select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          options={[
            { value: '', label: 'All statuses' },
            { value: 'draft', label: 'Draft' },
            { value: 'inreview', label: 'In review' },
            { value: 'published', label: 'Published' },
            { value: 'archived', label: 'Archived' },
          ]}
        />
      </div>
      <div className="w-40">
        <Select
          value={kindFilter}
          onChange={(e) => setKindFilter(e.target.value)}
          options={[
            { value: '', label: 'All kinds' },
            ...SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k })),
          ]}
        />
      </div>
      <Button variant="outline" size="sm" onClick={reload} disabled={loading}>
        Refresh
      </Button>
    </div>
  );

  return (
    <AdminCatalogLayout
      title="Speaking drill bank"
      description="Curate the post-session remediation drills that the speaking analytics console recommends to learners after low-scoring criteria."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Content"
      hideViewModeToggle
      filters={filtersNode}
      actions={
        <Button asChild variant="outline">
          <Link href="/admin/content/speaking/drills/ai-draft">AI-assisted draft</Link>
        </Button>
      }
    >
      {error && (
        <div className="col-span-full">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      )}

      <Card className="col-span-full">
        <CardHeader>
          <CardTitle>Create drill (manual)</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="grid gap-3 sm:grid-cols-2" onSubmit={submitDraft}>
            <Input
              placeholder="Title"
              value={draft.title}
              onChange={(e) => setDraft({ ...draft, title: e.target.value })}
              required
            />
            <Select
              value={draft.drillKind}
              onChange={(e) =>
                setDraft({ ...draft, drillKind: e.target.value as SpeakingDrillKind })
              }
              options={SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k }))}
            />
            <Input
              placeholder="Profession id (optional)"
              value={draft.professionId ?? ''}
              onChange={(e) =>
                setDraft({ ...draft, professionId: e.target.value || null })
              }
            />
            <Input
              placeholder="Target criteria (comma-separated)"
              value={draft.targetCriteria.join(', ')}
              onChange={(e) =>
                setDraft({
                  ...draft,
                  targetCriteria: e.target.value.split(','),
                })
              }
            />
            <textarea
              className="rounded-admin border border-admin-border bg-admin-bg-surface p-2 text-sm sm:col-span-2"
              rows={3}
              placeholder="Instruction text shown to the learner"
              value={draft.instructionText}
              onChange={(e) => setDraft({ ...draft, instructionText: e.target.value })}
              required
            />
            <div className="sm:col-span-2 flex justify-end">
              <Button type="submit" disabled={creating}>
                {creating ? 'Creating...' : 'Create drill'}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      {loading ? (
        <div className="col-span-full">
          <Skeleton className="h-48 w-full" />
        </div>
      ) : visible.length === 0 ? (
        <div className="col-span-full">
          <EmptyState
            illustration={<Sparkles />}
            title="No drills match the current filters"
            description="Adjust filters or create a new drill above."
          />
        </div>
      ) : (
        <Card className="col-span-full">
          <ul className="divide-y divide-admin-border">
            {visible.map((d) => {
              const variant = STATUS_VARIANT[d.status] ?? 'default';
              return (
                <li key={d.drillId} className="flex flex-wrap items-center gap-3 p-4">
                  <div className="flex-1 min-w-[16rem]">
                    <div className="flex flex-wrap items-center gap-2">
                      <Link
                        href={`/admin/content/speaking/drills/${encodeURIComponent(d.drillId)}`}
                        className="font-medium text-admin-fg-strong hover:underline"
                      >
                        {d.title}
                      </Link>
                      <Badge variant={variant as any} intensity="tinted">{d.status}</Badge>
                      <Badge variant="default" intensity="tinted">{String(d.drillKind)}</Badge>
                      {d.professionId && <Badge variant="default" intensity="tinted">{d.professionId}</Badge>}
                    </div>
                    <p className="mt-1 text-sm text-admin-fg-muted line-clamp-2">
                      {d.instructionText}
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {d.status !== 'published' && (
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => publish(d.drillId)}
                        disabled={busyId === d.drillId}
                      >
                        Publish
                      </Button>
                    )}
                    {d.status !== 'archived' && (
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => archive(d.drillId)}
                        disabled={busyId === d.drillId}
                      >
                        Archive
                      </Button>
                    )}
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => destroy(d.drillId)}
                      disabled={busyId === d.drillId}
                    >
                      Delete
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>
        </Card>
      )}
    </AdminCatalogLayout>
  );
}
