'use client';

import { useCallback, useRef, useState } from 'react';
import { Download, Upload, FileSpreadsheet, AlertCircle, CheckCircle2, Users, FileText, SkipForward } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/admin/ui/button';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { bulkImportUsers } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Users', href: '/admin/users' },
  { label: 'Bulk import' },
];

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
    <AdminSettingsLayout
      title="Bulk Import Users"
      description="Upload a CSV file to create multiple user accounts at once."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Users"
      icon={<Upload className="h-5 w-5" />}
      backHref="/admin/users"
    >
      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}

      <SettingsSection title="Upload CSV">
        <div className="space-y-6">
          {/* Template download */}
          <Card surface="tinted-primary">
            <CardContent className="p-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <FileSpreadsheet size={20} className="text-admin-fg-muted" />
                  <div>
                    <p className="text-sm font-medium text-admin-fg-strong">CSV Template</p>
                    <p className="text-xs text-admin-fg-muted">Download the template with required headers</p>
                  </div>
                </div>
                <Button variant="outline" size="sm" onClick={handleDownloadTemplate}>
                  <Download size={14} className="mr-1.5" /> Download
                </Button>
              </div>
            </CardContent>
          </Card>

          {/* Drop zone */}
          <div
            className={`flex cursor-pointer flex-col items-center justify-center rounded-admin-lg border-2 border-dashed px-6 py-12 text-center transition-colors ${
              isDragging
                ? 'border-[var(--admin-primary)] bg-admin-primary-tint'
                : selectedFile
                  ? 'border-admin-success bg-admin-success-tint'
                  : 'border-admin-border hover:border-admin-border-strong hover:bg-admin-bg-subtle'
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
            <Upload size={32} className={selectedFile ? 'text-admin-success' : 'text-admin-fg-muted'} />
            {selectedFile ? (
              <div className="mt-3">
                <p className="text-sm font-semibold text-admin-fg-strong">{selectedFile.name}</p>
                <p className="mt-1 text-xs text-admin-fg-muted">{(selectedFile.size / 1024).toFixed(1)} KB</p>
                <p className="mt-2 text-xs text-[var(--admin-primary)]">Click or drop to replace</p>
              </div>
            ) : (
              <div className="mt-3">
                <p className="text-sm font-medium text-admin-fg-strong">Drop your CSV file here</p>
                <p className="mt-1 text-xs text-admin-fg-muted">or click to browse, max 5 MB, up to 1,000 rows</p>
              </div>
            )}
          </div>

          {/* Upload button */}
          <div className="flex justify-end">
            <Button
              variant="primary"
              disabled={!selectedFile || isUploading}
              loading={isUploading}
              onClick={handleUpload}
            >
              <Upload size={14} className="mr-1.5" /> {isUploading ? 'Importing…' : 'Import Users'}
            </Button>
          </div>

          {/* Format guide */}
          <Card>
            <CardContent className="p-4">
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">CSV Format</p>
              <p className="mt-1 text-xs text-admin-fg-muted">
                Headers: <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-admin-fg-strong">email,firstName,lastName,role,profession</code>
              </p>
              <p className="mt-1 text-xs text-admin-fg-muted">
                Roles: <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-admin-fg-strong">learner</code>,{' '}
                <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-admin-fg-strong">expert</code>,{' '}
                <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-admin-fg-strong">admin</code>
              </p>
              <p className="mt-1 text-xs text-admin-fg-muted">Duplicate emails are skipped. Invalid rows are reported in the results.</p>
            </CardContent>
          </Card>
        </div>
      </SettingsSection>

      {/* Results */}
      {result && (
        <SettingsSection title="Import Results">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <KpiTile label="Total rows" value={result.total} icon={<FileText className="h-4 w-4" />} />
            <KpiTile label="Created" value={result.created} icon={<Users className="h-4 w-4" />} tone="success" />
            <KpiTile label="Skipped" value={result.skipped} icon={<SkipForward className="h-4 w-4" />} tone={result.skipped > 0 ? 'warning' : 'default'} />
          </div>

          {result.created > 0 && result.errors.length === 0 && (
            <Card surface="tinted-success" className="mt-4">
              <CardContent className="p-3 flex items-center gap-2 text-sm text-admin-success">
                <CheckCircle2 size={16} />
                All users imported successfully.
              </CardContent>
            </Card>
          )}

          {result.errors.length > 0 && (
            <div className="mt-4">
              <div className="mb-2 flex items-center gap-2 text-sm font-medium text-admin-danger">
                <AlertCircle size={16} />
                {result.errors.length} row(s) had errors
              </div>
              <div className="overflow-hidden rounded-admin-lg border border-admin-border">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-admin-border bg-admin-bg-subtle">
                      <th scope="col" className="px-4 py-2 text-left font-semibold text-admin-fg-strong">Row</th>
                      <th scope="col" className="px-4 py-2 text-left font-semibold text-admin-fg-strong">Email</th>
                      <th scope="col" className="px-4 py-2 text-left font-semibold text-admin-fg-strong">Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.errors.map((err, i) => (
                      <tr key={i} className="border-b border-admin-border last:border-b-0">
                        <td className="px-4 py-2 text-admin-fg-muted">{err.row}</td>
                        <td className="px-4 py-2 font-mono text-xs text-admin-fg-strong">{err.email || '-'}</td>
                        <td className="px-4 py-2 text-admin-danger">{err.error}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </SettingsSection>
      )}
    </AdminSettingsLayout>
  );
}
