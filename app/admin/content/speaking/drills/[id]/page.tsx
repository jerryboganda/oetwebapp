'use client';

/**
 * OET Speaking — Phase 11 P11.5 — admin drill bank detail / edit page.
 */
import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';

import { InlineAlert } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
import {
  archiveAdminDrill,
  deleteAdminDrill,
  forceDeleteAdminDrill,
  getAdminDrill,
  publishAdminDrill,
  SPEAKING_DRILL_KINDS,
  updateAdminDrill,
  type AdminDrillDetail,
  type AdminDrillUpdateInput,
  type SpeakingDrillKind,
} from '@/lib/api/speaking-drills';

const BREADCRUMBS_BASE = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Drill bank', href: '/admin/content/speaking/drills' },
];

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

  async function forceDelete() {
    if (!detail) return;
    if (typeof window !== 'undefined' && !window.confirm('Permanently delete this archived drill AND all learner attempts for it? This cannot be undone.')) return;
    setSaving(true);
    try {
      await forceDeleteAdminDrill(detail.drillId);
      router.push('/admin/content/speaking/drills');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
      setSaving(false);
    }
  }

  const breadcrumbs = [...BREADCRUMBS_BASE, { label: detail?.title ?? 'Drill detail' }];

  return (
    <AdminCatalogLayout
      title={detail?.title ?? 'Drill detail'}
      description="Edit drill metadata, instruction text, and recommendation rules."
      breadcrumbs={breadcrumbs}
      eyebrow="Content"
      backHref="/admin/content/speaking/drills"
      hideViewModeToggle
      actions={
        detail && (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={destroy} disabled={saving}>
              Delete
            </Button>
            {detail.status === 'archived' ? (
              <Button variant="outline" size="sm" className="text-red-600" onClick={forceDelete} disabled={saving}>
                Force delete
              </Button>
            ) : null}
            <Button variant="outline" size="sm" onClick={archive} disabled={saving || detail.status === 'archived'}>
              Archive
            </Button>
            <Button variant="outline" size="sm" onClick={publish} disabled={saving || detail.status === 'published'}>
              Publish
            </Button>
            <Button size="sm" onClick={save} disabled={saving}>
              {saving ? 'Saving…' : 'Save changes'}
            </Button>
          </div>
        )
      }
    >
      {detail && (
        <div className="col-span-full flex flex-wrap gap-2">
          <Badge variant={detail.status === 'published' ? 'success' : 'default'} intensity="tinted">
            {detail.status}
          </Badge>
          <Badge variant="default" intensity="tinted">{String(detail.drillKind)}</Badge>
          {detail.professionId && (
            <Badge variant="default" intensity="tinted">{detail.professionId}</Badge>
          )}
        </div>
      )}

      {error && (
        <div className="col-span-full">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      )}
      {info && (
        <div className="col-span-full">
          <InlineAlert variant="success">{info}</InlineAlert>
        </div>
      )}

      {loading || !detail ? (
        <div className="col-span-full">
          <Skeleton className="h-64 w-full" />
        </div>
      ) : (
        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Drill content</CardTitle>
          </CardHeader>
          <CardContent>
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
                wrapperClassName="sm:col-span-2"
                placeholder="Target criteria (comma-separated)"
                value={(edit.targetCriteria ?? []).join(', ')}
                onChange={(e) =>
                  setEdit({
                    ...edit,
                    targetCriteria: e.target.value.split(','),
                  })
                }
              />
              <Textarea
                wrapperClassName="sm:col-span-2"
                rows={5}
                placeholder="Instruction text"
                value={edit.instructionText ?? ''}
                onChange={(e) => setEdit({ ...edit, instructionText: e.target.value })}
              />
            </div>
          </CardContent>
        </Card>
      )}
    </AdminCatalogLayout>
  );
}
