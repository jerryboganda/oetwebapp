'use client';

// SUBAGENT_C: Bulk rulebook JSON import. Replaces the manual
// `seed-rulebooks.mjs` workflow with a drag-drop UI that previews each
// rulebook before posting it to `POST /v1/admin/rulebooks/import`, and
// surfaces a one-click "Publish" affordance per imported rulebook
// (`POST /v1/admin/rulebooks/{id}/publish`).

import { useCallback, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { AlertCircle, CheckCircle2, FileJson, Loader2, Upload, X } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
import {
  adminImportRulebook,
  adminPublishRulebook,
  type AdminRulebookDetail,
} from '@/lib/api';
import type {
  RulebookImportMode,
  RulebookImportPreview,
} from '@/lib/types/admin/bulk-import';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

interface ImportedRulebookRow {
  filename: string;
  detail: AdminRulebookDetail;
  publishState: 'idle' | 'publishing' | 'published' | 'error';
  publishError?: string;
}

function previewFromJson(filename: string, rawJson: string): RulebookImportPreview {
  try {
    const parsed = JSON.parse(rawJson) as Record<string, unknown>;
    const kind = String(parsed.kind ?? '');
    const profession = String(parsed.profession ?? '');
    const version = String(parsed.version ?? '');
    const sections = Array.isArray((parsed as Record<string, unknown>).sections)
      ? ((parsed as { sections: unknown[] }).sections).length
      : 0;
    const rules = Array.isArray((parsed as Record<string, unknown>).rules)
      ? ((parsed as { rules: unknown[] }).rules).length
      : 0;
    if (!kind || !profession || !version) {
      return {
        filename,
        parsed,
        kind,
        profession,
        version,
        sectionsCount: sections,
        rulesCount: rules,
        rawJson,
        error: 'Missing required fields: kind / profession / version.',
      };
    }
    return {
      filename,
      parsed,
      kind,
      profession,
      version,
      sectionsCount: sections,
      rulesCount: rules,
      rawJson,
    };
  } catch (err) {
    return {
      filename,
      parsed: null,
      kind: '',
      profession: '',
      version: '',
      sectionsCount: 0,
      rulesCount: 0,
      rawJson,
      error: err instanceof Error ? err.message : 'Invalid JSON.',
    };
  }
}

export default function AdminRulebookBulkImportPage() {
  const { isAuthenticated } = useAdminAuth();
  const fileRef = useRef<HTMLInputElement>(null);
  const [previews, setPreviews] = useState<RulebookImportPreview[]>([]);
  const [mode, setMode] = useState<RulebookImportMode>('create');
  const [importing, setImporting] = useState(false);
  const [imported, setImported] = useState<ImportedRulebookRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [dragging, setDragging] = useState(false);

  const validPreviews = useMemo(() => previews.filter((p) => !p.error), [previews]);

  const ingestFiles = useCallback(async (files: FileList | File[]) => {
    setError(null);
    const list = Array.from(files).filter((f) => f.name.toLowerCase().endsWith('.json'));
    if (list.length === 0) {
      setError('Drop one or more `.json` rulebook files.');
      return;
    }
    const parsed = await Promise.all(
      list.map(async (file) => previewFromJson(file.name, await file.text())),
    );
    setPreviews((prev) => [...prev, ...parsed]);
  }, []);

  const handlePickFiles = useCallback(
    async (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = e.target.files;
      if (files) await ingestFiles(files);
      if (fileRef.current) fileRef.current.value = '';
    },
    [ingestFiles],
  );

  const handleDrop = useCallback(
    async (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      setDragging(false);
      if (e.dataTransfer?.files?.length) {
        await ingestFiles(e.dataTransfer.files);
      }
    },
    [ingestFiles],
  );

  const removePreview = (idx: number) =>
    setPreviews((prev) => prev.filter((_, i) => i !== idx));

  const importAll = useCallback(async () => {
    if (validPreviews.length === 0) return;
    setImporting(true);
    setError(null);
    const results: ImportedRulebookRow[] = [];
    const failures: string[] = [];
    for (const preview of validPreviews) {
      try {
        const detail = await adminImportRulebook(preview.rawJson, mode);
        results.push({ filename: preview.filename, detail, publishState: 'idle' });
      } catch (err) {
        failures.push(`${preview.filename}: ${err instanceof Error ? err.message : String(err)}`);
      }
    }
    setImported((prev) => [...results, ...prev]);
    setPreviews((prev) => prev.filter((p) => p.error)); // keep only invalid previews on screen
    setImporting(false);
    if (failures.length > 0) {
      setError(failures.join('\n'));
    }
    setToast({
      variant: failures.length > 0 ? 'error' : 'success',
      message: `Imported ${results.length} rulebook${results.length === 1 ? '' : 's'}${failures.length ? `, ${failures.length} failed` : ''}.`,
    });
  }, [mode, validPreviews]);

  const publish = useCallback(async (idx: number) => {
    setImported((prev) =>
      prev.map((row, i) => (i === idx ? { ...row, publishState: 'publishing', publishError: undefined } : row)),
    );
    try {
      const id = imported[idx].detail.id;
      const updated = await adminPublishRulebook(id);
      setImported((prev) =>
        prev.map((row, i) =>
          i === idx ? { ...row, detail: updated, publishState: 'published' } : row,
        ),
      );
      setToast({ variant: 'success', message: 'Rulebook published.' });
    } catch (err) {
      setImported((prev) =>
        prev.map((row, i) =>
          i === idx
            ? { ...row, publishState: 'error', publishError: err instanceof Error ? err.message : String(err) }
            : row,
        ),
      );
      setToast({ variant: 'error', message: 'Publish failed. See row for details.' });
    }
  }, [imported]);

  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        title="Bulk rulebook import"
        description="Drag-drop one or more rulebook JSON files, preview each, then import + publish in one place."
      />

      <AdminRoutePanel title="Stage rulebook JSON">
        <div
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={handleDrop}
          className={`flex flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed p-6 text-sm transition-colors ${
            dragging ? 'border-primary bg-primary/5' : 'border-border bg-background-light'
          }`}
        >
          <FileJson className="h-8 w-8 text-muted" />
          <span className="text-muted">Drop rulebook .json files here, or</span>
          <label className="inline-flex">
            <input
              ref={fileRef}
              type="file"
              accept=".json,application/json"
              multiple
              className="hidden"
              onChange={handlePickFiles}
            />
            <Button variant="primary" size="sm" asChild>
              <span>
                <Upload className="mr-1 h-4 w-4" /> Choose files
              </span>
            </Button>
          </label>
        </div>

        {error && (
          <InlineAlert variant="error" title="Some imports failed" className="mt-4">
            <pre className="whitespace-pre-wrap text-xs">{error}</pre>
          </InlineAlert>
        )}

        {previews.length > 0 && (
          <div className="mt-4 flex flex-col gap-2">
            <div className="flex flex-wrap items-center gap-3">
              <Select
                aria-label="Import mode"
                value={mode}
                onChange={(e) => setMode((e.target.value as RulebookImportMode) ?? 'create')}
                options={[
                  { value: 'create', label: 'Create (skip if version exists)' },
                  { value: 'replace', label: 'Replace (overwrite same kind/profession/version)' },
                ]}
              />
              <Button
                variant="primary"
                size="sm"
                onClick={importAll}
                disabled={importing || validPreviews.length === 0}
              >
                {importing ? (
                  <>
                    <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Importing…
                  </>
                ) : (
                  <>Import {validPreviews.length} valid file{validPreviews.length === 1 ? '' : 's'}</>
                )}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPreviews([])}
                disabled={importing}
              >
                Clear queue
              </Button>
            </div>

            <ul className="flex flex-col divide-y divide-border rounded-2xl border border-border">
              {previews.map((p, idx) => (
                <li key={`${p.filename}:${idx}`} className="flex items-start justify-between gap-3 p-3">
                  <div className="flex flex-col gap-1">
                    <div className="flex items-center gap-2">
                      <FileJson className="h-4 w-4 text-muted" />
                      <span className="text-sm font-medium">{p.filename}</span>
                      {p.error ? (
                        <Badge variant="danger">Invalid</Badge>
                      ) : (
                        <Badge variant="muted">{p.kind} · {p.profession} · v{p.version}</Badge>
                      )}
                    </div>
                    {p.error ? (
                      <span className="flex items-center gap-1 text-xs text-danger">
                        <AlertCircle className="h-3.5 w-3.5" /> {p.error}
                      </span>
                    ) : (
                      <span className="text-xs text-muted">
                        Sections: {p.sectionsCount} · Rules: {p.rulesCount}
                      </span>
                    )}
                  </div>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => removePreview(idx)}
                    aria-label={`Remove ${p.filename}`}
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </li>
              ))}
            </ul>
          </div>
        )}
      </AdminRoutePanel>

      <AdminRoutePanel
        title="Imported rulebooks"
      >
        {imported.length === 0 ? (
          <p className="text-sm text-muted">No rulebooks imported yet in this session.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border">
            {imported.map((row, idx) => (
              <li key={row.detail.id} className="flex items-start justify-between gap-3 py-3">
                <div className="flex flex-col gap-1">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium">
                      {row.detail.kind} / {row.detail.profession}
                    </span>
                    <Badge variant="muted">v{row.detail.version}</Badge>
                    <Badge variant={row.detail.status === 'Published' ? 'default' : 'muted'}>
                      {row.detail.status}
                    </Badge>
                  </div>
                  <span className="text-xs text-muted">From: {row.filename}</span>
                  {row.publishError && (
                    <span className="flex items-center gap-1 text-xs text-danger">
                      <AlertCircle className="h-3.5 w-3.5" /> {row.publishError}
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  <Link
                    href={`/admin/rulebooks/${row.detail.id}`}
                    className="text-xs underline text-primary"
                  >
                    Open
                  </Link>
                  {row.detail.status === 'Published' || row.publishState === 'published' ? (
                    <Badge variant="muted" className="text-[10px]">
                      <CheckCircle2 className="mr-1 h-3 w-3 inline" /> Published
                    </Badge>
                  ) : (
                    <Button
                      variant="primary"
                      size="sm"
                      onClick={() => publish(idx)}
                      disabled={row.publishState === 'publishing'}
                    >
                      {row.publishState === 'publishing' ? (
                        <>
                          <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Publishing…
                        </>
                      ) : (
                        'Publish'
                      )}
                    </Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
