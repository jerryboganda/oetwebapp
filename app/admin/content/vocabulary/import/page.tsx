'use client';

import {
    AdminRoutePanel,
    AdminRouteSectionHeader, AdminRouteWorkspace
} from '@/components/domain/admin-route-surface';
import { AdminDashboardShell } from "@/components/layout/admin-dashboard-shell";
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
    bulkImportAdminVocabulary, previewAdminVocabularyImport
} from '@/lib/api';
import { AlertTriangle, ArrowLeft, CheckCircle2, Download, FileSpreadsheet, Upload } from 'lucide-react';
import Link from 'next/link';
import { useRef, useState } from 'react';

type PreviewRow = {
  lineNumber: number;
  valid: boolean;
  term: string | null;
  definition: string | null;
  category: string | null;
  difficulty: string | null;
  professionId: string | null;
  exampleSentence: string | null;
  error: string | null;
};

type PreviewResponse = {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  duplicateRows: number;
  rows: PreviewRow[];
  warnings: string[];
};

const SAMPLE_CSV = `Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,IpaPronunciation
hypertension,"Persistently elevated arterial blood pressure.","He has a long-standing history of hypertension.",medical,medium,medicine,/ˌhaɪpəˈtɛnʃən/
dyspnoea,"Difficulty or laboured breathing; shortness of breath.","The patient presented with acute dyspnoea on exertion.",symptoms,medium,medicine,/dɪspˈniːə/
`;

export default function AdminVocabularyImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<PreviewResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [importing, setImporting] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const fileInput = useRef<HTMLInputElement | null>(null);

  async function handlePreview() {
    if (!file) return;
    setLoading(true);
    setPreview(null);
    try {
      const res = await previewAdminVocabularyImport(file);
      setPreview(res as PreviewResponse);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Preview failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setLoading(false);
    }
  }

  async function handleImport() {
    if (!file) return;
    setImporting(true);
    try {
      const res = await bulkImportAdminVocabulary(file, false);
      const r = res as { imported: number; skipped: number; duplicates: number; failedRows: number };
      setToast({
        variant: 'success',
        message: `Imported ${r.imported} · skipped ${r.skipped} (dup ${r.duplicates}, failed ${r.failedRows}).`,
      });
      setFile(null);
      setPreview(null);
      if (fileInput.current) fileInput.current.value = '';
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Import failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setImporting(false);
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
    <AdminDashboardShell>
      {toast && <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="Import vocabulary (CSV)"
          description="Upload RFC 4180 CSV. Required columns: Term, Definition. Optional: ExampleSentence, Category, Difficulty, ProfessionId, IpaPronunciation, AudioUrl, ContextNotes, Synonyms (| or ; separated), SourceProvenance."
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
              <input
                ref={fileInput}
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); }}
                className="text-sm"
              />
              <Button variant="secondary" size="sm" disabled={!file || loading} onClick={handlePreview}>
                <FileSpreadsheet className="mr-1.5 h-4 w-4" /> {loading ? 'Checking…' : 'Preview'}
              </Button>
              {preview && preview.validRows > 0 && (
                <Button variant="primary" size="sm" disabled={importing} onClick={handleImport}>
                  {importing ? 'Importing…' : `Import ${preview.validRows} row${preview.validRows === 1 ? '' : 's'}`}
                </Button>
              )}
            </div>

            {preview && (
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  <div className="rounded-2xl border border-gray-200 bg-surface p-3 text-sm">
                    <div className="text-xs text-muted">Total rows</div>
                    <div className="text-2xl font-bold text-navy">{preview.totalRows}</div>
                  </div>
                  <div className="rounded-2xl border border-green-200 bg-green-50 p-3 text-sm">
                    <div className="text-xs text-green-700">Valid</div>
                    <div className="text-2xl font-bold text-green-800">{preview.validRows}</div>
                  </div>
                  <div className="rounded-2xl border border-red-200 bg-red-50 p-3 text-sm">
                    <div className="text-xs text-red-700">Invalid</div>
                    <div className="text-2xl font-bold text-red-800">{preview.invalidRows}</div>
                  </div>
                  <div className="rounded-2xl border border-amber-200 bg-amber-50 p-3 text-sm">
                    <div className="text-xs text-amber-700">Duplicates</div>
                    <div className="text-2xl font-bold text-amber-800">{preview.duplicateRows}</div>
                  </div>
                </div>

                {preview.warnings.length > 0 && (
                  <InlineAlert variant="warning">
                    <ul className="list-inside list-disc space-y-1 text-sm">
                      {preview.warnings.map((w, i) => (<li key={i}>{w}</li>))}
                    </ul>
                  </InlineAlert>
                )}

                <div className="overflow-hidden rounded-2xl border border-gray-200">
                  <table className="w-full text-sm">
                    <thead className="bg-background-light text-left text-xs text-muted">
                      <tr>
                        <th className="px-3 py-2">#</th>
                        <th className="px-3 py-2">Status</th>
                        <th className="px-3 py-2">Term</th>
                        <th className="px-3 py-2">Definition</th>
                        <th className="px-3 py-2">Category</th>
                        <th className="px-3 py-2">Error</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {preview.rows.slice(0, 100).map(r => (
                        <tr key={r.lineNumber} className={!r.valid ? 'bg-red-50/50' : ''}>
                          <td className="px-3 py-1.5 text-xs text-muted">{r.lineNumber}</td>
                          <td className="px-3 py-1.5">
                            {r.valid
                              ? <Badge variant="success"><CheckCircle2 className="mr-1 h-3 w-3" />OK</Badge>
                              : <Badge variant="danger"><AlertTriangle className="mr-1 h-3 w-3" />Error</Badge>
                            }
                          </td>
                          <td className="px-3 py-1.5 font-medium">{r.term ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs text-muted line-clamp-1 max-w-xs">{r.definition ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs">{r.category ?? '—'}</td>
                          <td className="px-3 py-1.5 text-xs text-red-700">{r.error ?? ''}</td>
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
    </AdminDashboardShell>
  );
}
