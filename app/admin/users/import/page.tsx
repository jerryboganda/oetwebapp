'use client';

import Link from 'next/link';
import { useCallback, useRef, useState } from 'react';
import { ArrowLeft, Download, Upload, FileSpreadsheet, AlertCircle, CheckCircle2 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { bulkImportUsers } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

interface ImportError {
  row: number;
  email: string;
  error: string;
}

interface ImportResult {
  total: number;
  created: number;
  skipped: number;
  errors: ImportError[];
}

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const CSV_TEMPLATE = 'email,firstName,lastName,role,profession\njane.smith@example.com,Jane,Smith,learner,nursing\njohn.doe@example.com,John,Doe,expert,medicine\n';

export default function BulkImportUsersPage() {
  const { isAuthenticated } = useAdminAuth();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const handleFileSelect = useCallback((file: File | undefined) => {
    if (!file) return;
    if (!file.name.endsWith('.csv')) {
      setToast({ variant: 'error', message: 'Only CSV files are accepted.' });
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      setToast({ variant: 'error', message: 'File must be under 5 MB.' });
      return;
    }
    setSelectedFile(file);
    setResult(null);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    const file = e.dataTransfer.files[0];
    handleFileSelect(file);
  }, [handleFileSelect]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  }, []);

  const handleDragLeave = useCallback(() => {
    setIsDragging(false);
  }, []);

  const handleUpload = useCallback(async () => {
    if (!selectedFile) return;
    setIsUploading(true);
    setResult(null);
    try {
      const importResult = await bulkImportUsers(selectedFile);
      setResult(importResult);
      if (importResult.created > 0) {
        setToast({ variant: 'success', message: `Successfully imported ${importResult.created} user(s).` });
      } else if (importResult.errors.length > 0) {
        setToast({ variant: 'error', message: 'Import completed with errors. No users were created.' });
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Import failed. Please try again.';
      setToast({ variant: 'error', message });
    } finally {
      setIsUploading(false);
    }
  }, [selectedFile]);

  const handleDownloadTemplate = useCallback(() => {
    const blob = new Blob([CSV_TEMPLATE], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'user-import-template.csv';
    a.click();
    URL.revokeObjectURL(url);
  }, []);

  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}

      <div className="mb-6 flex items-center gap-3">
        <Link
          href="/admin/users"
          className="inline-flex items-center gap-1 text-sm font-medium text-muted hover:text-primary"
        >
          <ArrowLeft size={16} /> Back to Users
        </Link>
      </div>

      <AdminRouteSectionHeader
        title="Bulk Import Users"
        description="Upload a CSV file to create multiple user accounts at once."
      />

      <AdminRoutePanel>
        <div className="space-y-6">
          {/* Template download */}
          <div className="flex items-center justify-between rounded-2xl border border-border/40 bg-background-light px-4 py-3">
            <div className="flex items-center gap-3">
              <FileSpreadsheet size={20} className="text-muted" />
              <div>
                <p className="text-sm font-medium text-navy">CSV Template</p>
                <p className="text-xs text-muted">Download the template with required headers</p>
              </div>
            </div>
            <Button variant="outline" size="sm" onClick={handleDownloadTemplate}>
              <Download size={14} className="mr-1.5" /> Download
            </Button>
          </div>

          {/* Drop zone */}
          <div
            className={`flex cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed px-6 py-12 text-center transition-colors ${
              isDragging
                ? 'border-primary bg-primary/5'
                : selectedFile
                  ? 'border-emerald-400 bg-emerald-50/50 dark:bg-emerald-950/20'
                  : 'border-border/60 hover:border-primary/40 hover:bg-background-light'
            }`}
            onDrop={handleDrop}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onClick={() => fileInputRef.current?.click()}
            role="button"
            tabIndex={0}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') fileInputRef.current?.click(); }}
          >
            <input
              ref={fileInputRef}
              type="file"
              accept=".csv"
              className="hidden"
              onChange={(e) => handleFileSelect(e.target.files?.[0])}
            />
            <Upload size={32} className={selectedFile ? 'text-emerald-500' : 'text-muted'} />
            {selectedFile ? (
              <div className="mt-3">
                <p className="text-sm font-semibold text-navy">{selectedFile.name}</p>
                <p className="mt-1 text-xs text-muted">{(selectedFile.size / 1024).toFixed(1)} KB</p>
                <p className="mt-2 text-xs text-primary">Click or drop to replace</p>
              </div>
            ) : (
              <div className="mt-3">
                <p className="text-sm font-medium text-navy">Drop your CSV file here</p>
                <p className="mt-1 text-xs text-muted">or click to browse — max 5 MB, up to 1,000 rows</p>
              </div>
            )}
          </div>

          {/* Upload button */}
          <div className="flex justify-end">
            <Button
              variant="primary"
              disabled={!selectedFile || isUploading}
              onClick={handleUpload}
            >
              {isUploading ? (
                <>
                  <span className="mr-2 inline-block h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                  Importing…
                </>
              ) : (
                <>
                  <Upload size={14} className="mr-1.5" /> Import Users
                </>
              )}
            </Button>
          </div>

          {/* Format guide */}
          <div className="rounded-2xl border border-border/40 bg-background-light px-4 py-3">
            <p className="text-xs font-semibold uppercase tracking-wider text-muted">CSV Format</p>
            <p className="mt-1 text-xs text-muted">
              Headers: <code className="rounded bg-surface px-1 py-0.5 font-mono text-navy">email,firstName,lastName,role,profession</code>
            </p>
            <p className="mt-1 text-xs text-muted">
              Roles: <code className="rounded bg-surface px-1 py-0.5 font-mono text-navy">learner</code>,{' '}
              <code className="rounded bg-surface px-1 py-0.5 font-mono text-navy">expert</code>,{' '}
              <code className="rounded bg-surface px-1 py-0.5 font-mono text-navy">admin</code>
            </p>
            <p className="mt-1 text-xs text-muted">Duplicate emails are skipped. Invalid rows are reported in the results.</p>
          </div>
        </div>
      </AdminRoutePanel>

      {/* Results */}
      {result && (
        <AdminRoutePanel className="mt-6">
          <AdminRouteSectionHeader title="Import Results" />

          <div className="mt-4 grid grid-cols-3 gap-4">
            <div className="rounded-2xl bg-background-light px-4 py-3 text-center">
              <p className="text-2xl font-bold text-navy">{result.total}</p>
              <p className="text-xs text-muted">Total rows</p>
            </div>
            <div className="rounded-2xl bg-emerald-50 px-4 py-3 text-center dark:bg-emerald-950/30">
              <p className="text-2xl font-bold text-emerald-600">{result.created}</p>
              <p className="text-xs text-muted">Created</p>
            </div>
            <div className="rounded-2xl bg-amber-50 px-4 py-3 text-center dark:bg-amber-950/30">
              <p className="text-2xl font-bold text-amber-600">{result.skipped}</p>
              <p className="text-xs text-muted">Skipped</p>
            </div>
          </div>

          {result.created > 0 && result.errors.length === 0 && (
            <div className="mt-4 flex items-center gap-2 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700 dark:border-emerald-800 dark:bg-emerald-950/30 dark:text-emerald-400">
              <CheckCircle2 size={16} />
              All users imported successfully.
            </div>
          )}

          {result.errors.length > 0 && (
            <div className="mt-4">
              <div className="mb-2 flex items-center gap-2 text-sm font-medium text-red-600">
                <AlertCircle size={16} />
                {result.errors.length} row(s) had errors
              </div>
              <div className="overflow-hidden rounded-2xl border border-border/40">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border/40 bg-background-light">
                      <th className="px-4 py-2 text-left font-semibold text-navy">Row</th>
                      <th className="px-4 py-2 text-left font-semibold text-navy">Email</th>
                      <th className="px-4 py-2 text-left font-semibold text-navy">Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.errors.map((err, i) => (
                      <tr key={i} className="border-b border-border/20 last:border-b-0">
                        <td className="px-4 py-2 text-muted">{err.row}</td>
                        <td className="px-4 py-2 font-mono text-xs text-navy">{err.email || '—'}</td>
                        <td className="px-4 py-2 text-red-600">{err.error}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
