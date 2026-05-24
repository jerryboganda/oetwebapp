'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Plus, Upload } from 'lucide-react';
import {
  adminListRulebooks,
  adminGetRulebookMetadata,
  adminCreateRulebook,
  adminImportRulebook,
  type AdminRulebookSummary,
  type AdminRulebookMetadata,
} from '@/lib/api';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Toaster, toast as adminToast } from '@/components/admin/ui/toaster';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Label } from '@/components/admin/ui/label';

export default function AdminRulebooksListPage() {
  const [items, setItems] = useState<AdminRulebookSummary[]>([]);
  const [meta, setMeta] = useState<AdminRulebookMetadata | null>(null);
  const [loading, setLoading] = useState(true);
  const [kind, setKind] = useState('');
  const [profession, setProfession] = useState('');

  const [createOpen, setCreateOpen] = useState(false);
  const [createForm, setCreateForm] = useState({ kind: '', profession: '', version: '', authoritySource: '' });

  const [importOpen, setImportOpen] = useState(false);
  const [importJson, setImportJson] = useState('');
  const [importMode, setImportMode] = useState<'create' | 'replace'>('create');
  const fileRef = useRef<HTMLInputElement>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListRulebooks({ kind: kind || undefined, profession: profession || undefined });
      setItems(data || []);
      if (!meta) {
        try { setMeta(await adminGetRulebookMetadata()); } catch { /* non-blocking */ }
      }
    } catch (e) {
      adminToast.error((e as Error).message || 'Failed to load rulebooks.');
    } finally {
      setLoading(false);
    }
  }, [kind, profession, meta]);

  useEffect(() => { queueMicrotask(() => void reload()); }, [reload]);

  const grouped = useMemo(() => {
    const map = new Map<string, AdminRulebookSummary[]>();
    for (const r of items) {
      const k = `${r.kind}/${r.profession}`;
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(r);
    }
    return Array.from(map.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [items]);

  async function handleCreate() {
    if (!createForm.kind || !createForm.profession || !createForm.version.trim()) {
      adminToast.error('Kind, profession, and version are required.');
      return;
    }
    try {
      await adminCreateRulebook({
        kind: createForm.kind,
        profession: createForm.profession,
        version: createForm.version.trim(),
        authoritySource: createForm.authoritySource.trim() || null,
      });
      setCreateOpen(false);
      setCreateForm({ kind: '', profession: '', version: '', authoritySource: '' });
      await reload();
      adminToast.success('Rulebook created (Draft).');
    } catch (e) {
      adminToast.error((e as Error).message);
    }
  }

  async function handleFilePick(file: File) {
    try {
      const text = await file.text();
      JSON.parse(text);
      setImportJson(text);
    } catch {
      adminToast.error('Selected file is not valid JSON.');
    }
  }

  async function handleImport() {
    if (!importJson.trim()) {
      adminToast.error('Paste JSON or pick a file.');
      return;
    }
    try {
      await adminImportRulebook(importJson, importMode);
      setImportOpen(false);
      setImportJson('');
      setImportMode('create');
      await reload();
      adminToast.success('Rulebook imported.');
    } catch (e) {
      adminToast.error((e as Error).message);
    }
  }

  const filters = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1.5 min-w-[160px]">
        <Label htmlFor="filter-kind">Kind</Label>
        <Select value={kind || '__all__'} onValueChange={(v) => setKind(v === '__all__' ? '' : v)}>
          <SelectTrigger id="filter-kind">
            <SelectValue placeholder="All" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="__all__">All</SelectItem>
            {(meta?.kinds ?? []).map((k) => (
              <SelectItem key={k} value={k}>{k}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="flex flex-col gap-1.5 min-w-[160px]">
        <Label htmlFor="filter-profession">Profession</Label>
        <Select value={profession || '__all__'} onValueChange={(v) => setProfession(v === '__all__' ? '' : v)}>
          <SelectTrigger id="filter-profession">
            <SelectValue placeholder="All" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="__all__">All</SelectItem>
            {(meta?.professions ?? []).map((p) => (
              <SelectItem key={p} value={p}>{p}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <Button variant="outline" onClick={() => void reload()}>Refresh</Button>
    </div>
  );

  const actions = (
    <>
      <Button variant="outline" onClick={() => setImportOpen(true)} startIcon={<Upload className="h-4 w-4" />}>
        Import JSON
      </Button>
      <Button onClick={() => setCreateOpen(true)} startIcon={<Plus className="h-4 w-4" />}>
        New Rulebook
      </Button>
    </>
  );

  return (
    <>
      <AdminCatalogLayout
        title="Rulebooks"
        description="Create, edit, version, publish, import & export grading rules — fully managed from this UI."
        eyebrow="CMS"
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Rulebooks' }]}
        actions={actions}
        filters={filters}
        viewMode="list"
        hideViewModeToggle
        itemsClassName="flex flex-col gap-3"
      >
        {loading ? (
          [1, 2, 3].map((i) => <Skeleton key={i} className="h-20 rounded-admin-lg" />)
        ) : grouped.length === 0 ? (
          <Card>
            <CardContent className="p-8">
              <EmptyState
                illustration={<BookOpen aria-hidden="true" />}
                title="No rulebooks yet"
                description="Click New Rulebook to create your first grading rulebook."
                primaryAction={{ label: 'New Rulebook', onClick: () => setCreateOpen(true) }}
              />
            </CardContent>
          </Card>
        ) : (
          grouped.map(([key, group]) => (
            <Card key={key}>
              <CardHeader>
                <div className="min-w-0">
                  <CardTitle className="capitalize">{key.replace('/', ' · ')}</CardTitle>
                  <CardDescription>{group.length} version{group.length === 1 ? '' : 's'}</CardDescription>
                </div>
              </CardHeader>
              <CardContent>
                <div className="space-y-2">
                  {group.map((r) => (
                    <Link
                      key={r.id}
                      href={`/admin/rulebooks/${encodeURIComponent(r.id)}`}
                      className="flex items-center justify-between gap-3 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 transition-colors hover:bg-[var(--admin-state-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
                    >
                      <div className="flex flex-wrap items-center gap-3 min-w-0">
                        <span className="font-mono text-sm text-admin-fg-strong">{r.version}</span>
                        <Badge
                          variant={
                            r.status === 'Published'
                              ? 'success'
                              : r.status === 'Draft'
                                ? 'default'
                                : 'secondary'
                          }
                        >
                          {r.status}
                        </Badge>
                        <span className="text-xs text-admin-fg-muted">
                          {r.sectionCount} sections · {r.ruleCount} rules
                        </span>
                      </div>
                      <span className="text-xs text-admin-fg-muted shrink-0">
                        Updated {new Date(r.updatedAt).toLocaleDateString()}
                      </span>
                    </Link>
                  ))}
                </div>
              </CardContent>
            </Card>
          ))
        )}
      </AdminCatalogLayout>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>New Rulebook (Draft)</DialogTitle>
            <DialogDescription>
              Create a new draft rulebook. You can edit sections and rules after creation.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="create-kind">Kind</Label>
              <Select value={createForm.kind} onValueChange={(v) => setCreateForm({ ...createForm, kind: v })}>
                <SelectTrigger id="create-kind">
                  <SelectValue placeholder="Select…" />
                </SelectTrigger>
                <SelectContent>
                  {(meta?.kinds ?? []).map((k) => (
                    <SelectItem key={k} value={k}>{k}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="create-profession">Profession</Label>
              <Select value={createForm.profession} onValueChange={(v) => setCreateForm({ ...createForm, profession: v })}>
                <SelectTrigger id="create-profession">
                  <SelectValue placeholder="Select…" />
                </SelectTrigger>
                <SelectContent>
                  {(meta?.professions ?? []).map((p) => (
                    <SelectItem key={p} value={p}>{p}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Input
              label="Version label"
              placeholder="e.g. v1, 2026-q2"
              value={createForm.version}
              onChange={(e) => setCreateForm({ ...createForm, version: e.target.value })}
              required
            />
            <Input
              label="Authority source (optional)"
              placeholder="e.g. CBLA OET 2.1"
              value={createForm.authoritySource}
              onChange={(e) => setCreateForm({ ...createForm, authoritySource: e.target.value })}
            />
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button onClick={handleCreate}>Create Draft</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={importOpen} onOpenChange={setImportOpen}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>Import Rulebook (JSON)</DialogTitle>
            <DialogDescription>
              Upload a rulebook export, or paste raw JSON. Choose Replace to overwrite an existing version.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 max-h-[60vh] overflow-y-auto">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="import-mode">Mode</Label>
              <Select value={importMode} onValueChange={(v) => setImportMode(v as 'create' | 'replace')}>
                <SelectTrigger id="import-mode">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="create">Create — fail if version exists</SelectItem>
                  <SelectItem value="replace">Replace — overwrite existing version</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="import-file">Pick file</Label>
              <input
                ref={fileRef}
                id="import-file"
                type="file"
                accept="application/json,.json"
                onChange={(e) => { const f = e.target.files?.[0]; if (f) void handleFilePick(f); }}
                className="block w-full text-sm file:mr-3 file:py-2 file:px-3 file:rounded-admin file:border file:border-admin-border file:bg-admin-bg-subtle file:text-admin-fg-default"
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="import-json">…or paste JSON</Label>
              <textarea
                id="import-json"
                rows={12}
                value={importJson}
                onChange={(e) => setImportJson(e.target.value)}
                placeholder='{ "kind": "speaking", "profession": "medicine", "version": "v1", "sections": [], "rules": [] }'
                className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-xs text-admin-fg-default placeholder:text-admin-fg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setImportOpen(false)}>Cancel</Button>
            <Button onClick={handleImport} startIcon={<Upload className="h-4 w-4" />}>Import</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Toaster />
    </>
  );
}
