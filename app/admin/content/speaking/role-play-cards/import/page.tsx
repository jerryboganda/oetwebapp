'use client';

/**
 * WS9 (SPK-007) — scanned/text PDF import → structured role-play draft.
 *
 * Backend wiring (live):
 *   POST /v1/admin/speaking/role-play-cards/import   (multipart)
 *     → persists the source PDF via IFileStorage (provenance), extracts text
 *       (PdfPig + configured OCR fallback), runs a builder-validation pass
 *       that mirrors the publish gate, and — when `autoDraft` is set and
 *       usable text was extracted — produces a grounded Draft card.
 *
 * A scanned PDF with no OCR provider still saves the source asset and returns
 * the validation report so the admin can structure the card manually. The
 * admin reviews + edits + publishes the draft from the role-play card list.
 *
 * Multiple source PDFs can be selected at once — each is imported sequentially
 * (sharing profession/topic/auto-draft) and gets its own result card below, so
 * the admin can review/act on each import independently.
 */
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';

import { InlineAlert } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
import { downloadMediaAssetContent } from '@/lib/api';
import {
  PROFESSION_OPTIONS,
  importSpeakingRolePlayCard,
  type SpeakingContentImportResult,
} from '@/lib/api/speaking-role-play-cards';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards', href: '/admin/content/speaking/role-play-cards' },
  { label: 'Import PDF' },
];

interface ImportEntry {
  fileName: string;
  response?: SpeakingContentImportResult;
  error?: string;
}

/** Fetches the persisted source PDF (authenticated) for a completed import so the operator can read a scanned card while transcribing it by hand. */
function SourcePdfPreview({ mediaId }: { mediaId: string }) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;
    void downloadMediaAssetContent(mediaId)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [mediaId]);

  if (!url) return null;

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h4 className="text-sm font-semibold text-admin-fg">Source PDF (for manual entry)</h4>
        <a className="text-sm font-medium text-admin-accent underline" href={url} target="_blank" rel="noreferrer">
          Open in new tab
        </a>
      </div>
      <object data={url} type="application/pdf" className="h-[480px] w-full rounded-md border">
        <p className="p-3 text-sm text-admin-fg-muted">
          Inline preview unavailable.{' '}
          <a href={url} target="_blank" rel="noreferrer" className="underline">
            Open the source PDF
          </a>
          .
        </p>
      </object>
    </div>
  );
}

function ImportResultCard({
  entry,
  professionId,
  onEnterManually,
  onOpenDraft,
}: {
  entry: ImportEntry;
  professionId: string;
  onEnterManually: (sourceMediaId: string | null) => void;
  onOpenDraft: (draftCardId: string) => void;
}) {
  return (
    <Card className="col-span-full">
      <CardHeader>
        <CardTitle>{entry.fileName}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {entry.error ? (
          <InlineAlert variant="error">{entry.error}</InlineAlert>
        ) : entry.response ? (
          <>
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <Badge variant={entry.response.likelyScanned ? 'warning' : 'success'}>
                {entry.response.likelyScanned ? 'Scanned (little/no text)' : 'Text extracted'}
              </Badge>
              <span className="text-admin-fg-muted">
                {entry.response.extractedChars.toLocaleString()} chars · {(entry.response.sourceBytes / 1024).toFixed(0)} KB saved
              </span>
              <Badge variant={entry.response.validation.isPublishable ? 'success' : 'warning'}>
                {entry.response.validation.isPublishable ? 'Builder check passed' : `${entry.response.validation.blockers.length} blocker(s)`}
              </Badge>
            </div>

            {entry.response.warning ? <InlineAlert variant="warning">{entry.response.warning}</InlineAlert> : null}

            {entry.response.sourceMediaId ? <SourcePdfPreview mediaId={entry.response.sourceMediaId} /> : null}

            <div>
              <h4 className="mb-2 text-sm font-semibold text-admin-fg">Builder validation</h4>
              <ul className="space-y-1 text-sm">
                {entry.response.validation.checks.map((check) => (
                  <li key={check.field} className="flex items-center gap-2">
                    <Badge variant={check.detected ? 'success' : check.required ? 'danger' : 'muted'}>
                      {check.detected ? 'detected' : check.required ? 'missing' : 'optional'}
                    </Badge>
                    <span className="font-medium text-admin-fg">{check.field}</span>
                    {check.note ? <span className="text-admin-fg-muted">— {check.note}</span> : null}
                  </li>
                ))}
              </ul>
            </div>

            {entry.response.draftCardId ? (
              <div className="flex items-center gap-3">
                <Badge variant="success">Draft created</Badge>
                <Button variant="outline" onClick={() => onOpenDraft(entry.response!.draftCardId!)}>
                  Open draft to review &amp; edit
                </Button>
              </div>
            ) : (
              <div className="space-y-2">
                <p className="text-sm text-admin-fg-muted">
                  {entry.response.likelyScanned
                    ? 'No draft was created — the PDF is a scanned image with no extractable text. Enter the card manually using the source preview above as your reference. The manual step (not OCR) is what guarantees 100% fidelity.'
                    : 'No draft was created. The source asset was saved — enter the card manually using the source preview above.'}
                </p>
                <Button onClick={() => onEnterManually(entry.response!.sourceMediaId ?? null)}>
                  Enter card manually
                </Button>
              </div>
            )}
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}

export default function AdminSpeakingRolePlayCardImportPage() {
  const router = useRouter();
  const [files, setFiles] = useState<File[]>([]);
  const [professionId, setProfessionId] = useState('');
  const [topic, setTopic] = useState('');
  const [autoDraft, setAutoDraft] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submittingIndex, setSubmittingIndex] = useState(0);
  const [results, setResults] = useState<ImportEntry[]>([]);

  async function submit() {
    setError(null);
    setResults([]);
    if (files.length === 0) {
      setError('Choose one or more source PDFs to import.');
      return;
    }
    if (!professionId.trim()) {
      setError('Profession is required.');
      return;
    }
    setSubmitting(true);
    try {
      for (let i = 0; i < files.length; i++) {
        setSubmittingIndex(i);
        const f = files[i];
        try {
          const response = await importSpeakingRolePlayCard({
            file: f,
            professionId: professionId.trim(),
            topic: topic.trim() || null,
            autoDraft,
          });
          setResults((prev) => [...prev, { fileName: f.name, response }]);
        } catch (err) {
          setResults((prev) => [
            ...prev,
            { fileName: f.name, error: err instanceof Error ? err.message : 'Could not import the source PDF.' },
          ]);
        }
      }
    } finally {
      setSubmitting(false);
    }
  }

  function enterManually(sourceMediaId: string | null) {
    const sp = new URLSearchParams();
    if (professionId.trim()) sp.set('professionId', professionId.trim());
    if (sourceMediaId) sp.set('sourceMediaId', sourceMediaId);
    const qs = sp.toString();
    router.push(`/admin/content/speaking/role-play-cards/new${qs ? `?${qs}` : ''}`);
  }

  return (
    <AdminCatalogLayout
      title="Speaking · Import source PDF"
      description="Import a scanned or text source paper. The source is saved for provenance, text is extracted (OCR fallback for scanned pages), and a builder-validation report shows which structured fields are present before a draft is created. The admin remains accountable for the published card."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Content · Import"
      backHref="/admin/content/speaking/role-play-cards"
      hideViewModeToggle
    >
      <Card className="col-span-full">
        <CardHeader>
          <CardTitle>Source</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <input
                type="file"
                multiple
                accept="application/pdf,.pdf"
                onChange={(e) => setFiles(Array.from(e.target.files ?? []))}
                className="block w-full text-sm text-admin-fg-muted file:mr-3 file:rounded-md file:border-0 file:bg-admin-accent/10 file:px-3 file:py-2 file:text-sm file:font-medium file:text-admin-accent"
              />
              {files.length === 1 ? (
                <p className="mt-1 text-xs text-admin-fg-muted">{files[0].name}</p>
              ) : files.length > 1 ? (
                <ul className="mt-1 max-h-28 space-y-0.5 overflow-y-auto text-xs text-admin-fg-muted list-disc pl-4">
                  {files.map((f, i) => (
                    <li key={`${f.name}-${i}`}>{f.name}</li>
                  ))}
                </ul>
              ) : null}
            </div>
            <Select
              value={professionId}
              onChange={(e) => setProfessionId(e.target.value)}
              options={[{ value: '', label: 'Select profession…' }, ...PROFESSION_OPTIONS]}
            />
            <Input
              placeholder="Topic / scenario seed (optional)"
              value={topic}
              onChange={(e) => setTopic(e.target.value)}
            />
            <label className="flex items-center gap-2 text-sm text-admin-fg-muted sm:col-span-2">
              <input
                type="checkbox"
                checked={autoDraft}
                onChange={(e) => setAutoDraft(e.target.checked)}
              />
              Auto-draft a card from the extracted text (when usable text is found)
            </label>
          </div>
          {error ? (
            <div className="mt-3">
              <InlineAlert variant="error">{error}</InlineAlert>
            </div>
          ) : null}
          {submitting && files.length > 1 ? (
            <p className="mt-3 text-xs text-admin-fg-muted">
              Importing {submittingIndex + 1} of {files.length}: {files[submittingIndex]?.name}
            </p>
          ) : null}
          <div className="mt-3 flex justify-end">
            <Button onClick={submit} disabled={submitting}>
              {submitting ? 'Importing…' : files.length > 1 ? `Import ${files.length} PDFs` : 'Import PDF'}
            </Button>
          </div>
        </CardContent>
      </Card>

      {results.map((entry, i) => (
        <ImportResultCard
          key={`${entry.fileName}-${i}`}
          entry={entry}
          professionId={professionId}
          onEnterManually={enterManually}
          onOpenDraft={(draftCardId) => router.push(`/admin/content/speaking/role-play-cards/${encodeURIComponent(draftCardId)}`)}
        />
      ))}
    </AdminCatalogLayout>
  );
}
