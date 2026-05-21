'use client';

/**
 * OET Speaking — Phase 11 P11.5 — admin drill bank detail / edit page.
 */
import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
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
  deleteAdminDrill,
  getAdminDrill,
  publishAdminDrill,
  SPEAKING_DRILL_KINDS,
  updateAdminDrill,
  type AdminDrillDetail,
  type AdminDrillUpdateInput,
  type SpeakingDrillKind,
} from '@/lib/api/speaking-drills';

export default function AdminSpeakingDrillEditPage() {
  const params = useParams();
  const router = useRouter();
  const drillId = typeof params?.id === 'string' ? params.id : Array.isArray(params?.id) ? params.id[0] : '';

  const [detail, setDetail] = useState<AdminDrillDetail | null>(null);
  const [edit, setEdit] = useState<AdminDrillUpdateInput>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const reload = useCallback(async () => {
    if (!drillId) return;
    setLoading(true);
    setError(null);
    try {
      const d = await getAdminDrill(drillId);
      setDetail(d);
      setEdit({
        drillKind: d.drillKind,
        professionId: d.professionId,
        title: d.title,
        instructionText: d.instructionText,
        targetCriteria: d.targetCriteria,
        recommendedAfterSessionScoreBelow: d.recommendedAfterSessionScoreBelow ?? null,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load drill.');
    } finally {
      setLoading(false);
    }
  }, [drillId]);

  useEffect(() => {
    reload();
  }, [reload]);

  async function save() {
    if (!detail) return;
    setSaving(true);
    setError(null);
    setInfo(null);
    try {
      await updateAdminDrill(detail.drillId, {
        ...edit,
        targetCriteria: (edit.targetCriteria ?? [])
          .map((s) => s.trim())
          .filter((s) => s.length > 0),
      });
      setInfo('Saved.');
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }

  async function publish() {
    if (!detail) return;
    setSaving(true);
    try {
      await publishAdminDrill(detail.drillId);
      await reload();
      setInfo('Published.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Publish failed.');
    } finally {
      setSaving(false);
    }
  }

  async function archive() {
    if (!detail) return;
    setSaving(true);
    try {
      await archiveAdminDrill(detail.drillId);
      await reload();
      setInfo('Archived.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Archive failed.');
    } finally {
      setSaving(false);
    }
  }

  async function destroy() {
    if (!detail) return;
    if (typeof window !== 'undefined' && !window.confirm('Delete this drill? This cannot be undone.')) return;
    setSaving(true);
    try {
      await deleteAdminDrill(detail.drillId);
      router.push('/admin/content/speaking/drills');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
      setSaving(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Edit speaking drill">
      <div className="space-y-6">
        <header className="flex flex-wrap items-center justify-between gap-3">
          <div className="space-y-1">
            <Link href="/admin/content/speaking/drills" className="text-sm text-slate-500 hover:underline">
              ← Back to drill bank
            </Link>
            <h1 className="text-2xl font-semibold text-slate-900">
              {detail?.title ?? 'Drill detail'}
            </h1>
            {detail && (
              <div className="flex flex-wrap gap-2">
                <Badge variant={detail.status === 'published' ? 'success' : 'muted'}>{detail.status}</Badge>
                <Badge variant="outline">{String(detail.drillKind)}</Badge>
                {detail.professionId && <Badge variant="muted">{detail.professionId}</Badge>}
              </div>
            )}
          </div>
        </header>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        {info && <InlineAlert variant="success">{info}</InlineAlert>}

        {loading || !detail ? (
          <Skeleton className="h-64 w-full" />
        ) : (
          <Card className="space-y-4 p-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <Input
                placeholder="Title"
                value={edit.title ?? ''}
                onChange={(e) => setEdit({ ...edit, title: e.target.value })}
              />
              <Select
                value={(edit.drillKind as string) ?? detail.drillKind}
                onChange={(e) =>
                  setEdit({ ...edit, drillKind: e.target.value as SpeakingDrillKind })
                }
                options={SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k }))}
              />
              <Input
                placeholder="Profession id (optional)"
                value={edit.professionId ?? ''}
                onChange={(e) => setEdit({ ...edit, professionId: e.target.value || null })}
              />
              <Input
                type="number"
                step={1}
                min={0}
                max={500}
                placeholder="Recommend when scaled below…"
                value={edit.recommendedAfterSessionScoreBelow ?? ''}
                onChange={(e) =>
                  setEdit({
                    ...edit,
                    recommendedAfterSessionScoreBelow:
                      e.target.value === '' ? null : Number(e.target.value),
                  })
                }
              />
              <Input
                className="sm:col-span-2"
                placeholder="Target criteria (comma-separated)"
                value={(edit.targetCriteria ?? []).join(', ')}
                onChange={(e) =>
                  setEdit({
                    ...edit,
                    targetCriteria: e.target.value.split(','),
                  })
                }
              />
              <textarea
                className="rounded border border-slate-300 p-2 text-sm sm:col-span-2"
                rows={5}
                placeholder="Instruction text"
                value={edit.instructionText ?? ''}
                onChange={(e) => setEdit({ ...edit, instructionText: e.target.value })}
              />
            </div>

            <div className="flex flex-wrap gap-2 justify-end">
              <Button variant="outline" onClick={destroy} disabled={saving}>
                Delete
              </Button>
              <Button variant="outline" onClick={archive} disabled={saving || detail.status === 'archived'}>
                Archive
              </Button>
              <Button variant="outline" onClick={publish} disabled={saving || detail.status === 'published'}>
                Publish
              </Button>
              <Button onClick={save} disabled={saving}>
                {saving ? 'Saving…' : 'Save changes'}
              </Button>
            </div>
          </Card>
        )}
      </div>
    </AdminRouteWorkspace>
  );
}
