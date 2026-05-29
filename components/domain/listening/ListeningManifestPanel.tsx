'use client';

import { useRef, useState } from 'react';
import { Download, Upload, FileJson } from 'lucide-react';

import { SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button, buttonVariants } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Textarea } from '@/components/ui/form-controls';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/admin/ui/alert-dialog';
import { cn } from '@/lib/utils';
import {
  exportListeningManifest,
  importListeningManifest,
  type ListeningStructureImportResultDto,
  type ListeningStructureManifestDto,
} from '@/lib/listening-authoring-api';

interface ListeningManifestPanelProps {
  paperId: string;
  paperTitle: string;
  onImported: () => void;
  onNotify: (variant: 'success' | 'error', message: string) => void;
}

/**
 * WS5 — full-test §19 JSON import / export for Listening. Mirrors
 * `app/admin/content/reading/[paperId]/ReadingManifestPanel.tsx`: paste or
 * drop a manifest, toggle "replace existing", import with a confirm step, and
 * render the publish-gate report (42 questions across A24 / B6 / C12 with 5
 * extracts when complete).
 */
export function ListeningManifestPanel({ paperId, paperTitle, onImported, onNotify }: ListeningManifestPanelProps) {
  const [manifestText, setManifestText] = useState('');
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [importing, setImporting] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [result, setResult] = useState<ListeningStructureImportResultDto | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  async function handleExport() {
    setExporting(true);
    try {
      const manifest = await exportListeningManifest(paperId);
      const blob = new Blob([JSON.stringify(manifest, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      const safeTitle = (paperTitle || 'listening-paper').replace(/[^a-z0-9]+/gi, '-').toLowerCase();
      anchor.href = url;
      anchor.download = `${safeTitle}-manifest.json`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      onNotify('success', 'Manifest downloaded.');
    } catch (err) {
      onNotify('error', err instanceof Error ? err.message : 'Export failed');
    } finally {
      setExporting(false);
    }
  }

  function handleFile(file: File) {
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') setManifestText(reader.result);
    };
    reader.onerror = () => onNotify('error', 'Could not read the selected file.');
    reader.readAsText(file);
  }

  function parseManifest(): ListeningStructureManifestDto | null {
    try {
      const parsed = JSON.parse(manifestText);
      if (parsed && typeof parsed === 'object') {
        const m = parsed as ListeningStructureManifestDto;
        const hasPart =
          Array.isArray(m.partA?.extracts) ||
          Array.isArray(m.partB?.extracts) ||
          Array.isArray(m.partC?.extracts);
        if (hasPart) return m;
      }
    } catch {
      /* fall through */
    }
    return null;
  }

  function requestImport() {
    if (!parseManifest()) {
      onNotify('error', 'Paste or upload a valid §19 manifest (must contain partA, partB, or partC with an "extracts" array).');
      return;
    }
    setConfirmOpen(true);
  }

  async function confirmImport() {
    const manifest = parseManifest();
    setConfirmOpen(false);
    if (!manifest) return;
    setImporting(true);
    setResult(null);
    try {
      const importResult = await importListeningManifest(paperId, manifest, replaceExisting);
      setResult(importResult);
      onNotify('success', 'Manifest imported.');
      onImported();
    } catch (err) {
      onNotify('error', err instanceof Error ? err.message : 'Import failed');
    } finally {
      setImporting(false);
    }
  }

  const counts = result?.report.counts;

  return (
    <SettingsSection
      title="Import / Export test"
      description="Download the full Listening test as a §19 JSON manifest, or import one to seed this paper (Parts A, B, and C in one document)."
      actions={
        <Button
          variant="secondary"
          size="sm"
          onClick={handleExport}
          disabled={exporting}
          startIcon={<Download className="h-4 w-4" />}
        >
          {exporting ? 'Exporting…' : 'Export manifest'}
        </Button>
      }
    >
      <div className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <input
            ref={fileInputRef}
            type="file"
            accept="application/json,.json"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) handleFile(file);
              e.target.value = '';
            }}
          />
          <Button
            variant="ghost"
            size="sm"
            onClick={() => fileInputRef.current?.click()}
            startIcon={<FileJson className="h-4 w-4" />}
          >
            Upload .json
          </Button>
          <label className="flex items-center gap-2 text-sm text-admin-fg-default">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-border text-primary"
              checked={replaceExisting}
              onChange={(e) => setReplaceExisting(e.target.checked)}
            />
            Replace existing test
          </label>
        </div>

        <Textarea
          label="Manifest JSON"
          value={manifestText}
          onChange={(e) => setManifestText(e.target.value)}
          rows={8}
          className="font-mono text-xs"
          placeholder='{ "testTitle": "…", "partA": { "extracts": [ … ] }, "partB": { … }, "partC": { … } }'
        />

        <Button
          variant="primary"
          size="sm"
          onClick={requestImport}
          disabled={importing || !manifestText.trim()}
          startIcon={<Upload className="h-4 w-4" />}
        >
          {importing ? 'Importing…' : 'Import manifest'}
        </Button>

        {result && counts && (
          <div className="rounded-lg border border-admin-border bg-admin-bg-subtle p-3 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={result.report.isPublishReady ? 'success' : 'warning'}>
                {result.report.isPublishReady ? 'Publish ready' : 'Needs review'}
              </Badge>
              <span className="text-admin-fg-muted">
                Part A {counts.partACount} / 24 · Part B {counts.partBCount} / 6 · Part C {counts.partCCount} / 12 ·{' '}
                {counts.totalItems} / 42 questions
              </span>
            </div>
            {result.report.issues.length > 0 && (
              <ul className="mt-2 space-y-1">
                {result.report.issues.slice(0, 8).map((issue, idx) => (
                  <li key={idx} className="text-xs text-admin-fg-muted">
                    <span className="font-mono">{issue.code}</span> — {issue.message}
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Import manifest?</AlertDialogTitle>
            <AlertDialogDescription>
              {replaceExisting
                ? 'This will REPLACE the existing Listening test structure for this paper. Questions and extracts not present in the manifest will be removed. This cannot be undone.'
                : 'This will import the manifest into this paper. If the paper already has an authored structure, enable "Replace existing test" instead.'}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className={cn(replaceExisting && buttonVariants({ variant: 'destructive' }))}
              onClick={confirmImport}
            >
              {replaceExisting ? 'Replace test' : 'Import'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </SettingsSection>
  );
}
