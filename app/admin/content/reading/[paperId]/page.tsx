'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  AlertTriangle,
  Archive as ArchiveIcon,
  ArrowLeft,
  BookOpen,
  CheckCircle2,
  Layers,
  RefreshCw,
  RotateCcw,
  Save,
  Sparkles,
  Upload,
} from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Textarea } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminPublishPaperWithWarnings,
  adminUnarchivePaper,
  adminReadingEnsureCanonical,
  adminReadingGetManifest,
  adminReadingGetStructure,
  adminReadingImportManifest,
  adminReadingValidate,
} from '@/lib/api';
import {
  archiveContentPaper,
  getContentPaper,
  type ContentPaperDto,
} from '@/lib/content-upload-api';
import type {
  ReadingPartCode,
  ReadingPartView,
  ReadingStructure,
  ReadingStructureManifest,
  ReadingValidationReport,
} from '@/lib/types/admin/reading-authoring';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

function resolvePaperId(raw: string | string[] | undefined): string | null {
  if (!raw) return null;
  if (Array.isArray(raw)) return raw[0] ?? null;
  return raw;
}

function statusVariant(status: string): 'success' | 'muted' | 'warning' | 'default' {
  if (status === 'Published') return 'success';
  if (status === 'Archived') return 'muted';
  if (status === 'InReview') return 'warning';
  return 'default';
}

const PART_LABELS: Record<ReadingPartCode, string> = {
  A: 'Part A — 20 short-answer (blank fill)',
  B: 'Part B — 6 MCQ (3 options)',
  C: 'Part C — 16 MCQ (4 options)',
};

const EXPECTED_COUNTS: Record<ReadingPartCode, number> = { A: 20, B: 6, C: 16 };

export default function ReadingPaperWorkspacePage() {
  const params = useParams();
  const paperId = resolvePaperId(params?.paperId as string | string[] | undefined);

  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublish = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [structure, setStructure] = useState<ReadingStructure | null>(null);
  const [report, setReport] = useState<ReadingValidationReport | null>(null);
  const [manifestText, setManifestText] = useState<string>('');
  const [manifestReplace, setManifestReplace] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    try {
      const [p, s, r, m] = await Promise.all([
        getContentPaper(paperId),
        adminReadingGetStructure(paperId).catch(() => null),
        adminReadingValidate(paperId).catch(() => null),
        adminReadingGetManifest(paperId).catch(() => null),
      ]);
      setPaper(p);
      setStructure(s);
      setReport(r);
      if (m) setManifestText(JSON.stringify(m, null, 2));
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load paper.' });
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  const partsByCode = useMemo(() => {
    const map = new Map<ReadingPartCode, ReadingPartView>();
    structure?.parts?.forEach((p) => {
      const code = p.partCode as ReadingPartCode;
      if (code === 'A' || code === 'B' || code === 'C') map.set(code, p);
    });
    return map;
  }, [structure]);

  const handleEnsureCanonical = async () => {
    if (!paperId || !canWrite) return;
    setBusy(true);
    try {
      await adminReadingEnsureCanonical(paperId);
      setToast({ variant: 'success', message: 'Canonical Part A / B / C ensured.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Ensure canonical failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleValidate = async () => {
    if (!paperId) return;
    setBusy(true);
    try {
      const r = await adminReadingValidate(paperId);
      setReport(r);
      setToast({ variant: r.isPublishReady ? 'success' : 'error',
        message: r.isPublishReady ? 'Paper is publish-ready.' : `${r.issues.length} validation issue(s).` });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Validate failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handlePublish = async () => {
    if (!paperId || !canPublish) return;
    setBusy(true);
    try {
      const r = await adminPublishPaperWithWarnings(paperId);
      const warn = r.warnings && r.warnings.length > 0 ? ` (warnings: ${r.warnings.length})` : '';
      setToast({ variant: 'success', message: `Published${warn}.` });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleArchive = async () => {
    if (!paperId || !canWrite) return;
    if (!confirm('Archive this reading paper? Learners will no longer see it.')) return;
    setBusy(true);
    try {
      await archiveContentPaper(paperId);
      setToast({ variant: 'success', message: 'Paper archived.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Archive failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleUnarchive = async () => {
    if (!paperId || !canWrite) return;
    setBusy(true);
    try {
      await adminUnarchivePaper(paperId);
      setToast({ variant: 'success', message: 'Paper restored.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Unarchive failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleManifestImport = async () => {
    if (!paperId || !canWrite) return;
    let parsed: ReadingStructureManifest;
    try {
      parsed = JSON.parse(manifestText) as ReadingStructureManifest;
    } catch {
      setToast({ variant: 'error', message: 'Manifest is not valid JSON.' });
      return;
    }
    if (manifestReplace && !confirm('Replace existing reading structure? This deletes all current texts and questions.')) return;
    setBusy(true);
    try {
      const result = await adminReadingImportManifest(paperId, {
        replaceExisting: manifestReplace,
        manifest: parsed,
      });
      setStructure(result.structure);
      setReport(result.report);
      setToast({ variant: 'success', message: 'Manifest imported.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Manifest import failed.' });
    } finally {
      setBusy(false);
    }
  };

  if (!paperId) {
    return (
      <AdminRouteWorkspace role="main">
        <Card className="p-8 text-center text-sm text-muted">Invalid paper id.</Card>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Reading paper workspace">
      <AdminRouteHero
        eyebrow="Reading workspace"
        icon={BookOpen}
        accent="navy"
        title={paper?.title ?? 'Reading paper'}
        description={paper ? `${paper.subtestCode} · ${paper.appliesToAllProfessions ? 'All professions' : paper.professionId ?? 'Unassigned'} · ${paper.difficulty}` : '\u00a0'}
        aside={(
          <div className="flex flex-wrap gap-2">
            <Button variant="ghost" asChild>
              <Link href="/admin/content/reading">
                <ArrowLeft className="mr-1 h-4 w-4" /> Back
              </Link>
            </Button>
            <Button variant="outline" size="sm" onClick={() => void load()}>
              <RotateCcw className="h-4 w-4" />
            </Button>
          </div>
        )}
      />

      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 4 }).map((_, i) => (<Skeleton key={i} className="h-20 rounded-xl" />))}
        </div>
      ) : (
        <>
          {/* Header status panel */}
          <AdminRoutePanel>
            <div className="flex flex-wrap items-center gap-3">
              {paper && (
                <Badge variant={statusVariant(paper.status)}>{paper.status}</Badge>
              )}
              {report && (
                <Badge variant={report.isPublishReady ? 'success' : 'warning'}>
                  {report.isPublishReady ? 'Publish-ready' : `${report.issues.length} issue${report.issues.length === 1 ? '' : 's'}`}
                </Badge>
              )}
              <span className="text-xs text-muted">
                Counts — A: {report?.counts.partACount ?? '—'} / 20 · B: {report?.counts.partBCount ?? '—'} / 6 · C: {report?.counts.partCCount ?? '—'} / 16 · points: {report?.counts.totalPoints ?? '—'}
              </span>
              <div className="ml-auto flex flex-wrap gap-2">
                <Button variant="outline" size="sm" onClick={() => void handleValidate()} disabled={busy}>
                  <CheckCircle2 className="mr-1 h-4 w-4" /> Validate
                </Button>
                {canWrite && (
                  <Button variant="outline" size="sm" onClick={() => void handleEnsureCanonical()} disabled={busy}>
                    <Layers className="mr-1 h-4 w-4" /> Ensure canonical
                  </Button>
                )}
                {canPublish && paper && paper.status !== 'Published' && paper.status !== 'Archived' && (
                  <Button size="sm" onClick={() => void handlePublish()} disabled={busy}>
                    <Sparkles className="mr-1 h-4 w-4" /> Publish
                  </Button>
                )}
                {canPublish && paper && paper.status === 'Published' && (
                  <Button size="sm" variant="outline" onClick={() => void handlePublish()} disabled={busy}>
                    <RefreshCw className="mr-1 h-4 w-4" /> Republish
                  </Button>
                )}
                {canWrite && paper && paper.status === 'Archived' && (
                  <Button size="sm" variant="outline" onClick={() => void handleUnarchive()} disabled={busy}>
                    <RotateCcw className="mr-1 h-4 w-4" /> Unarchive
                  </Button>
                )}
                {canWrite && paper && paper.status !== 'Archived' && (
                  <Button size="sm" variant="outline" onClick={() => void handleArchive()} disabled={busy}>
                    <ArchiveIcon className="mr-1 h-4 w-4" /> Archive
                  </Button>
                )}
              </div>
            </div>
          </AdminRoutePanel>

          {/* Validation issues */}
          {report && report.issues.length > 0 && (
            <Card className="p-4">
              <div className="mb-2 flex items-center gap-2 text-sm font-medium">
                <AlertTriangle className="h-4 w-4 text-amber-600" /> Validation issues
              </div>
              <ul className="space-y-1 text-xs">
                {report.issues.map((iss, i) => (
                  <li key={`${iss.code}-${i}`} className="flex items-start gap-2">
                    <Badge variant={iss.severity === 'error' ? 'danger' : 'warning'}>{iss.severity}</Badge>
                    <span className="font-mono text-[10px] text-muted">{iss.code}</span>
                    <span>{iss.message}</span>
                  </li>
                ))}
              </ul>
            </Card>
          )}

          {/* Parts navigation */}
          <Card className="p-4">
            <div className="mb-3 text-sm font-medium">Parts</div>
            <div className="grid gap-3 sm:grid-cols-3">
              {(['A', 'B', 'C'] as const).map((code) => {
                const part = partsByCode.get(code);
                const qCount = part?.questions.length ?? 0;
                const tCount = part?.texts.length ?? 0;
                const expected = EXPECTED_COUNTS[code];
                const ok = qCount === expected;
                return (
                  <Link
                    key={code}
                    href={`/admin/content/reading/${paperId}/parts/${code}`}
                    className="block rounded-xl border border-border bg-background-light p-4 hover:bg-background-light/60"
                  >
                    <div className="flex items-center justify-between">
                      <div className="text-sm font-semibold">{PART_LABELS[code]}</div>
                      <Badge variant={ok ? 'success' : 'warning'}>{qCount}/{expected}</Badge>
                    </div>
                    <div className="mt-1 text-xs text-muted">
                      {tCount} text{tCount === 1 ? '' : 's'} · time limit {part?.timeLimitMinutes ?? '—'} min
                    </div>
                  </Link>
                );
              })}
            </div>
            <div className="mt-3">
              <Button variant="ghost" size="sm" asChild>
                <Link href={`/admin/content/reading/${paperId}/extractions`}>
                  Open AI extraction queue →
                </Link>
              </Button>
            </div>
          </Card>

          {/* Manifest editor */}
          {canWrite && (
            <Card className="p-4">
              <div className="mb-2 flex items-center justify-between">
                <div className="text-sm font-medium">Structure manifest (JSON)</div>
                <label className="flex items-center gap-2 text-xs">
                  <input
                    type="checkbox"
                    checked={manifestReplace}
                    onChange={(e) => setManifestReplace(e.target.checked)}
                  />
                  Replace existing on import
                </label>
              </div>
              <Textarea
                value={manifestText}
                onChange={(e) => setManifestText(e.target.value)}
                rows={18}
                className="font-mono text-xs"
                spellCheck={false}
              />
              <div className="mt-3 flex justify-end gap-2">
                <Button variant="outline" size="sm" onClick={() => void load()} disabled={busy}>
                  <RotateCcw className="mr-1 h-4 w-4" /> Reload from server
                </Button>
                <Button size="sm" onClick={() => void handleManifestImport()} disabled={busy || !manifestText.trim()}>
                  <Upload className="mr-1 h-4 w-4" /> Import manifest
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    if (!manifestText) return;
                    void navigator.clipboard?.writeText(manifestText).then(() => {
                      setToast({ variant: 'success', message: 'Manifest copied to clipboard.' });
                    });
                  }}
                  disabled={!manifestText}
                >
                  <Save className="mr-1 h-4 w-4" /> Copy
                </Button>
              </div>
            </Card>
          )}
        </>
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
