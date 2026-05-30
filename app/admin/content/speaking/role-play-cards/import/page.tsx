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
 */
import { useState } from 'react';
import { useRouter } from 'next/navigation';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';

import { InlineAlert } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
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

export default function AdminSpeakingRolePlayCardImportPage() {
  const router = useRouter();
  const [file, setFile] = useState<File | null>(null);
  const [professionId, setProfessionId] = useState('');
  const [topic, setTopic] = useState('');
  const [autoDraft, setAutoDraft] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<SpeakingContentImportResult | null>(null);

  async function submit() {
    setError(null);
    setResult(null);
    if (!file) {
      setError('Choose a source PDF to import.');
      return;
    }
    if (!professionId.trim()) {
      setError('Profession is required.');
      return;
    }
    setSubmitting(true);
    try {
      const response = await importSpeakingRolePlayCard({
        file,
        professionId: professionId.trim(),
        topic: topic.trim() || null,
        autoDraft,
      });
      setResult(response);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not import the source PDF.');
    } finally {
      setSubmitting(false);
    }
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
                accept="application/pdf,.pdf"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm text-admin-fg-muted file:mr-3 file:rounded-md file:border-0 file:bg-admin-accent/10 file:px-3 file:py-2 file:text-sm file:font-medium file:text-admin-accent"
              />
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
          <div className="mt-3 flex justify-end">
            <Button onClick={submit} disabled={submitting}>
              {submitting ? 'Importing…' : 'Import PDF'}
            </Button>
          </div>
        </CardContent>
      </Card>

      {result ? (
        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Import result</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <Badge variant={result.likelyScanned ? 'warning' : 'success'}>
                {result.likelyScanned ? 'Scanned (little/no text)' : 'Text extracted'}
              </Badge>
              <span className="text-admin-fg-muted">
                {result.extractedChars.toLocaleString()} chars · {(result.sourceBytes / 1024).toFixed(0)} KB saved
              </span>
              <Badge variant={result.validation.isPublishable ? 'success' : 'warning'}>
                {result.validation.isPublishable ? 'Builder check passed' : `${result.validation.blockers.length} blocker(s)`}
              </Badge>
            </div>

            {result.warning ? <InlineAlert variant="warning">{result.warning}</InlineAlert> : null}

            <div>
              <h4 className="mb-2 text-sm font-semibold text-admin-fg">Builder validation</h4>
              <ul className="space-y-1 text-sm">
                {result.validation.checks.map((check) => (
                  <li key={check.field} className="flex items-center gap-2">
                    <Badge variant={check.detected ? 'success' : check.required ? 'error' : 'muted'}>
                      {check.detected ? 'detected' : check.required ? 'missing' : 'optional'}
                    </Badge>
                    <span className="font-medium text-admin-fg">{check.field}</span>
                    {check.note ? <span className="text-admin-fg-muted">— {check.note}</span> : null}
                  </li>
                ))}
              </ul>
            </div>

            {result.draftCardId ? (
              <div className="flex items-center gap-3">
                <Badge variant="success">Draft created</Badge>
                <Button
                  variant="outline"
                  onClick={() =>
                    router.push(`/admin/content/speaking/role-play-cards/${encodeURIComponent(result.draftCardId!)}`)
                  }
                >
                  Open draft to review &amp; edit
                </Button>
              </div>
            ) : (
              <p className="text-sm text-admin-fg-muted">
                No draft was created. The source asset was saved — structure the card manually via{' '}
                <span className="font-medium">New role-play card</span>.
              </p>
            )}
          </CardContent>
        </Card>
      ) : null}
    </AdminCatalogLayout>
  );
}
