'use client';

import { useState } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Toast, InlineAlert } from '@/components/ui/alert';
import { Upload } from 'lucide-react';
import { adminBulkUploadRecalls, type RecallsBulkUploadRow, type RecallsBulkUploadResult } from '@/lib/api';

/**
 * /admin/recalls/bulk-upload — admin CSV bulk upload of vocabulary terms.
 *
 * Spec §8: admins should be able to bulk-upload words by Excel/CSV with
 * topic, difficulty, IPA, example sentence, common mistakes (synonyms),
 * British/American variants and exam type.
 *
 * Expected CSV columns (header row):
 *   term, definition, exampleSentence, category, difficulty,
 *   ipa, americanSpelling, synonymsCsv, examTypeCode, professionId
 *
 * `term` and `definition` are required; everything else is optional.
 * Rows with the same (term, examTypeCode, professionId) are upserted
 * idempotently by the server.
 */
export default function AdminRecallsBulkUploadPage() {
  const [csv, setCsv] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<RecallsBulkUploadResult | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [parseError, setParseError] = useState<string | null>(null);

  function parseCsv(input: string): RecallsBulkUploadRow[] {
    const lines = input
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter((l) => l.length > 0);
    if (lines.length < 2) throw new Error('CSV must include a header row and at least one data row.');
    const header = lines[0]!.split(',').map((h) => h.trim().toLowerCase());

    const required = ['term', 'definition'];
    for (const r of required) {
      if (!header.includes(r)) throw new Error(`Header row must include column "${r}".`);
    }

    return lines.slice(1).map((line) => {
      const cols = splitCsvLine(line);
      const get = (k: string) => {
        const i = header.indexOf(k);
        return i >= 0 ? cols[i]?.trim() : undefined;
      };
      return {
        term: get('term') ?? '',
        definition: get('definition') ?? '',
        exampleSentence: get('examplesentence') ?? undefined,
        category: get('category') ?? undefined,
        difficulty: get('difficulty') ?? undefined,
        ipa: get('ipa') ?? undefined,
        americanSpelling: get('americanspelling') ?? undefined,
        synonymsCsv: get('synonymscsv') ?? undefined,
        examTypeCode: get('examtypecode') ?? undefined,
        professionId: get('professionid') ?? undefined,
      } satisfies RecallsBulkUploadRow;
    });
  }

  async function handleUpload() {
    setParseError(null);
    setResult(null);
    let rows: RecallsBulkUploadRow[];
    try {
      rows = parseCsv(csv);
    } catch (e) {
      setParseError((e as Error).message);
      return;
    }
    setSubmitting(true);
    try {
      const r = await adminBulkUploadRecalls(rows);
      setResult(r);
      setToast({ variant: 'success', message: `Uploaded: ${r.inserted} new, ${r.updated} updated, ${r.skipped} skipped.` });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message ?? 'Upload failed.' });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleFile(file: File) {
    const text = await file.text();
    setCsv(text);
  }

  const sample =
    'term,definition,exampleSentence,category,difficulty,ipa,americanSpelling,synonymsCsv,examTypeCode,professionId\n' +
    'palpitations,awareness of irregular heartbeat,The patient complained of palpitations.,cardiology,medium,/ˌpælpɪˈteɪʃənz/,,,OET,medicine\n' +
    'haemorrhage,heavy bleeding,A massive haemorrhage was suspected.,emergency,hard,,hemorrhage,,OET,medicine';

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Recalls — Bulk upload"
        description="CSV bulk-import of vocabulary terms for the Recalls module."
        icon={Upload}
      />

      <AdminRoutePanel
        title="1. Upload your CSV"
        description="Paste CSV text or select a .csv file. The header row must include term and definition."
      >
        <div className="space-y-3">
          <input
            type="file"
            accept=".csv,text/csv"
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void handleFile(f);
            }}
            className="block text-sm"
          />
          <textarea
            value={csv}
            onChange={(e) => setCsv(e.target.value)}
            placeholder={sample}
            rows={12}
            className="w-full rounded-xl border border-border bg-background-light px-3 py-2 font-mono text-xs focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
          {parseError && <InlineAlert variant="warning">{parseError}</InlineAlert>}
          <div className="flex gap-2">
            <Button onClick={handleUpload} disabled={submitting || !csv.trim()} variant="primary">
              {submitting ? 'Uploading…' : 'Upload'}
            </Button>
            <Button
              onClick={() => setCsv(sample)}
              variant="primary"
              className="bg-background-light text-navy hover:bg-background"
            >
              Load sample
            </Button>
          </div>
        </div>
      </AdminRoutePanel>

      {result && (
        <AdminRoutePanel
          title="2. Result"
          description={`Inserted ${result.inserted} · Updated ${result.updated} · Skipped ${result.skipped}`}
        >
          {result.errors.length > 0 ? (
            <ul className="space-y-1 text-xs text-warning">
              {result.errors.slice(0, 50).map((e, i) => (
                <li key={i}>• {e}</li>
              ))}
              {result.errors.length > 50 && (
                <li className="text-muted">…and {result.errors.length - 50} more.</li>
              )}
            </ul>
          ) : (
            <p className="text-sm text-success">All rows accepted.</p>
          )}
        </AdminRoutePanel>
      )}

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}

/** Minimal CSV line splitter that respects double-quoted fields. */
function splitCsvLine(line: string): string[] {
  const out: string[] = [];
  let cur = '';
  let inQuote = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (inQuote) {
      if (c === '"' && line[i + 1] === '"') {
        cur += '"';
        i++;
      } else if (c === '"') {
        inQuote = false;
      } else {
        cur += c;
      }
    } else {
      if (c === ',') {
        out.push(cur);
        cur = '';
      } else if (c === '"') {
        inQuote = true;
      } else {
        cur += c;
      }
    }
  }
  out.push(cur);
  return out;
}
