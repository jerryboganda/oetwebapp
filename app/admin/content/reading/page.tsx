'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  Archive as ArchiveIcon,
  CheckCircle2,
  BookOpen,
  Plus,
  RotateCcw,
} from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminBulkPublishPapers,
  adminBulkSetPaperStatus,
  adminPublishPaperWithWarnings,
  adminUnarchivePaper,
} from '@/lib/api';
import {
  archiveContentPaper,
  listContentPapers,
  type ContentPaperDto,
  type ContentStatus,
} from '@/lib/content-upload-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUS_OPTIONS = [
  { value: '', label: 'Any status' },
  { value: 'Draft', label: 'Draft' },
  { value: 'InReview', label: 'In review' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const PROFESSION_OPTIONS = [
  { value: '', label: 'Any profession' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'optometry', label: 'Optometry' },
];

function statusVariant(status: ContentStatus | string): 'success' | 'muted' | 'warning' | 'default' {
  if (status === 'Published') return 'success';
  if (status === 'Archived') return 'muted';
  if (status === 'InReview') return 'warning';
  return 'default';
}

export default function AdminReadingPapersPage() {
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublish = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [rows, setRows] = useState<ContentPaperDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);
  const [status, setStatus] = useState('');
  const [profession, setProfession] = useState('');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await listContentPapers({
        subtest: 'reading',
        status: status || undefined,
        profession: profession || undefined,
        search: search || undefined,
        pageSize: 100,
      });
      setRows(Array.isArray(res) ? res : []);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load reading papers.' });
    } finally {
      setLoading(false);
    }
  }, [status, profession, search]);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  const draftRows = useMemo(() => rows.filter((r) => r.status === 'Draft'), [rows]);

  const toggleSelected = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const selectAllDrafts = () => setSelected(new Set(draftRows.map((r) => r.id)));
  const clearSelection = () => setSelected(new Set());

  const handlePublish = async (id: string) => {
    if (!canPublish) return;
    try {
      const r = await adminPublishPaperWithWarnings(id);
      const warn = r.warnings && r.warnings.length > 0 ? ` (warnings: ${r.warnings.length})` : '';
      setToast({ variant: 'success', message: `Published${warn}.` });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish failed.' });
    }
  };

  const handleArchive = async (id: string) => {
    if (!canWrite) return;
    if (!confirm('Archive this reading paper? Learners will no longer see it.')) return;
    try {
      await archiveContentPaper(id);
      setToast({ variant: 'success', message: 'Paper archived.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Archive failed.' });
    }
  };

  const handleUnarchive = async (id: string) => {
    if (!canWrite) return;
    try {
      await adminUnarchivePaper(id);
      setToast({ variant: 'success', message: 'Paper restored.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Unarchive failed.' });
    }
  };

  const handleBulkPublish = async () => {
    if (!canPublish || selected.size === 0) return;
    setBulkBusy(true);
    try {
      const res = await adminBulkPublishPapers(Array.from(selected));
      const ok = res.results.filter((r) => r.ok).length;
      const failed = res.results.length - ok;
      setToast({
        variant: failed === 0 ? 'success' : 'error',
        message: `Bulk publish: ${ok} ok, ${failed} failed.`,
      });
      clearSelection();
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Bulk publish failed.' });
    } finally {
      setBulkBusy(false);
    }
  };

  const handleBulkArchive = async () => {
    if (!canWrite || selected.size === 0) return;
    if (!confirm(`Archive ${selected.size} reading paper(s)?`)) return;
    setBulkBusy(true);
    try {
      const res = await adminBulkSetPaperStatus(Array.from(selected), 'Archived');
      const ok = res.results.filter((r) => r.ok).length;
      const failed = res.results.length - ok;
      setToast({
        variant: failed === 0 ? 'success' : 'error',
        message: `Bulk archive: ${ok} ok, ${failed} failed.`,
      });
      clearSelection();
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Bulk archive failed.' });
    } finally {
      setBulkBusy(false);
    }
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Reading CMS">
      <AdminRouteHero
        eyebrow="CMS"
        icon={BookOpen}
        accent="navy"
        title="Reading CMS"
        description="Manage reading papers — Part A (20 short-answer), Part B (6 MCQ-3), Part C (16 MCQ-4)."
        aside={canWrite ? (
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap gap-2">
              <Button className="inline-flex items-center gap-2" asChild>
                <Link href="/admin/content/reading/new">
                  <Plus className="h-4 w-4" /> New reading paper
                </Link>
              </Button>
            </div>
          </div>
        ) : undefined}
      />

      <AdminRoutePanel>
        <div className="grid gap-3 sm:grid-cols-4">
          <Select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            label="Status"
            options={STATUS_OPTIONS}
          />
          <Select
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            label="Profession"
            options={PROFESSION_OPTIONS}
          />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            label="Search"
            placeholder="Title or slug"
          />
          <div className="flex items-end">
            <Button
              variant="outline"
              size="sm"
              onClick={() => void load()}
              className="inline-flex items-center gap-2"
            >
              <RotateCcw className="h-4 w-4" /> Reload
            </Button>
          </div>
        </div>
      </AdminRoutePanel>

      {(canPublish || canWrite) && selected.size > 0 && (
        <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-background-light px-4 py-3 text-sm">
          <span className="font-medium">Bulk actions:</span>
          <span className="text-muted">{selected.size} selected</span>
          {canPublish && draftRows.length > 0 && (
            <Button variant="ghost" size="sm" onClick={selectAllDrafts}>
              Select all drafts ({draftRows.length})
            </Button>
          )}
          <Button variant="ghost" size="sm" onClick={clearSelection}>Clear</Button>
          {canPublish && (
            <Button
              size="sm"
              disabled={selected.size === 0 || bulkBusy}
              onClick={() => void handleBulkPublish()}
              className="inline-flex items-center gap-2"
            >
              <CheckCircle2 className="h-4 w-4" /> Publish selected
            </Button>
          )}
          {canWrite && (
            <Button
              size="sm"
              variant="outline"
              disabled={selected.size === 0 || bulkBusy}
              onClick={() => void handleBulkArchive()}
              className="inline-flex items-center gap-2"
            >
              <ArchiveIcon className="h-4 w-4" /> Archive selected
            </Button>
          )}
        </div>
      )}

      <div className="text-sm text-muted">{rows.length} paper{rows.length === 1 ? '' : 's'}</div>

      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-14 rounded-xl" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <Card className="p-8 text-center text-sm text-muted">No reading papers match these filters.</Card>
      ) : (
        <Card className="overflow-hidden">
          <table className="min-w-full text-sm">
            <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
              <tr>
                {(canPublish || canWrite) && <th className="px-3 py-2 w-8" aria-label="Select" />}
                <th className="px-4 py-2">Title</th>
                <th className="px-4 py-2">Profession</th>
                <th className="px-4 py-2">Difficulty</th>
                <th className="px-4 py-2">Status</th>
                <th className="px-4 py-2">Updated</th>
                <th className="px-4 py-2 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr key={p.id} className="border-t border-border hover:bg-background-light/40">
                  {(canPublish || canWrite) && (
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        aria-label={`Select ${p.title}`}
                        checked={selected.has(p.id)}
                        onChange={() => toggleSelected(p.id)}
                      />
                    </td>
                  )}
                  <td className="px-4 py-2">
                    <Link
                      href={`/admin/content/reading/${p.id}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {p.title}
                    </Link>
                    <div className="font-mono text-[10px] text-muted">{p.slug}</div>
                  </td>
                  <td className="px-4 py-2 text-xs capitalize">
                    {p.appliesToAllProfessions ? <Badge variant="muted">All</Badge> : p.professionId ?? '—'}
                  </td>
                  <td className="px-4 py-2 text-xs capitalize">{p.difficulty}</td>
                  <td className="px-4 py-2">
                    <Badge variant={statusVariant(p.status)}>{p.status}</Badge>
                  </td>
                  <td className="px-4 py-2 text-xs">{new Date(p.updatedAt).toLocaleString()}</td>
                  <td className="px-4 py-2 text-right">
                    <div className="flex justify-end gap-1">
                      <Button variant="ghost" size="sm" asChild>
                        <Link href={`/admin/content/reading/${p.id}`}>Workspace</Link>
                      </Button>
                      {canPublish && p.status !== 'Published' && p.status !== 'Archived' && (
                        <Button variant="ghost" size="sm" onClick={() => void handlePublish(p.id)}>
                          Publish
                        </Button>
                      )}
                      {canWrite && p.status === 'Archived' && (
                        <Button variant="ghost" size="sm" onClick={() => void handleUnarchive(p.id)}>
                          Unarchive
                        </Button>
                      )}
                      {canWrite && p.status !== 'Archived' && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => void handleArchive(p.id)}
                          aria-label={`Archive ${p.title}`}
                        >
                          <ArchiveIcon className="h-3.5 w-3.5" />
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
    </AdminRouteWorkspace>
  );
}
