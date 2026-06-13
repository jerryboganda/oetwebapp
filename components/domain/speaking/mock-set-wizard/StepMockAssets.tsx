'use client';

/**
 * Mock-set wizard — step 3: exam-level shared assets (optional).
 *
 * Warm-up prompts and assessment-criteria PDFs are scoped to a profession, not
 * to an individual mock set, so this step manages the profession-level pool
 * (it does NOT attach files onto the mock-set record). This folds in the old
 * standalone shared-resources page. Uploading publishes the resource so
 * learners see it immediately.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { FileText, Loader2, Upload, CheckCircle2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import {
  adminListSpeakingSharedResources,
  adminPublishSpeakingSharedResource,
  adminUploadSpeakingSharedResource,
  type AdminSpeakingMockSetRow,
  type SpeakingSharedResourceDto,
  type SpeakingSharedResourceKind,
} from '@/lib/api';

function AssetKindSection({
  kind,
  title,
  professionId,
  canWrite,
}: {
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId: string;
  canWrite: boolean;
}) {
  const [items, setItems] = useState<SpeakingSharedResourceDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploadTitle, setUploadTitle] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const list = await adminListSpeakingSharedResources({ kind, profession: professionId || undefined });
      setItems(list);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load resources.');
    } finally {
      setLoading(false);
    }
  }, [kind, professionId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  async function handleUpload() {
    const file = fileRef.current?.files?.[0];
    if (!file) {
      setError('Choose a PDF to upload.');
      return;
    }
    if (!uploadTitle.trim()) {
      setError('Give the resource a title.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await adminUploadSpeakingSharedResource({
        file,
        kind,
        title: uploadTitle.trim(),
        professionId: professionId || null,
      });
      await adminPublishSpeakingSharedResource(created.id);
      setUploadTitle('');
      if (fileRef.current) fileRef.current.value = '';
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Upload failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-3 rounded-2xl border border-border bg-surface p-4">
      <div className="flex items-center gap-2">
        <FileText className="h-4 w-4 text-muted" />
        <h3 className="text-sm font-bold text-navy">{title}</h3>
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {loading ? (
        <p className="inline-flex items-center gap-2 text-sm text-muted"><Loader2 className="h-4 w-4 animate-spin" /> Loading…</p>
      ) : items.length === 0 ? (
        <p className="text-sm text-muted">No {title.toLowerCase()} uploaded for this profession yet.</p>
      ) : (
        <ul className="space-y-2">
          {items.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-background-light px-3 py-2 text-sm">
              <div className="min-w-0">
                <p className="truncate font-semibold text-navy">{r.title}</p>
                <p className="truncate text-xs text-muted">{r.media?.originalFilename ?? '—'}</p>
              </div>
              <Badge variant={r.status === 'Published' ? 'success' : 'muted'}>{r.status}</Badge>
            </li>
          ))}
        </ul>
      )}

      {canWrite ? (
        <div className="space-y-2 border-t border-border pt-3">
          <Input label="New resource title" value={uploadTitle} onChange={(e) => setUploadTitle(e.target.value)} placeholder={`e.g. "${title} — ${professionId || 'all'}"`} />
          <input ref={fileRef} type="file" accept="application/pdf" className="block w-full text-sm text-navy file:mr-3 file:rounded-lg file:border-0 file:bg-primary/10 file:px-3 file:py-1.5 file:text-xs file:font-bold file:text-primary" />
          <Button type="button" variant="primary" size="sm" onClick={() => void handleUpload()} disabled={busy}>
            {busy ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : <Upload className="mr-1 h-3.5 w-3.5" />} Upload &amp; publish
          </Button>
        </div>
      ) : null}
    </div>
  );
}

export function StepMockAssets() {
  const wizard = useAdminWizard<AdminSpeakingMockSetRow>();
  const row = wizard.entity;

  useStepRegistration('assets', { canAdvance: true, submit: null });

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Exam assets</h2>
        <p className="text-sm text-muted">
          Warm-up prompts and assessment-criteria PDFs apply to <span className="font-semibold">all Speaking exams for {row.professionId || 'this profession'}</span> — they are a shared pool, not attached per mock set. Manage them here instead of a separate page.
        </p>
      </header>

      <div className="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-3 py-1 text-xs font-semibold text-emerald-600">
        <CheckCircle2 className="h-3.5 w-3.5" /> This step is optional — you can publish the mock set without changing assets.
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <AssetKindSection kind="WarmUpQuestions" title="Warm-up questions" professionId={row.professionId} canWrite={wizard.canWrite} />
        <AssetKindSection kind="AssessmentCriteria" title="Assessment criteria" professionId={row.professionId} canWrite={wizard.canWrite} />
      </div>
    </div>
  );
}
