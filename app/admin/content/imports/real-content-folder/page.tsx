'use client';

import { useCallback, useMemo, useRef, useState } from 'react';
import { FolderUp, CheckCircle2, AlertTriangle, Upload as UploadIcon, ArrowRight } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  adminStageRealContentFolder,
  adminCommitRealContentFolder,
  type RealContentStageResultDto,
  type RealContentCommitResultDto,
  type RealContentProposalDto,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AdminRealContentFolderImportPage() {
  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [staging, setStaging] = useState(false);
  const [stageResult, setStageResult] = useState<RealContentStageResultDto | null>(null);
  const [approved, setApproved] = useState<Set<string>>(new Set());
  const [committing, setCommitting] = useState(false);
  const [commitResult, setCommitResult] = useState<RealContentCommitResultDto | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const allSourcePaths = useMemo(
    () => stageResult?.proposals.map((p) => p.sourcePath) ?? [],
    [stageResult],
  );

  const handleStage = useCallback(async () => {
    if (!file) { setToast({ variant: 'error', message: 'Pick a ZIP file first.' }); return; }
    setStaging(true);
    setStageResult(null);
    setCommitResult(null);
    try {
      const result = await adminStageRealContentFolder(file);
      setStageResult(result);
      setApproved(new Set(result.proposals.map((p) => p.sourcePath)));
      setToast({
        variant: 'success',
        message: `Staged ${result.proposals.length} proposals (${result.issues.length} issue(s)).`,
      });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setStaging(false);
    }
  }, [file]);

  function toggleApproval(sourcePath: string) {
    setApproved((prev) => {
      const next = new Set(prev);
      if (next.has(sourcePath)) next.delete(sourcePath);
      else next.add(sourcePath);
      return next;
    });
  }

  function selectAll() { setApproved(new Set(allSourcePaths)); }
  function selectNone() { setApproved(new Set()); }

  const handleCommit = useCallback(async () => {
    if (!stageResult) return;
    if (approved.size === 0) {
      setToast({ variant: 'error', message: 'Tick at least one proposal to commit.' });
      return;
    }
    if (!confirm(`Create ${approved.size} draft row(s) on production? They will be created as Drafts and you'll need to publish them separately.`)) return;
    setCommitting(true);
    try {
      const result = await adminCommitRealContentFolder(stageResult.sessionId, Array.from(approved));
      setCommitResult(result);
      setToast({
        variant: 'success',
        message: `Created ${result.created.length} row(s). ${result.errors.length} error(s).`,
      });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setCommitting(false);
    }
  }, [stageResult, approved]);

  const groupedProposals = useMemo(() => {
    if (!stageResult) return new Map<string, RealContentProposalDto[]>();
    const m = new Map<string, RealContentProposalDto[]>();
    for (const p of stageResult.proposals) {
      if (!m.has(p.target)) m.set(p.target, []);
      m.get(p.target)!.push(p);
    }
    return m;
  }, [stageResult]);

  return (
    <AdminRouteWorkspace role="main" aria-label="Real Content folder import">
      <AdminRouteHero
        eyebrow="CMS"
        icon={FolderUp}
        accent="navy"
        title="Real Content folder importer"
        description="Drop the zipped Project Real Content folder. The system parses Listening/Reading/Writing/Speaking samples, recall PDFs, result-table images, rulebook PDFs, and the Scoring System file — then lets you review + commit them all as Drafts."
        aside={null}
      />

      <AdminRoutePanel>
        <div className="space-y-3">
          <p className="text-sm text-muted">
            Step 1 — zip up your <code>Project Real Content</code> folder, upload it here, review the parsed proposals, then commit. Nothing is auto-published.
          </p>
          <div className="flex flex-col sm:flex-row sm:items-end gap-3">
            <div className="flex-1">
              <label className="block text-sm font-medium mb-1">ZIP file (max 1 GB)</label>
              <input
                ref={fileRef}
                type="file"
                accept=".zip,application/zip,application/x-zip-compressed"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="w-full text-sm"
              />
              {file ? (
                <p className="text-xs text-muted mt-1">
                  {file.name} - {(file.size / 1024 / 1024).toFixed(2)} MB
                </p>
              ) : null}
            </div>
            <Button onClick={() => void handleStage()} disabled={!file || staging}>
              {staging ? 'Staging...' : (<><UploadIcon className="h-4 w-4 mr-1" />Stage proposals</>)}
            </Button>
          </div>
        </div>
      </AdminRoutePanel>

      {staging ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-24" />)}
        </div>
      ) : null}

      {stageResult ? (
        <Card className="p-4 space-y-4">
          <div className="flex items-center justify-between flex-wrap gap-3">
            <div>
              <h2 className="text-lg font-semibold">Proposals ({stageResult.proposals.length})</h2>
              <p className="text-xs text-muted">Session <code>{stageResult.sessionId.slice(0, 12)}...</code> · staged {new Date(stageResult.stagedAt).toLocaleTimeString()}</p>
            </div>
            <div className="flex items-center gap-2 flex-wrap">
              <Button size="sm" variant="outline" onClick={selectAll}>Select all</Button>
              <Button size="sm" variant="outline" onClick={selectNone}>Select none</Button>
              <Button onClick={() => void handleCommit()} disabled={committing || approved.size === 0}>
                {committing ? 'Committing...' : (<>Commit {approved.size} <ArrowRight className="h-4 w-4 ml-1" /></>)}
              </Button>
            </div>
          </div>

          {stageResult.issues.length > 0 ? (
            <div className="rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm">
              <div className="flex items-center gap-2 font-medium mb-1">
                <AlertTriangle className="h-4 w-4" /> Parsing issues
              </div>
              <ul className="list-disc list-inside text-xs space-y-1">
                {stageResult.issues.map((i, idx) => <li key={idx}>{i}</li>)}
              </ul>
            </div>
          ) : null}

          <div className="space-y-4">
            {Array.from(groupedProposals.entries()).map(([target, items]) => (
              <section key={target}>
                <h3 className="font-semibold mb-2 flex items-center gap-2">
                  <Badge variant="outline">{target}</Badge>
                  <span className="text-xs text-muted">({items.length})</span>
                </h3>
                <div className="space-y-2">
                  {items.map((p) => {
                    const isApproved = approved.has(p.sourcePath);
                    return (
                      <label
                        key={p.sourcePath}
                        className={`flex items-start gap-3 p-3 rounded border cursor-pointer ${isApproved ? 'border-primary/40 bg-primary/5' : 'border-border'}`}
                      >
                        <input
                          type="checkbox"
                          checked={isApproved}
                          onChange={() => toggleApproval(p.sourcePath)}
                          className="mt-1"
                        />
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 flex-wrap">
                            <span className="font-medium truncate">{p.title}</span>
                            {p.subtest ? <Badge variant="outline">{p.subtest}</Badge> : null}
                            {p.cardType ? <Badge variant="outline">{p.cardType}</Badge> : null}
                            {p.letterType ? <Badge variant="outline">{p.letterType}</Badge> : null}
                            {p.periodLabel ? <Badge variant="outline">{p.periodLabel}</Badge> : null}
                            {p.sharedResourceKind ? <Badge variant="outline">{p.sharedResourceKind}</Badge> : null}
                            {p.rulebookKind ? <Badge variant="outline">{p.rulebookKind}/{p.rulebookProfession}</Badge> : null}
                          </div>
                          <p className="text-xs text-muted truncate mt-0.5">{p.sourcePath}</p>
                          {p.assets.length > 0 ? (
                            <p className="text-xs text-muted mt-1">
                              Assets: {p.assets.map((a) => `${a.role}${a.part ? ` (${a.part})` : ''}`).join(' · ')}
                            </p>
                          ) : null}
                        </div>
                      </label>
                    );
                  })}
                </div>
              </section>
            ))}
          </div>
        </Card>
      ) : null}

      {commitResult ? (
        <Card className="p-4 space-y-3">
          <h2 className="text-lg font-semibold flex items-center gap-2">
            <CheckCircle2 className="h-5 w-5 text-success" />
            Commit complete
          </h2>
          <div className="text-sm">
            <strong>{commitResult.created.length}</strong> row(s) created as Drafts.
            {commitResult.errors.length > 0 ? <span className="text-danger"> · {commitResult.errors.length} error(s)</span> : null}
          </div>
          {commitResult.created.length > 0 ? (
            <ul className="text-xs space-y-1 max-h-60 overflow-y-auto">
              {commitResult.created.map((c, i) => (
                <li key={i}>
                  <Badge variant="outline">{c.target}</Badge> {c.title} <code className="text-muted">{c.id.slice(0, 12)}...</code>
                </li>
              ))}
            </ul>
          ) : null}
          {commitResult.errors.length > 0 ? (
            <div className="rounded-lg border border-danger/40 bg-danger/10 p-3 text-xs space-y-1">
              {commitResult.errors.map((e, i) => <div key={i}>{e}</div>)}
            </div>
          ) : null}
          <p className="text-xs text-muted">
            Drafts are now in each respective admin page. Open Recalls Library, Result Templates, Speaking Shared Resources, Scoring System, and the per-paper admin pages to publish them.
          </p>
        </Card>
      ) : null}

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}
