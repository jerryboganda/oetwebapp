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
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AdminRulebooksListPage() {
  const [items, setItems] = useState<AdminRulebookSummary[]>([]);
  const [meta, setMeta] = useState<AdminRulebookMetadata | null>(null);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);
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
      setToast({ variant: 'error', message: (e as Error).message || 'Failed to load rulebooks.' });
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

  const kindOptions = useMemo(
    () => [{ value: '', label: 'All' }, ...((meta?.kinds ?? []).map((k) => ({ value: k, label: k })))],
    [meta],
  );
  const professionOptions = useMemo(
    () => [{ value: '', label: 'All' }, ...((meta?.professions ?? []).map((p) => ({ value: p, label: p })))],
    [meta],
  );

  async function handleCreate() {
    if (!createForm.kind || !createForm.profession || !createForm.version.trim()) {
      setToast({ variant: 'error', message: 'Kind, profession, and version are required.' });
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
      setToast({ variant: 'success', message: 'Rulebook created (Draft).' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }

  async function handleFilePick(file: File) {
    try {
      const text = await file.text();
      JSON.parse(text);
      setImportJson(text);
    } catch {
      setToast({ variant: 'error', message: 'Selected file is not valid JSON.' });
    }
  }

  async function handleImport() {
    if (!importJson.trim()) {
      setToast({ variant: 'error', message: 'Paste JSON or pick a file.' });
      return;
    }
    try {
      await adminImportRulebook(importJson, importMode);
      setImportOpen(false);
      setImportJson('');
      setImportMode('create');
      await reload();
      setToast({ variant: 'success', message: 'Rulebook imported.' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Rulebooks">
      <AdminRouteHero
        eyebrow="CMS"
        icon={BookOpen}
        accent="navy"
        title="Rulebooks"
        description="Create, edit, version, publish, import & export grading rules — fully managed from this UI."
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" onClick={() => setImportOpen(true)}>
                <Upload className="h-4 w-4 mr-1" /> Import JSON
              </Button>
              <Button onClick={() => setCreateOpen(true)}>
                <Plus className="h-4 w-4 mr-1" /> New Rulebook
              </Button>
            </div>
          </div>
        )}
      />

      <AdminRoutePanel>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <Select label="Kind" value={kind} onChange={(e) => setKind(e.target.value)} options={kindOptions} />
          <Select label="Profession" value={profession} onChange={(e) => setProfession(e.target.value)} options={professionOptions} />
          <div className="flex items-end">
            <Button onClick={() => void reload()} variant="outline">Refresh</Button>
          </div>
        </div>
      </AdminRoutePanel>

      {loading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}
        </div>
      ) : grouped.length === 0 ? (
        <Card className="p-8 text-center text-gray-500">No rulebooks found. Click <strong>New Rulebook</strong> to create one.</Card>
      ) : (
        <div className="space-y-3">
          {grouped.map(([key, group]) => (
            <Card key={key} className="p-4">
              <div className="flex items-start justify-between flex-wrap gap-3 mb-3">
                <div>
                  <h2 className="font-semibold text-lg capitalize">{key.replace('/', ' · ')}</h2>
                  <p className="text-xs text-gray-500">{group.length} version(s)</p>
                </div>
              </div>
              <div className="space-y-2">
                {group.map((r) => (
                  <Link key={r.id} href={`/admin/rulebooks/${encodeURIComponent(r.id)}`}
                    className="flex items-center justify-between p-3 rounded border hover:bg-gray-50 transition">
                    <div className="flex items-center gap-3 flex-wrap">
                      <span className="font-mono text-sm">{r.version}</span>
                      <Badge variant={r.status === 'Published' ? 'success' : r.status === 'Draft' ? 'muted' : 'outline'}>
                        {r.status}
                      </Badge>
                      <span className="text-xs text-gray-500">{r.sectionCount} sections · {r.ruleCount} rules</span>
                    </div>
                    <span className="text-xs text-gray-400">Updated {new Date(r.updatedAt).toLocaleDateString()}</span>
                  </Link>
                ))}
              </div>
            </Card>
          ))}
        </div>
      )}

      {createOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50" onClick={() => setCreateOpen(false)}>
          <Card className="p-6 max-w-lg w-full" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-bold mb-4">New Rulebook (Draft)</h3>
            <div className="space-y-3">
              <Select label="Kind" value={createForm.kind} onChange={(e) => setCreateForm({ ...createForm, kind: e.target.value })}
                options={[{ value: '', label: 'Select…' }, ...((meta?.kinds ?? []).map((k) => ({ value: k, label: k })))]} />
              <Select label="Profession" value={createForm.profession} onChange={(e) => setCreateForm({ ...createForm, profession: e.target.value })}
                options={[{ value: '', label: 'Select…' }, ...((meta?.professions ?? []).map((p) => ({ value: p, label: p })))]} />
              <Input label="Version label" placeholder="e.g. v1, 2026-q2" value={createForm.version}
                onChange={(e) => setCreateForm({ ...createForm, version: e.target.value })} />
              <Input label="Authority source (optional)" placeholder="e.g. CBLA OET 2.1" value={createForm.authoritySource}
                onChange={(e) => setCreateForm({ ...createForm, authoritySource: e.target.value })} />
            </div>
            <div className="flex justify-end gap-2 mt-6">
              <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
              <Button onClick={handleCreate}>Create Draft</Button>
            </div>
          </Card>
        </div>
      )}

      {importOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50" onClick={() => setImportOpen(false)}>
          <Card className="p-6 max-w-2xl w-full max-h-[90vh] overflow-y-auto" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-bold mb-4">Import Rulebook (JSON)</h3>
            <div className="space-y-3">
              <Select label="Mode" value={importMode}
                onChange={(e) => setImportMode(e.target.value as 'create' | 'replace')}
                options={[
                  { value: 'create', label: 'Create — fail if version exists' },
                  { value: 'replace', label: 'Replace — overwrite existing version' },
                ]} />
              <div>
                <label className="text-sm font-semibold tracking-tight">Pick file</label>
                <input ref={fileRef} type="file" accept="application/json,.json"
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) void handleFilePick(f); }}
                  className="block w-full text-sm mt-1.5 file:mr-3 file:py-2 file:px-3 file:rounded file:border file:bg-white" />
              </div>
              <div>
                <label className="text-sm font-semibold tracking-tight">…or paste JSON</label>
                <textarea rows={12} value={importJson} onChange={(e) => setImportJson(e.target.value)}
                  className="mt-1.5 w-full font-mono text-xs border rounded p-2"
                  placeholder='{ "kind": "speaking", "profession": "medicine", "version": "v1", "sections": [], "rules": [] }' />
              </div>
            </div>
            <div className="flex justify-end gap-2 mt-6">
              <Button variant="outline" onClick={() => setImportOpen(false)}>Cancel</Button>
              <Button onClick={handleImport}><Upload className="h-4 w-4 mr-1" /> Import</Button>
            </div>
          </Card>
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
