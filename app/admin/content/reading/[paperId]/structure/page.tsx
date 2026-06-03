'use client';

/**
 * G2 — Reading structure review.
 *
 * Side-by-side review screen: the attached Reading PDFs (left) beside the
 * structure tools (right). The operator reads the Part A/B/C PDFs on the left,
 * builds or corrects the canonical 42-item question structure on the right,
 * validates against the publish gate, then publishes from the paper overview.
 *
 * Reuses: ReadingManifestPanel (import/export + report), validateReadingPaper,
 * getContentPaper (assets), downloadMediaAssetContent (authenticated PDF bytes).
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Loader2, HelpCircle } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Toast } from '@/components/ui/alert';
import { ReadingManifestPanel } from '../ReadingManifestPanel';
import { validateReadingPaper, type ReadingValidationReport } from '@/lib/reading-authoring-api';
import { getContentPaper, type ContentPaperDto } from '@/lib/content-upload-api';
import { downloadMediaAssetContent } from '@/lib/api';

function AssetPdfPreview({ mediaAssetId, label }: { mediaAssetId: string; label: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;
    downloadMediaAssetContent(mediaAssetId)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [mediaAssetId]);

  return (
    <div className="rounded-lg border border-admin-border p-2">
      <div className="mb-1 flex items-center justify-between">
        <span className="text-xs font-semibold text-admin-fg">{label}</span>
        {url ? (
          <a className="text-xs font-medium text-admin-accent underline" href={url} target="_blank" rel="noreferrer">
            Open in new tab
          </a>
        ) : null}
      </div>
      {failed ? (
        <p className="px-1 py-3 text-xs text-admin-fg-muted">Preview unavailable for this asset.</p>
      ) : url ? (
        <object data={url} type="application/pdf" className="h-[420px] w-full rounded">
          <p className="p-2 text-xs text-admin-fg-muted">
            Inline preview unavailable.{' '}
            <a href={url} target="_blank" rel="noreferrer" className="underline">Open the PDF</a>.
          </p>
        </object>
      ) : (
        <div className="flex h-[120px] items-center justify-center">
          <Loader2 className="h-4 w-4 animate-spin text-admin-fg-muted" />
        </div>
      )}
    </div>
  );
}

export default function AdminReadingStructureReviewPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [validation, setValidation] = useState<ReadingValidationReport | null>(null);
  const [validating, setValidating] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    if (!paperId) return;
    getContentPaper(paperId)
      .then(setPaper)
      .catch(() => setToast({ variant: 'error', message: 'Could not load the paper.' }));
  }, [paperId]);

  const revalidate = useCallback(async () => {
    if (!paperId) return;
    setValidating(true);
    try {
      setValidation(await validateReadingPaper(paperId));
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Validation failed' });
    } finally {
      setValidating(false);
    }
  }, [paperId]);

  useEffect(() => {
    void revalidate();
  }, [revalidate]);

  const assets = useMemo(() => paper?.assets ?? [], [paper]);

  return (
    <AdminSettingsLayout
      title="Reading · Structure review"
      description="Read the attached Part A/B/C PDFs on the left; build or correct the 42-item structure (Part A 20, Part B 6, Part C 16) on the right via the manifest, validate against the publish gate, then publish from the paper overview."
      eyebrow="Content · Reading"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: paper?.title ?? paperId, href: `/admin/content/reading/${paperId}` },
        { label: 'Structure review' },
      ]}
    >
      <div className="grid gap-4 lg:grid-cols-2">
        <SettingsSection
          title="Source documents"
          description="The PDFs attached to this paper, including the primary Part A/B/C question-paper slots."
        >
          <div className="space-y-3">
            {assets.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">No source documents are attached to this paper yet.</p>
            ) : (
              assets.map((a) => (
                <AssetPdfPreview
                  key={a.mediaAssetId}
                  mediaAssetId={a.mediaAssetId}
                  label={`${a.role}${a.part ? ` · Part ${a.part}` : ''}`}
                />
              ))
            )}
          </div>
        </SettingsSection>

        <div className="space-y-4">
          {paper ? (
            <ReadingManifestPanel
              paperId={paperId}
              paperTitle={paper.title ?? ''}
              onImported={() => void revalidate()}
              onNotify={(variant, message) => setToast({ variant, message })}
            />
          ) : null}

          <SettingsSection
            title="Validation"
            description="OET Reading must total 42 items before it can be published."
            actions={
              <Button size="sm" variant="secondary" onClick={() => void revalidate()} disabled={validating}>
                {validating ? 'Checking…' : 'Re-validate'}
              </Button>
            }
          >
            {validation ? (
              <div className="space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant={validation.isPublishReady ? 'success' : 'warning'}>
                    {validation.isPublishReady ? 'Publish ready' : 'Needs review'}
                  </Badge>
                  <span className="text-xs text-admin-fg-muted">
                    Part A {validation.counts.partACount} · Part B {validation.counts.partBCount} · Part C{' '}
                    {validation.counts.partCCount} · {validation.counts.totalPoints} points
                  </span>
                </div>
                {validation.issues.length > 0 ? (
                  <ul className="space-y-1">
                    {validation.issues.slice(0, 12).map((iss, i) => (
                      <li key={i} className="text-xs text-admin-fg-muted">
                        <Badge variant={iss.severity === 'error' ? 'danger' : 'warning'}>{iss.severity}</Badge>{' '}
                        <span className="font-mono">{iss.code}</span> — {iss.message}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="text-xs text-admin-fg-muted">No outstanding issues.</p>
                )}
              </div>
            ) : (
              <p className="text-sm text-admin-fg-muted">Run validation to see the publish-gate report.</p>
            )}
          </SettingsSection>

          <SettingsSection
            title="Manual authoring"
            description="Attach or replace the Part A/B/C PDFs, then author the learner-safe questions directly."
          >
            <div className="flex flex-wrap gap-2">
              <Button asChild size="sm" variant="outline">
                <Link href={`/admin/content/reading/${paperId}/texts`}>
                  <FileText className="h-4 w-4" />
                  Manage PDFs
                </Link>
              </Button>
              <Button asChild size="sm" variant="outline">
                <Link href={`/admin/content/reading/${paperId}/questions`}>
                  <HelpCircle className="h-4 w-4" />
                  Edit questions
                </Link>
              </Button>
              <Button asChild size="sm" variant="ghost">
                <Link href={`/admin/content/reading/${paperId}`}>
                  <ArrowLeft className="h-4 w-4" />
                  Back to paper
                </Link>
              </Button>
            </div>
          </SettingsSection>
        </div>
      </div>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminSettingsLayout>
  );
}
