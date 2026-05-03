'use client';

import { useState, useRef } from 'react';
import Link from 'next/link';
import { ArrowLeft, Upload, FileSpreadsheet, AlertTriangle, CheckCircle2, Download, FileText, Copy } from 'lucide-react';
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
  rollbackAdminVocabularyImportBatch,
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

type BatchSummary = {
  importBatchId: string;
  total: number;
  draft: number;
  active: number;
  archived: number;
  warnings: string[];
};

const SAMPLE_CSV = `Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,IpaPronunciation,AmericanSpelling,Collocations,RelatedTerms,SourceProvenance
hypertension,"Persistently elevated arterial blood pressure.","He has a long-standing history of hypertension.",medical,medium,medicine,/ˌhaɪpəˈtɛnʃən/,"","","","src=recalls-src-001;p=4;row=12;quote=hypertension"
dyspnoea,"Difficulty or laboured breathing; shortness of breath.","The patient presented with acute dyspnoea on exertion.",symptoms,medium,medicine,/dɪspˈniːə/,dyspnea,"dyspnoea on exertion","shortness of breath","src=recalls-src-001;p=4;row=13;quote=dyspnoea"
`;

function makeImportBatchId() {
  const date = new Date().toISOString().slice(0, 10).replaceAll('-', '');
  const suffix = Math.random().toString(36).slice(2, 8);
  return `recalls-${date}-${suffix}`;
}

export default function AdminVocabularyImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [importBatchId, setImportBatchId] = useState(makeImportBatchId);
  const [preview, setPreview] = useState<PreviewResponse | null>(null);
  const [dryRun, setDryRun] = useState<ImportResponse | null>(null);
  const [committedBatchId, setCommittedBatchId] = useState<string | null>(null);
  const [batchSummary, setBatchSummary] = useState<BatchSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [dryRunning, setDryRunning] = useState(false);
  const [importing, setImporting] = useState(false);
  const [batchActionLoading, setBatchActionLoading] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const fileInput = useRef<HTMLInputElement | null>(null);

  async function handlePreview() {
    if (!file) return;
    const batchId = importBatchId.trim() || makeImportBatchId();
    setImportBatchId(batchId);
    setLoading(true);
    setPreview(null);
    setDryRun(null);
    setBatchSummary(null);
    try {
      const res = await previewAdminVocabularyImport(file, batchId);
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

  async function handleImport() {
    if (!file || !dryRun || dryRun.failedRows > 0 || dryRun.skipped > 0 || dryRun.imported !== preview?.validRows) return;
    setImporting(true);
    try {
      const res = await bulkImportAdminVocabulary(file, false, importBatchId.trim());
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
      const res = await bulkImportAdminVocabulary(file, true, importBatchId.trim());
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

  return (
    <>
      {toast && <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="Import vocabulary (CSV)"
          description="Upload RFC 4180 CSV. Required columns: Term, Definition, Category, Difficulty, SourceProvenance. Optional: ExampleSentence, ProfessionId, IpaPronunciation, AmericanSpelling, AudioUrl, AudioSlowUrl, AudioSentenceUrl, AudioMediaAssetId, ContextNotes, Synonyms/SynonymsCsv, Collocations, RelatedTerms."
          icon={Upload}
          actions={
            <>
              <Link href="/admin/content/vocabulary">
                <Button variant="secondary" size="sm"><ArrowLeft className="mr-1.5 h-4 w-4" />Back</Button>
              </Link>
              <Button variant="secondary" size="sm" onClick={downloadSample}>
                <Download className="mr-1.5 h-4 w-4" /> Sample CSV
              </Button>
            </>
          }
        />

        <AdminRoutePanel>
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-3">
              <label className="flex min-w-64 flex-col gap-1 text-xs font-medium text-muted">
                Import batch ID
                <input
                  value={importBatchId}
                  onChange={(e) => { setImportBatchId(e.target.value); setPreview(null); setDryRun(null); setBatchSummary(null); }}
                  disabled={!!preview || dryRunning || importing}
                  maxLength={64}
                  className="rounded-lg border border-border bg-background-light px-3 py-2 font-mono text-xs text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:opacity-70"
                />
              </label>
              <input
                ref={fileInput}
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setDryRun(null); setBatchSummary(null); }}
                className="text-sm"
              />
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

            {batchSummary && (
              <InlineAlert variant={batchSummary.active > 0 ? 'warning' : 'success'}>
                Batch {batchSummary.importBatchId}: {batchSummary.total} total · {batchSummary.draft} draft · {batchSummary.active} active · {batchSummary.archived} archived.
                {batchSummary.warnings.length > 0 && ` ${batchSummary.warnings.join(' ')}`}
              </InlineAlert>
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
                      {preview.rows.slice(0, 100).map(r => (
                        <tr key={r.lineNumber} className={!r.valid ? 'bg-danger/10' : ''}>
                          <td className="px-3 py-1.5 text-xs text-muted">{r.lineNumber}</td>
                          <td className="px-3 py-1.5">
                            {r.valid
                              ? <Badge variant="success"><CheckCircle2 className="mr-1 h-3 w-3" />OK</Badge>
                              : <Badge variant="danger"><AlertTriangle className="mr-1 h-3 w-3" />Error</Badge>
                            }
                          </td>
                          <td className="px-3 py-1.5 font-medium">{r.term ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs text-muted line-clamp-1 max-w-xs">{r.definition ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs">{r.americanSpelling ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs">{r.category ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs text-danger">{r.error ?? ''}</td>
                        </tr>
                      ))}
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
