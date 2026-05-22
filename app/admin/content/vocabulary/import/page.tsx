'use client';

import { useState, useRef, useEffect } from 'react';
import Link from 'next/link';
import { ArrowLeft, Upload, FileSpreadsheet, AlertTriangle, CheckCircle2, Download, FileText, Copy, FileCheck2, SearchCheck, Tag, RefreshCw, Volume2 } from 'lucide-react';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteSummaryCard,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  previewAdminVocabularyImport,
  bulkImportAdminVocabulary,
  fetchAdminVocabularyImportBatch,
  exportAdminVocabularyImportBatchCsv,
  reconcileAdminVocabularyImportBatch,
  rollbackAdminVocabularyImportBatch,
  backfillAdminVocabularyAudio,
  adminListRecallSetTags,
  type RecallSetTagDto,
} from '@/lib/api';

type PreviewRow = {
  lineNumber: number;
  valid: boolean;
  term: string | null;
  definition: string | null;
  category: string | null;
  difficulty: string | null;
  professionId: string | null;
  americanSpelling: string | null;
  exampleSentence: string | null;
  error: string | null;
};

type PreviewResponse = {
  importBatchId: string;
  totalRows: number;
  validRows: number;
  invalidRows: number;
  duplicateRows: number;
  rows: PreviewRow[];
  warnings: string[];
};

type ImportResponse = {
  importBatchId: string;
  imported: number;
  skipped: number;
  duplicates: number;
  failedRows: number;
  errors?: string[];
};

type BatchSummaryRow = {
  id: string;
  term: string | null;
  audioMediaAssetId: string | null;
  audioUrl: string | null;
};

type BatchSummary = {
  importBatchId: string;
  total: number;
  draft: number;
  active: number;
  archived: number;
  warnings: string[];
  rows?: BatchSummaryRow[];
};

type ReconciliationFieldMismatch = {
  field: string;
  expected: string | null;
  actual: string | null;
};

type ReconciliationRow = {
  lineNumber: number | null;
  key: string;
  status: string;
  mismatches: ReconciliationFieldMismatch[];
  error: string | null;
};

type ReconciliationResponse = {
  importBatchId: string;
  manifestRows: number;
  storedRows: number;
  matchedRows: number;
  missingRows: number;
  extraRows: number;
  mismatchedRows: number;
  invalidManifestRows: number;
  clean: boolean;
  rows: ReconciliationRow[];
};

const SAMPLE_CSV = `Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,ExamTypeCode,IpaPronunciation,AmericanSpelling,ImageUrl,Collocations,RelatedTerms,SourceProvenance
hypertension,"Persistently elevated arterial blood pressure.","He has a long-standing history of hypertension.",medical,medium,medicine,oet,/ˌhaɪpəˈtɛnʃən/,"","","","","src=recalls-src-001;p=4;row=12;quote=hypertension"
dyspnoea,"Difficulty or laboured breathing; shortness of breath.","The patient presented with acute dyspnoea on exertion.",symptoms,medium,medicine,oet,/dɪspˈniːə/,dyspnea,"","dyspnoea on exertion","shortness of breath","src=recalls-src-001;p=4;row=13;quote=dyspnoea"
`;

function makeImportBatchId() {
  const date = new Date().toISOString().slice(0, 10).replaceAll('-', '');
  const suffix = Math.random().toString(36).slice(2, 8);
  return `recalls-${date}-${suffix}`;
}

export default function AdminVocabularyImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [manifestFile, setManifestFile] = useState<File | null>(null);
  const [importBatchId, setImportBatchId] = useState(makeImportBatchId);
  const [preview, setPreview] = useState<PreviewResponse | null>(null);
  const [dryRun, setDryRun] = useState<ImportResponse | null>(null);
  const [committedBatchId, setCommittedBatchId] = useState<string | null>(null);
  const [batchSummary, setBatchSummary] = useState<BatchSummary | null>(null);
  const [reconciliation, setReconciliation] = useState<ReconciliationResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [dryRunning, setDryRunning] = useState(false);
  const [importing, setImporting] = useState(false);
  const [batchActionLoading, setBatchActionLoading] = useState(false);
  const [reconciling, setReconciling] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'warning' | 'error'; message: string } | null>(null);
  const fileInput = useRef<HTMLInputElement | null>(null);
  const manifestInput = useRef<HTMLInputElement | null>(null);

  // Recall-set tag: required for the upload — every imported term is tagged
  // with this practice-collection label so admins can filter/maintain them.
  const [recallSetTags, setRecallSetTags] = useState<RecallSetTagDto[]>([]);
  const [recallSetCode, setRecallSetCode] = useState<string>('');
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const tags = await adminListRecallSetTags();
        if (cancelled) return;
        setRecallSetTags(tags);
        // Default to the highest-priority active tag (lowest sort order).
        if (tags.length > 0 && !recallSetCode) setRecallSetCode(tags[0].code);
      } catch {
        // Non-blocking: user can still type a code manually if list fetch fails.
      }
    })();
    return () => { cancelled = true; };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handlePreview() {
    if (!file) return;
    if (!recallSetCode) {
      setToast({ variant: 'error', message: 'Pick a practice-collection (recall set) label before previewing — it is mandatory for the bulk upload.' });
      return;
    }
    const batchId = importBatchId.trim() || makeImportBatchId();
    setImportBatchId(batchId);
    setLoading(true);
    setPreview(null);
    setDryRun(null);
    setBatchSummary(null);
    try {
      const res = await previewAdminVocabularyImport(file, batchId, recallSetCode);
      const typed = res as PreviewResponse;
      setPreview(typed);
      setImportBatchId(typed.importBatchId);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Preview failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setLoading(false);
    }
  }

  async function handleReconcileBatch() {
    const batchId = committedBatchId ?? importBatchId.trim();
    if (!batchId || !manifestFile) return;
    setReconciling(true);
    setReconciliation(null);
    try {
      const res = await reconcileAdminVocabularyImportBatch(batchId, manifestFile);
      const typed = res as ReconciliationResponse;
      setReconciliation(typed);
      setCommittedBatchId(typed.importBatchId);
      setToast({
        variant: typed.clean ? 'success' : 'warning',
        message: typed.clean
          ? `Reconciliation clean for batch ${typed.importBatchId}.`
          : `Reconciliation needs review: ${typed.mismatchedRows} mismatched, ${typed.missingRows} missing, ${typed.extraRows} extra, ${typed.invalidManifestRows} invalid.`,
      });
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Reconciliation failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setReconciling(false);
    }
  }

  async function handleImport() {
    if (!file || !dryRun || dryRun.failedRows > 0 || dryRun.skipped > 0 || dryRun.imported !== preview?.validRows) return;
    setImporting(true);
    try {
      const res = await bulkImportAdminVocabulary(file, false, importBatchId.trim(), recallSetCode);
      const r = res as ImportResponse;
      if (r.skipped > 0 || r.failedRows > 0 || r.imported !== dryRun.imported) {
        setToast({
          variant: 'error',
          message: `Import needs review: imported ${r.imported}, skipped ${r.skipped}, failed ${r.failedRows}.`,
        });
        return;
      }
      setToast({
        variant: 'success',
        message: `Imported batch ${r.importBatchId}: ${r.imported} row${r.imported === 1 ? '' : 's'}.`,
      });
      setCommittedBatchId(r.importBatchId);
      setFile(null);
      setPreview(null);
      setDryRun(null);
      if (fileInput.current) fileInput.current.value = '';
      await loadBatchSummary(r.importBatchId);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Import failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setImporting(false);
    }
  }

  async function handleDryRun() {
    if (!file || !preview || preview.validRows === 0 || preview.invalidRows > 0) return;
    setDryRunning(true);
    setDryRun(null);
    try {
      const res = await bulkImportAdminVocabulary(file, true, importBatchId.trim(), recallSetCode);
      const typed = res as ImportResponse;
      setDryRun(typed);
      setImportBatchId(typed.importBatchId);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Dry run failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setDryRunning(false);
    }
  }

  async function loadBatchSummary(batchId = committedBatchId ?? importBatchId.trim()) {
    if (!batchId) return;
    setBatchActionLoading(true);
    try {
      const res = await fetchAdminVocabularyImportBatch(batchId);
      setBatchSummary(res as BatchSummary);
      setCommittedBatchId(batchId);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Batch summary failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setBatchActionLoading(false);
    }
  }

  async function handleExportBatch() {
    const batchId = committedBatchId ?? importBatchId.trim();
    if (!batchId) return;
    setBatchActionLoading(true);
    try {
      const blob = await exportAdminVocabularyImportBatchCsv(batchId);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `vocabulary-import-${batchId}-export.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Export failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setBatchActionLoading(false);
    }
  }

  async function handleAudioBackfill() {
    const batchId = committedBatchId ?? importBatchId.trim();
    if (!batchId) return;
    setBatchActionLoading(true);
    try {
      const res = await backfillAdminVocabularyAudio(batchId) as { enqueued?: number; skipped?: string };
      if (res?.skipped) {
        setToast({ variant: 'warning', message: `Audio backfill not available: ${res.skipped}.` });
      } else {
        setToast({ variant: 'success', message: `Queued ${res?.enqueued ?? 0} term${res?.enqueued === 1 ? '' : 's'} for Qwen3 TTS generation.` });
      }
      await loadBatchSummary(batchId);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Audio backfill failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setBatchActionLoading(false);
    }
  }

  async function handleRollbackBatch() {
    const batchId = committedBatchId ?? importBatchId.trim();
    if (!batchId) return;
    const confirmed = window.confirm(`Archive all draft rows from import batch ${batchId}? Active rows will not be changed.`);
    if (!confirmed) return;
    setBatchActionLoading(true);
    try {
      const res = await rollbackAdminVocabularyImportBatch(batchId, false) as { archived: number; blocked: number };
      setToast({ variant: res.blocked > 0 ? 'error' : 'success', message: `Rollback archived ${res.archived}; blocked ${res.blocked}.` });
      await loadBatchSummary(batchId);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Rollback failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setBatchActionLoading(false);
    }
  }

  function downloadSample() {
    const blob = new Blob([SAMPLE_CSV], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'vocabulary-sample.csv';
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  const reconciliationIssueRows = reconciliation?.rows.filter((row) => row.status !== 'matched') ?? [];
  const reconciliationRowsToDisplay = reconciliation
    ? reconciliationIssueRows.length > 0
      ? reconciliationIssueRows
      : reconciliation.rows.slice(0, 50)
    : [];
  const omittedReconciliationRows = reconciliation ? reconciliation.rows.length - reconciliationRowsToDisplay.length : 0;

  return (
    <>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="Import vocabulary (CSV)"
          description="Upload any CSV — only the Term column is required. Re-importing an exported CSV works seamlessly (Id column enables upsert). Header-less CSVs (one term per line) are accepted. Column matching is case-insensitive and accepts aliases (e.g. SynonymsJson = Synonyms = SynonymsCsv). Supported fields: Definition, Category, Difficulty, ExampleSentence, ProfessionId, ExamTypeCode, IpaPronunciation, AmericanSpelling, AudioUrl, AudioSlowUrl, AudioSentenceUrl, AudioMediaAssetId, ImageUrl, ContextNotes, Synonyms, Collocations, RelatedTerms, SourceProvenance. Synonyms/Collocations/RelatedTerms accept JSON arrays, pipe-delimited, semicolon-delimited, or comma-separated values. Delimiter auto-detected (comma, semicolon, or tab). Audio generated by Qwen3 TTS within ~2 min. Duplicates deduplicated; rows with existing Id are updated."
          icon={Upload}
          actions={
            <>
              <Button variant="secondary" size="sm" asChild>
<Link href="/admin/content/vocabulary"><ArrowLeft className="mr-1.5 h-4 w-4" />Back</Link>
</Button>
              <Button variant="secondary" size="sm" onClick={downloadSample}>
                <Download className="mr-1.5 h-4 w-4" /> Sample CSV
              </Button>
            </>
          }
        />

        <AdminRoutePanel>
          <div className="space-y-4">
            {/* Mandatory recall-set picker — every imported term inherits this label. */}
            <div className="rounded-xl border border-primary/30 bg-primary/5 p-4">
              <div className="flex items-center gap-2 text-sm font-semibold text-primary mb-2">
                <Tag className="h-4 w-4" /> Practice-collection label (required)
              </div>
              <p className="text-xs text-muted mb-3">
                Pick the practice-collection (recall set) every term in this CSV belongs to.
                The upload is rejected if this is empty. Add or edit labels in{' '}
                <Link href="/admin/content/vocabulary/recall-set-tags" className="text-primary underline">Recall set tags</Link>.
              </p>
              <select
                value={recallSetCode}
                onChange={(e) => setRecallSetCode(e.target.value)}
                disabled={!!preview || dryRunning || importing}
                required
                aria-required="true"
                className="w-full max-w-md rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:opacity-70"
              >
                <option value="" disabled>— Select a practice-collection label —</option>
                {recallSetTags.map((t) => (
                  <option key={t.code} value={t.code}>{t.displayName} ({t.code})</option>
                ))}
              </select>
              {!recallSetCode ? (
                <p className="mt-2 text-xs text-danger">
                  Selection required. Upload + preview + dry-run + commit are all blocked until you pick a label.
                </p>
              ) : null}
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <label className="flex min-w-64 flex-col gap-1 text-xs font-medium text-admin-text-muted">
                Import batch ID
                <input
                  value={importBatchId}
                  onChange={(e) => { setImportBatchId(e.target.value); setCommittedBatchId(null); setPreview(null); setDryRun(null); setBatchSummary(null); setReconciliation(null); }}
                  disabled={!!preview || dryRunning || importing}
                  maxLength={64}
                  className="rounded-lg border border-border bg-background-light px-3 py-2 font-mono text-xs text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:opacity-70"
                />
              </label>
              <label className="flex min-w-64 flex-col gap-1 text-xs font-medium text-admin-text-muted">
                Source CSV
                <input
                  ref={fileInput}
                  type="file"
                  accept=".csv,text/csv"
                  onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setDryRun(null); setBatchSummary(null); setReconciliation(null); }}
                  className="text-sm text-navy file:mr-3 file:rounded-lg file:border file:border-border file:bg-surface file:px-3 file:py-2 file:text-xs file:font-semibold file:text-navy hover:file:bg-background-light"
                />
              </label>
              <Button variant="secondary" size="sm" disabled={!file || loading} onClick={handlePreview}>
                <FileSpreadsheet className="mr-1.5 h-4 w-4" /> {loading ? 'Checking…' : 'Preview'}
              </Button>
              {preview && preview.validRows > 0 && preview.invalidRows === 0 && (
                <Button variant="secondary" size="sm" disabled={dryRunning} onClick={handleDryRun}>
                  {dryRunning ? 'Running…' : 'Dry run'}
                </Button>
              )}
              {preview && dryRun && dryRun.imported === preview.validRows && dryRun.skipped === 0 && dryRun.failedRows === 0 && (
                <Button variant="primary" size="sm" disabled={importing} onClick={handleImport}>
                  {importing ? 'Importing…' : `Import ${preview.validRows} row${preview.validRows === 1 ? '' : 's'}`}
                </Button>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <Button variant="outline" size="sm" disabled={batchActionLoading || !importBatchId.trim()} onClick={() => void loadBatchSummary()}>
                {batchActionLoading ? 'Checking…' : 'Batch summary'}
              </Button>
              <Button variant="secondary" size="sm" disabled={batchActionLoading || !(committedBatchId ?? importBatchId.trim())} onClick={handleExportBatch}>
                <Download className="mr-1.5 h-4 w-4" /> Export reconciliation CSV
              </Button>
              <Button variant="destructive" size="sm" disabled={batchActionLoading || !(committedBatchId ?? importBatchId.trim())} onClick={handleRollbackBatch}>
                Roll back draft batch
              </Button>
            </div>

            {batchSummary && (() => {
              const summaryRows = batchSummary.rows ?? [];
              const audioReadyCount = summaryRows.filter(r => !!r.audioMediaAssetId).length;
              const knownRowCount = summaryRows.length;
              const audioReady = knownRowCount > 0 && audioReadyCount >= knownRowCount;
              const showAudioBlock = knownRowCount > 0;
              return (
                <div className="space-y-2">
                  <InlineAlert variant={batchSummary.active > 0 ? 'warning' : 'success'}>
                    Batch {batchSummary.importBatchId}: {batchSummary.total} total · {batchSummary.draft} draft · {batchSummary.active} active · {batchSummary.archived} archived.
                    {batchSummary.warnings.length > 0 && ` ${batchSummary.warnings.join(' ')}`}
                  </InlineAlert>
                  {showAudioBlock && (
                    <div className="flex flex-wrap items-center gap-2 rounded-xl border border-border bg-background-light/60 px-3 py-2 text-xs text-navy">
                      <Volume2 className="h-4 w-4 text-primary" />
                      <span className="font-medium">Audio: {audioReadyCount} / {knownRowCount}</span>
                      {audioReady
                        ? <Badge variant="success"><CheckCircle2 className="mr-1 h-3 w-3" />Ready</Badge>
                        : <Badge variant="warning"><RefreshCw className="mr-1 h-3 w-3" />Generating…</Badge>
                      }
                      <Button variant="outline" size="sm" disabled={batchActionLoading} onClick={() => void loadBatchSummary()}>
                        <RefreshCw className="mr-1.5 h-3 w-3" /> Refresh
                      </Button>
                      {!audioReady && (
                        <Button variant="secondary" size="sm" disabled={batchActionLoading} onClick={handleAudioBackfill}>
                          Backfill missing audio
                        </Button>
                      )}
                    </div>
                  )}
                </div>
              );
            })()}

            <div className="rounded-2xl border border-border bg-background-light/70 p-4">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                <label className="flex min-w-64 flex-1 flex-col gap-1 text-xs font-medium text-admin-text-muted">
                  Approved manifest CSV
                  <input
                    ref={manifestInput}
                    type="file"
                    accept=".csv,text/csv"
                    onChange={(e) => { setManifestFile(e.target.files?.[0] ?? null); setReconciliation(null); }}
                    className="text-sm text-navy file:mr-3 file:rounded-lg file:border file:border-border file:bg-surface file:px-3 file:py-2 file:text-xs file:font-semibold file:text-navy hover:file:bg-white"
                  />
                </label>
                <div className="flex flex-wrap items-center gap-2">
                  <Button variant="outline" size="sm" disabled={reconciling || !manifestFile || !(committedBatchId ?? importBatchId.trim())} onClick={handleReconcileBatch}>
                    <SearchCheck className="mr-1.5 h-4 w-4" /> {reconciling ? 'Reconciling…' : 'Reconcile manifest'}
                  </Button>
                  {reconciliation && (
                    <Badge variant={reconciliation.clean ? 'success' : 'warning'}>
                      <FileCheck2 className="mr-1 h-3 w-3" /> {reconciliation.clean ? 'Clean' : 'Needs review'}
                    </Badge>
                  )}
                </div>
              </div>
            </div>

            {reconciliation && (
              <div className="space-y-3">
                <InlineAlert variant={reconciliation.clean ? 'success' : 'warning'}>
                  Reconciliation {reconciliation.clean ? 'clean' : 'needs review'} for batch {reconciliation.importBatchId}: {reconciliation.matchedRows} matched · {reconciliation.mismatchedRows} mismatched · {reconciliation.missingRows} missing · {reconciliation.extraRows} extra · {reconciliation.invalidManifestRows} invalid.
                </InlineAlert>
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <AdminRouteSummaryCard label="Manifest rows" value={reconciliation.manifestRows} icon={<FileText className="h-5 w-5" />} />
                  <AdminRouteSummaryCard label="Stored rows" value={reconciliation.storedRows} icon={<FileSpreadsheet className="h-5 w-5" />} />
                  <AdminRouteSummaryCard label="Matched" value={reconciliation.matchedRows} icon={<CheckCircle2 className="h-5 w-5" />} tone="success" />
                  <AdminRouteSummaryCard label="Needs review" value={reconciliation.mismatchedRows + reconciliation.missingRows + reconciliation.extraRows + reconciliation.invalidManifestRows} icon={<AlertTriangle className="h-5 w-5" />} tone={reconciliation.clean ? 'default' : 'warning'} />
                </div>
                {reconciliationRowsToDisplay.length > 0 && (
                  <div className="overflow-hidden rounded-2xl border border-border bg-surface">
                    <table className="w-full text-sm">
                      <thead className="bg-background-light text-left text-xs text-muted">
                        <tr>
                          <th className="px-3 py-2">Manifest line</th>
                          <th className="px-3 py-2">Status</th>
                          <th className="px-3 py-2">Key</th>
                          <th className="px-3 py-2">Mismatch</th>
                          <th className="px-3 py-2">Error</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {reconciliationRowsToDisplay.map((row, index) => {
                          const statusTone = row.status === 'matched' ? 'success' : row.status.includes('invalid') || row.status.includes('duplicate') ? 'danger' : 'warning';
                          return (
                            <tr key={`${row.key}-${row.lineNumber ?? index}`} className={row.status === 'matched' ? '' : 'bg-warning/10'}>
                              <td className="px-3 py-1.5 text-xs text-muted">{row.lineNumber ?? '—'}</td>
                              <td className="px-3 py-1.5"><Badge variant={statusTone}>{row.status}</Badge></td>
                              <td className="px-3 py-1.5 font-mono text-xs text-navy">{row.key}</td>
                              <td className="px-3 py-1.5 text-xs text-muted">
                                {row.mismatches.length > 0
                                  ? row.mismatches.map((m) => `${m.field}: expected ${m.expected ?? 'blank'} / stored ${m.actual ?? 'blank'}`).join('; ')
                                  : '—'}
                              </td>
                              <td className="px-3 py-1.5 text-xs text-danger">{row.error ?? ''}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                    {omittedReconciliationRows > 0 && (
                      <div className="p-3 text-center text-xs text-muted">
                        {reconciliationIssueRows.length > 0
                          ? `Showing all ${reconciliationIssueRows.length} issue row${reconciliationIssueRows.length === 1 ? '' : 's'}; ${omittedReconciliationRows} matched row${omittedReconciliationRows === 1 ? '' : 's'} hidden.`
                          : `Showing first ${reconciliationRowsToDisplay.length} of ${reconciliation.rows.length} matched reconciliation rows.`}
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}

            {preview && (
              <div className="space-y-3">
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <AdminRouteSummaryCard label="Total rows" value={preview.totalRows} icon={<FileText className="h-5 w-5" />} />
                  <AdminRouteSummaryCard label="Valid" value={preview.validRows} icon={<CheckCircle2 className="h-5 w-5" />} tone="success" />
                  <AdminRouteSummaryCard label="Invalid" value={preview.invalidRows} icon={<AlertTriangle className="h-5 w-5" />} tone={preview.invalidRows > 0 ? 'danger' : 'default'} />
                  <AdminRouteSummaryCard label="Duplicates" value={preview.duplicateRows} icon={<Copy className="h-5 w-5" />} tone={preview.duplicateRows > 0 ? 'warning' : 'default'} />
                </div>

                {preview.warnings.length > 0 && (
                  <InlineAlert variant="warning">
                    <ul className="list-inside list-disc space-y-1 text-sm">
                      {preview.warnings.map((w, i) => (<li key={i}>{w}</li>))}
                    </ul>
                  </InlineAlert>
                )}

                {dryRun && (
                  <InlineAlert variant={dryRun.skipped > 0 || dryRun.failedRows > 0 ? 'warning' : 'success'}>
                    Dry run: {dryRun.imported} importable · {dryRun.skipped} skipped · {dryRun.duplicates} duplicate · {dryRun.failedRows} failed.
                    {(dryRun.skipped > 0 || dryRun.failedRows > 0) && ' Resolve every skipped or failed row before committing.'}
                  </InlineAlert>
                )}

                <div className="overflow-hidden rounded-2xl border border-border">
                  <table className="w-full text-sm">
                    <thead className="bg-background-light text-left text-xs text-muted">
                      <tr>
                        <th className="px-3 py-2">#</th>
                        <th className="px-3 py-2">Status</th>
                        <th className="px-3 py-2">Term</th>
                        <th className="px-3 py-2">Definition</th>
                        <th className="px-3 py-2">American</th>
                        <th className="px-3 py-2">Category</th>
                        <th className="px-3 py-2">Error</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {preview.rows.slice(0, 100).map(r => {
                        const isDuplicateInCsv = !r.valid && !!r.error && r.error.startsWith('duplicate-in-csv');
                        return (
                        <tr key={r.lineNumber} className={!r.valid ? (isDuplicateInCsv ? 'bg-warning/10' : 'bg-danger/10') : ''}>
                          <td className="px-3 py-1.5 text-xs text-muted">{r.lineNumber}</td>
                          <td className="px-3 py-1.5">
                            {r.valid
                              ? <Badge variant="success"><CheckCircle2 className="mr-1 h-3 w-3" />OK</Badge>
                              : isDuplicateInCsv
                                ? <Badge variant="warning" title={r.error ?? undefined}><Copy className="mr-1 h-3 w-3" />Duplicate</Badge>
                                : <Badge variant="danger"><AlertTriangle className="mr-1 h-3 w-3" />Error</Badge>
                            }
                          </td>
                          <td className="px-3 py-1.5 font-medium">{r.term ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs text-muted line-clamp-1 max-w-xs">{r.definition ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs">{r.americanSpelling ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs">{r.category ?? '—'}</td>
                          <td className={`px-3 py-1.5 text-xs ${isDuplicateInCsv ? 'text-warning' : 'text-danger'}`}>{r.error ?? ''}</td>
                        </tr>
                        );
                      })}
                    </tbody>
                  </table>
                  {preview.rows.length > 100 && (
                    <div className="p-3 text-center text-xs text-muted">Showing first 100 of {preview.rows.length} rows.</div>
                  )}
                </div>
              </div>
            )}
          </div>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </>
  );
}
