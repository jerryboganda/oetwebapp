'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, GraduationCap, Plus, RefreshCw, Save, Send, X, Archive } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { Checkbox } from '@/components/admin/ui/checkbox';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import { Toast } from '@/components/ui/alert';
import {
  adminListInterlocutorModules,
  adminCreateInterlocutorModule,
  adminUpdateInterlocutorModule,
  adminPublishInterlocutorModule,
  adminArchiveInterlocutorModule,
  type InterlocutorTrainingModule,
  type InterlocutorTrainingStage,
} from '@/lib/api/interlocutor-training';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STAGE_OPTIONS: { value: InterlocutorTrainingStage; label: string }[] = [
  { value: 'Onboarding', label: 'Onboarding' },
  { value: 'Refresher', label: 'Refresher' },
];

const STAGE_FILTER_OPTIONS = [{ value: '', label: 'All stages' }, ...STAGE_OPTIONS];

const STATUS_FILTER_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Onboarding', href: '/admin/onboarding' },
  { label: 'Interlocutor', href: '/admin/onboarding/interlocutor' },
  { label: 'Training modules' },
];

function statusBadge(status: string) {
  if (status === 'Published') return <Badge variant="success" size="sm">Published</Badge>;
  if (status === 'Archived') return <Badge variant="secondary" size="sm">Archived</Badge>;
  return <Badge variant="warning" size="sm">Draft</Badge>;
}

function formatDate(iso: string | null): string {
  if (!iso) return '-';
  try {
    return new Date(iso).toLocaleDateString();
  } catch {
    return iso;
  }
}

type ModuleForm = {
  title: string;
  contentMarkdown: string;
  stage: InterlocutorTrainingStage;
  orderIndex: number;
  requiredForCalibration: boolean;
};

const EMPTY_FORM: ModuleForm = {
  title: '',
  contentMarkdown: '',
  stage: 'Onboarding',
  orderIndex: 100,
  requiredForCalibration: false,
};

export default function AdminInterlocutorModulesPage() {
  const [items, setItems] = useState<InterlocutorTrainingModule[]>([]);
  const [loading, setLoading] = useState(true);
  const [stageFilter, setStageFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState<InterlocutorTrainingModule | null>(null);
  const [form, setForm] = useState<ModuleForm>(EMPTY_FORM);
  const [saving, setSaving] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListInterlocutorModules({
        stage: stageFilter || null,
        status: statusFilter || null,
      });
      setItems(data ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [stageFilter, statusFilter]);

  useEffect(() => { void reload(); }, [reload]);

  const publishedCount = useMemo(
    () => items.filter((m) => m.status === 'Published').length,
    [items],
  );

  function openCreate() {
    setEditing(null);
    // Park new modules at the end of the current stage ordering.
    const nextOrder = items.length === 0
      ? 100
      : Math.max(...items.map((m) => m.orderIndex)) + 10;
    setForm({ ...EMPTY_FORM, orderIndex: nextOrder });
    setEditOpen(true);
  }

  function openEdit(row: InterlocutorTrainingModule) {
    setEditing(row);
    setForm({
      title: row.title,
      contentMarkdown: row.contentMarkdown ?? '',
      stage: (row.stage === 'Refresher' ? 'Refresher' : 'Onboarding'),
      orderIndex: row.orderIndex,
      requiredForCalibration: row.requiredForCalibration,
    });
    setEditOpen(true);
  }

  async function handleSave(e?: React.FormEvent) {
    e?.preventDefault();
    if (!form.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    setSaving(true);
    try {
      if (editing) {
        await adminUpdateInterlocutorModule(editing.id, {
          title: form.title.trim(),
          contentMarkdown: form.contentMarkdown,
          stage: form.stage,
          orderIndex: form.orderIndex,
          requiredForCalibration: form.requiredForCalibration,
        });
        setToast({ variant: 'success', message: `Updated "${form.title.trim()}".` });
      } else {
        const created = await adminCreateInterlocutorModule({
          title: form.title.trim(),
          contentMarkdown: form.contentMarkdown,
          stage: form.stage,
          orderIndex: form.orderIndex,
          requiredForCalibration: form.requiredForCalibration,
        });
        setToast({
          variant: 'success',
          message: `Created "${created.title}" as a draft. Publish it to reach tutors.`,
        });
      }
      setEditOpen(false);
      await reload();
    } catch (err) {
      setToast({ variant: 'error', message: (err as Error).message });
    } finally {
      setSaving(false);
    }
  }

  async function handlePublish(row: InterlocutorTrainingModule) {
    if (row.requiredForCalibration && !confirm(
      `Publish "${row.title}"?\n\nThis module is marked required for calibration — publishing it immediately blocks live-room eligibility for every tutor who has not completed it.`,
    )) return;
    setBusyId(row.id);
    try {
      const updated = await adminPublishInterlocutorModule(row.id);
      setItems((prev) => prev.map((m) => (m.id === row.id ? updated : m)));
      setToast({ variant: 'success', message: `Published "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleArchive(row: InterlocutorTrainingModule) {
    if (!confirm(
      `Archive "${row.title}"?\n\nArchived modules disappear from every tutor's training checklist. Existing completion records are kept.`,
    )) return;
    setBusyId(row.id);
    try {
      const updated = await adminArchiveInterlocutorModule(row.id);
      setItems((prev) => prev.map((m) => (m.id === row.id ? updated : m)));
      setToast({ variant: 'success', message: `Archived "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  const filters = (
    <div className="flex flex-wrap items-end justify-between w-full gap-3">
      <div className="flex flex-wrap items-end gap-3">
        <NativeSelect
          label="Stage"
          value={stageFilter}
          onChange={(e) => setStageFilter(e.target.value)}
          options={STAGE_FILTER_OPTIONS}
          wrapperClassName="w-44"
        />
        <NativeSelect
          label="Status"
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          options={STATUS_FILTER_OPTIONS}
          wrapperClassName="w-44"
        />
      </div>
      <Button size="sm" variant="ghost" onClick={() => void reload()}>
        <RefreshCw className="h-4 w-4 mr-1" /> Refresh
      </Button>
    </div>
  );

  return (
    <>
      <AdminCatalogLayout
        eyebrow="Onboarding"
        title="Interlocutor training modules"
        description="Author the markdown training modules tutors work through before they join the live calibration pool. Modules stay in Draft until published; modules marked required for calibration gate live-room eligibility once published."
        breadcrumbs={BREADCRUMBS}
        actions={(
          <>
            <Button variant="secondary" size="sm" asChild>
              <Link href="/admin/onboarding/interlocutor">
                <ArrowLeft className="mr-1.5 h-4 w-4" />Back to trainees
              </Link>
            </Button>
            <Button variant="primary" size="sm" onClick={openCreate}>
              <Plus className="mr-1.5 h-4 w-4" />New module
            </Button>
          </>
        )}
        filters={filters}
        hideViewModeToggle
        itemsClassName="flex flex-col gap-3"
      >
        {loading ? (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-24 rounded-admin-lg" />)}
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            illustration={<GraduationCap />}
            title="No training modules yet"
            description='Tutors see an empty checklist until at least one module is published. Click "New module" to author the first one.'
            primaryAction={{ label: 'New module', onClick: openCreate }}
          />
        ) : (
          <>
            {publishedCount === 0 ? (
              <p className="text-xs text-admin-fg-muted">
                Nothing is published yet — tutors still see an empty training checklist.
              </p>
            ) : null}
            {items.map((row) => (
              <Card key={row.id}>
                <CardContent className="p-4 pt-4">
                  <div className="flex items-start justify-between flex-wrap gap-3">
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap mb-1">
                        <h3 className="font-semibold text-admin-fg-strong truncate">{row.title}</h3>
                        {statusBadge(row.status)}
                        <Badge variant="info" size="sm">{row.stage}</Badge>
                        {row.requiredForCalibration ? (
                          <Badge variant="primary" size="sm">required</Badge>
                        ) : null}
                      </div>
                      {row.contentMarkdown ? (
                        <p className="text-sm text-admin-fg-muted line-clamp-2">
                          {row.contentMarkdown}
                        </p>
                      ) : (
                        <p className="text-sm text-admin-fg-muted italic">No content written yet.</p>
                      )}
                      <p className="text-xs text-admin-fg-muted mt-1">
                        order {row.orderIndex} · updated {formatDate(row.updatedAt)}
                        {row.publishedAt ? ` · published ${formatDate(row.publishedAt)}` : ''}
                      </p>
                    </div>
                    <div className="flex items-center gap-1 flex-wrap">
                      <Button
                        size="sm"
                        variant="secondary"
                        onClick={() => openEdit(row)}
                        disabled={busyId === row.id}
                      >
                        Edit
                      </Button>
                      {row.status !== 'Published' ? (
                        <Button
                          size="sm"
                          variant="primary"
                          onClick={() => void handlePublish(row)}
                          disabled={busyId === row.id}
                        >
                          <Send className="h-4 w-4 mr-1" /> Publish
                        </Button>
                      ) : null}
                      {row.status !== 'Archived' ? (
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => void handleArchive(row)}
                          disabled={busyId === row.id}
                        >
                          <Archive className="h-4 w-4 mr-1" /> Archive
                        </Button>
                      ) : null}
                    </div>
                  </div>
                </CardContent>
              </Card>
            ))}
          </>
        )}
      </AdminCatalogLayout>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? `Edit "${editing.title}"` : 'New training module'}</DialogTitle>
          </DialogHeader>
          <form className="space-y-3" onSubmit={(e) => void handleSave(e)}>
            <Input
              label="Title"
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              placeholder='e.g. "Running the role-play: pacing and prompts"'
              required
              maxLength={200}
            />
            <div className="grid grid-cols-2 gap-3">
              <NativeSelect
                label="Stage"
                value={form.stage}
                onChange={(e) => setForm({ ...form, stage: e.target.value as InterlocutorTrainingStage })}
                options={STAGE_OPTIONS}
                hint="Onboarding runs before the first live room; Refresher is periodic."
              />
              <Input
                type="number"
                label="Order index (lower shows first)"
                value={String(form.orderIndex)}
                onChange={(e) => setForm({ ...form, orderIndex: Number.parseInt(e.target.value || '0', 10) || 0 })}
              />
            </div>
            <Textarea
              label="Content (markdown)"
              value={form.contentMarkdown}
              onChange={(e) => setForm({ ...form, contentMarkdown: e.target.value })}
              rows={10}
              hint="Rendered as markdown in the tutor training checklist."
            />
            <label className="inline-flex items-start gap-2 text-sm text-admin-fg-default">
              <Checkbox
                checked={form.requiredForCalibration}
                onCheckedChange={(v) => setForm({ ...form, requiredForCalibration: v === true })}
              />
              <span>
                Required for calibration
                <span className="block text-xs text-admin-fg-muted">
                  Once published, tutors cannot join live rooms until they complete this module.
                </span>
              </span>
            </label>
            <DialogFooter>
              <Button type="button" variant="secondary" onClick={() => setEditOpen(false)} disabled={saving}>
                <X className="h-4 w-4 mr-1" /> Cancel
              </Button>
              <Button type="submit" variant="primary" disabled={saving}>
                <Save className="h-4 w-4 mr-1" /> {saving ? 'Saving…' : (editing ? 'Update' : 'Create draft')}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}
