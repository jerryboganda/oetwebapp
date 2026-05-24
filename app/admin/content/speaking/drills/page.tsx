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
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
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

const STATUS_VARIANT: Record<string, 'default' | 'success' | 'warning' | 'muted' | 'info'> = {
  draft: 'muted',
  inreview: 'warning',
  published: 'success',
  archived: 'default',
};

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

  return (
    <AdminRouteWorkspace role="main" aria-label="Speaking drills">
      <div className="space-y-6">
        <header className="flex flex-wrap items-end justify-between gap-3">
          <div className="space-y-1">
            <h1 className="text-2xl font-semibold text-foreground">Speaking - drill bank</h1>
            <p className="text-muted-foreground">
              Curate the post-session remediation drills that the speaking analytics console
              recommends to learners after low-scoring criteria.
            </p>
          </div>
          <Button asChild variant="outline"><Link href="/admin/content/speaking/drills/ai-draft">AI-assisted draft...</Link></Button>
        </header>

        <Card className="space-y-4 p-4">
          <h2 className="font-semibold text-foreground">Filters</h2>
          <div className="grid gap-3 sm:grid-cols-3">
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
            <Select
              value={kindFilter}
              onChange={(e) => setKindFilter(e.target.value)}
              options={[
                { value: '', label: 'All kinds' },
                ...SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k })),
              ]}
            />
            <Button variant="outline" onClick={reload} disabled={loading}>
              Refresh
            </Button>
          </div>
        </Card>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <Card className="space-y-3 p-4">
          <h2 className="font-semibold text-foreground">Create drill (manual)</h2>
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
              className="rounded border border-border p-2 text-sm sm:col-span-2"
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
        </Card>

        {loading ? (
          <Skeleton className="h-48 w-full" />
        ) : visible.length === 0 ? (
          <Card className="p-8 text-center text-muted-foreground">No drills match the current filters.</Card>
        ) : (
          <Card>
            <ul className="divide-y divide-slate-100">
              {visible.map((d) => {
                const variant = STATUS_VARIANT[d.status] ?? 'default';
                return (
                  <li key={d.drillId} className="flex flex-wrap items-center gap-3 p-4">
                    <div className="flex-1 min-w-[16rem]">
                      <div className="flex flex-wrap items-center gap-2">
                        <Link
                          href={`/admin/content/speaking/drills/${encodeURIComponent(d.drillId)}`}
                          className="font-medium text-foreground hover:underline"
                        >
                          {d.title}
                        </Link>
                        <Badge variant={variant}>{d.status}</Badge>
                        <Badge variant="outline">{String(d.drillKind)}</Badge>
                        {d.professionId && <Badge variant="muted">{d.professionId}</Badge>}
                      </div>
                      <p className="mt-1 text-sm text-muted-foreground line-clamp-2">
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
      </div>
    </AdminRouteWorkspace>
  );
}

