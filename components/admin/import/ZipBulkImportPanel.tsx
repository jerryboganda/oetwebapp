'use client';

// SUBAGENT_C: Reusable ZIP bulk-import panel.
// Embedded by `/admin/content/papers/import-zip` (dedicated page) AND by the
// reworked `/admin/content/import` page (ZIP tab). All session state is
// internal so both call sites can drop the component without wiring.

import { useCallback, useMemo, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, FileArchive, Loader2, Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input } from '@/components/ui/form-controls';
import { adminCommitZipImport, adminStartZipImport } from '@/lib/api';
import type {
  BulkImportApprovalInput,
  BulkImportCommitResult,
  BulkImportPaperProposal,
  BulkImportSessionResponse,
} from '@/lib/types/admin/bulk-import';

type Phase = 'idle' | 'uploading' | 'staged' | 'committing' | 'done';

interface ApprovalState {
  approve: boolean;
  overrideTitle: string;
  overrideSourceProvenance: string;
}

const ACCEPTED_TYPES = '.zip,application/zip,application/x-zip-compressed';

function buildInitialApprovals(papers: BulkImportPaperProposal[]): Record<string, ApprovalState> {
  const out: Record<string, ApprovalState> = {};
  for (const p of papers) {
    out[p.proposalId] = {
      approve: true,
      overrideTitle: '',
      overrideSourceProvenance: '',
    };
  }
  return out;
}

export function ZipBulkImportPanel({
  onCommitted,
}: {
  onCommitted?: (result: BulkImportCommitResult) => void;
}) {
  const [phase, setPhase] = useState<Phase>('idle');
  const [fileName, setFileName] = useState<string | null>(null);
  const [session, setSession] = useState<BulkImportSessionResponse | null>(null);
  const [approvals, setApprovals] = useState<Record<string, ApprovalState>>({});
  const [commit, setCommit] = useState<BulkImportCommitResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const reset = useCallback(() => {
    setPhase('idle');
    setFileName(null);
    setSession(null);
    setApprovals({});
    setCommit(null);
    setError(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }, []);

  const handleFile = useCallback(async (file: File) => {
    setError(null);
    setCommit(null);
    setFileName(file.name);
    setPhase('uploading');
    try {
      const result = await adminStartZipImport(file);
      setSession(result);
      setApprovals(buildInitialApprovals(result.papers ?? []));
      setPhase('staged');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setPhase('idle');
    }
  }, []);

  const handleCommit = useCallback(async () => {
    if (!session) return;
    setPhase('committing');
    setError(null);
    const payload: BulkImportApprovalInput[] = (session.papers ?? []).map((p) => {
      const a = approvals[p.proposalId];
      return {
        proposalId: p.proposalId,
        approve: a?.approve ?? false,
        overrideTitle: a?.overrideTitle?.trim() || null,
        overrideSourceProvenance: a?.overrideSourceProvenance?.trim() || null,
      };
    });
    try {
      const result = await adminCommitZipImport(session.sessionId, payload);
      setCommit(result);
      setPhase('done');
      setToast({
        variant: result.warnings.length > 0 ? 'error' : 'success',
        message: `Imported ${result.createdPaperCount} paper${result.createdPaperCount === 1 ? '' : 's'}, ${result.createdAssetCount} asset${result.createdAssetCount === 1 ? '' : 's'}.`,
      });
      onCommitted?.(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setPhase('staged');
    }
  }, [approvals, onCommitted, session]);

  const approvedCount = useMemo(
    () => Object.values(approvals).filter((a) => a.approve).length,
    [approvals],
  );

  const columns: Column<BulkImportPaperProposal>[] = useMemo(
    () => [
      {
        key: 'approve',
        header: 'Import',
        render: (p) => (
          <input
            type="checkbox"
            aria-label={`Import ${p.title}`}
            checked={approvals[p.proposalId]?.approve ?? false}
            onChange={(e) =>
              setApprovals((prev) => ({
                ...prev,
                [p.proposalId]: {
                  ...(prev[p.proposalId] ?? { approve: false, overrideTitle: '', overrideSourceProvenance: '' }),
                  approve: e.target.checked,
                },
              }))
            }
          />
        ),
      },
      {
        key: 'title',
        header: 'Title',
        render: (p) => (
          <div className="flex flex-col gap-1">
            <span className="text-sm font-medium">{p.title}</span>
            <Input
              placeholder="Override title (optional)"
              value={approvals[p.proposalId]?.overrideTitle ?? ''}
              onChange={(e) =>
                setApprovals((prev) => ({
                  ...prev,
                  [p.proposalId]: {
                    ...(prev[p.proposalId] ?? { approve: true, overrideTitle: '', overrideSourceProvenance: '' }),
                    overrideTitle: e.target.value,
                  },
                }))
              }
            />
          </div>
        ),
      },
      { key: 'subtest', header: 'Subtest', render: (p) => <Badge variant="muted">{p.subtestCode}</Badge> },
      {
        key: 'profession',
        header: 'Profession',
        render: (p) => (
          <span className="text-xs text-muted">
            {p.appliesToAllProfessions ? 'All' : p.professionId ?? '—'}
          </span>
        ),
      },
      {
        key: 'assets',
        header: 'Assets',
        render: (p) => (
          <div className="flex flex-wrap gap-1">
            {p.assets.map((a) => (
              <Badge key={`${p.proposalId}:${a.sourceRelativePath}`} variant="muted" className="text-[10px]">
                {a.role}
                {a.part ? ` · ${a.part}` : ''}
              </Badge>
            ))}
          </div>
        ),
      },
      {
        key: 'source',
        header: 'Source',
        render: (p) => (
          <Input
            placeholder={p.sourceProvenance ?? 'Override source (optional)'}
            value={approvals[p.proposalId]?.overrideSourceProvenance ?? ''}
            onChange={(e) =>
              setApprovals((prev) => ({
                ...prev,
                [p.proposalId]: {
                  ...(prev[p.proposalId] ?? { approve: true, overrideTitle: '', overrideSourceProvenance: '' }),
                  overrideSourceProvenance: e.target.value,
                },
              }))
            }
          />
        ),
      },
    ],
    [approvals],
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <label className="inline-flex">
          <input
            ref={fileInputRef}
            type="file"
            accept={ACCEPTED_TYPES}
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void handleFile(f);
            }}
            disabled={phase === 'uploading' || phase === 'committing'}
          />
          <Button variant="primary" size="sm" disabled={phase === 'uploading' || phase === 'committing'} asChild>
            <span>
              {phase === 'uploading' ? (
                <>
                  <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Uploading…
                </>
              ) : (
                <>
                  <Upload className="mr-1 h-4 w-4" /> Choose ZIP
                </>
              )}
            </span>
          </Button>
        </label>
        {fileName && (
          <span className="inline-flex items-center gap-1 text-xs text-muted">
            <FileArchive className="h-4 w-4" /> {fileName}
          </span>
        )}
        {session && phase !== 'idle' && (
          <Button variant="outline" size="sm" onClick={reset} disabled={phase === 'committing'}>
            Start over
          </Button>
        )}
      </div>

      {error && (
        <InlineAlert variant="error" title="Bulk import failed">
          {error}
        </InlineAlert>
      )}

      {session && (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-3 text-xs text-muted">
            <span>Session: <code className="font-mono">{session.sessionId}</code></span>
            <span>Expires: {new Date(session.expiresAt).toLocaleString()}</span>
            <span>Papers detected: {session.papers?.length ?? 0}</span>
            <span>Approved for import: {approvedCount}</span>
          </div>

          {(session.issues?.length ?? 0) > 0 && (
            <InlineAlert variant="warning" title="Manifest issues">
              <ul className="list-disc pl-5">
                {session.issues.map((issue, idx) => (
                  <li key={idx}>{issue}</li>
                ))}
              </ul>
            </InlineAlert>
          )}

          <DataTable
            columns={columns}
            data={session.papers ?? []}
            keyExtractor={(p) => p.proposalId}
          />

          <div className="flex items-center gap-2">
            <Button
              variant="primary"
              onClick={handleCommit}
              disabled={phase === 'committing' || approvedCount === 0}
            >
              {phase === 'committing' ? (
                <>
                  <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Committing…
                </>
              ) : (
                <>
                  <CheckCircle2 className="mr-1 h-4 w-4" /> Commit {approvedCount} paper{approvedCount === 1 ? '' : 's'}
                </>
              )}
            </Button>
          </div>
        </div>
      )}

      {commit && (
        <InlineAlert
          variant={commit.warnings.length > 0 ? 'warning' : 'success'}
          title="Bulk import completed"
        >
          <div className="flex flex-col gap-1 text-sm">
            <span>Created: {commit.createdPaperCount} papers · {commit.createdAssetCount} assets</span>
            {commit.deduplicatedAssetCount > 0 && (
              <span>Deduplicated assets: {commit.deduplicatedAssetCount}</span>
            )}
            {commit.warnings.length > 0 && (
              <div className="mt-2 flex items-start gap-1 text-warning">
                <AlertCircle className="mt-0.5 h-4 w-4" />
                <ul className="list-disc pl-5">
                  {commit.warnings.map((w, idx) => (
                    <li key={idx}>{w}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </InlineAlert>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </div>
  );
}
