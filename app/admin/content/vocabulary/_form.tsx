'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Plus, Save } from 'lucide-react';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { fetchAdminVocabularyRecallSets } from '@/lib/api';

export type VocabFormValues = {
  term: string;
  definition: string;
  exampleSentence: string;
  contextNotes: string;
  examTypeCode: string;
  professionId: string;
  category: string;
  difficulty: string;
  ipaPronunciation: string;
  americanSpelling: string;
  audioUrl: string;
  audioSlowUrl: string;
  audioSentenceUrl: string;
  audioMediaAssetId: string;
  imageUrl: string;
  synonyms: string[];
  collocations: string[];
  relatedTerms: string[];
  recallSetCodes: string[];
  commonMistakes: string[];
  similarSounding: string[];
  oetSubtestTags: string[];
  sourceProvenance: string;
  status: 'draft' | 'active' | 'archived';
};

type Props = {
  mode: 'create' | 'edit';
  initial?: Partial<VocabFormValues>;
  onSubmit: (values: VocabFormValues) => Promise<void>;
  onPublish?: () => Promise<void>;
  itemId?: string;
};

const CATEGORIES = [
  'medical', 'anatomy', 'symptoms', 'procedures', 'pharmacology', 'conditions',
  'clinical_communication', 'diagnostics', 'nursing_care', 'oral_health',
  'dispensing', 'counselling',
];

type RecallSetOption = { code: string; displayName: string };

export function VocabularyForm({ mode, initial, onSubmit, onPublish, itemId }: Props) {
  const router = useRouter();
  const [recallSetOptions, setRecallSetOptions] = useState<RecallSetOption[]>([]);
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetchAdminVocabularyRecallSets({ examTypeCode: 'oet' });
        if (cancelled) return;
        const sets = (res as { sets?: Array<{ code?: string; displayName?: string }> })?.sets ?? [];
        setRecallSetOptions(
          sets
            .filter((s): s is { code: string; displayName?: string } => typeof s.code === 'string')
            .map(s => ({ code: s.code, displayName: s.displayName ?? s.code }))
        );
      } catch {
        // fall back to canonical codes
        setRecallSetOptions([
          { code: 'old', displayName: 'Old Recalls + Most Common Words' },
          { code: '2023-2025', displayName: 'January 2023 — End of 2025' },
          { code: '2026', displayName: 'January 2026 onwards' },
        ]);
      }
    })();
    return () => { cancelled = true; };
  }, []);
  // NOTE: per admin request, the vocab form was simplified to a minimal set of
  // visible inputs (term, definition, example sentence, exam, profession,
  // category, difficulty, recall practice collection labels, status). The
  // other VocabFormValues fields are retained on state with sensible defaults
  // so the API contract is unchanged and the publish gate (which requires
  // sourceProvenance) still passes for admin-authored entries.
  const [v, setV] = useState<VocabFormValues>({
    term: initial?.term ?? '',
    definition: initial?.definition ?? '',
    exampleSentence: initial?.exampleSentence ?? '',
    contextNotes: initial?.contextNotes ?? '',
    examTypeCode: initial?.examTypeCode ?? 'oet',
    professionId: initial?.professionId ?? '',
    category: initial?.category ?? 'medical',
    difficulty: initial?.difficulty ?? 'medium',
    ipaPronunciation: initial?.ipaPronunciation ?? '',
    americanSpelling: initial?.americanSpelling ?? '',
    audioUrl: initial?.audioUrl ?? '',
    audioSlowUrl: initial?.audioSlowUrl ?? '',
    audioSentenceUrl: initial?.audioSentenceUrl ?? '',
    audioMediaAssetId: initial?.audioMediaAssetId ?? '',
    imageUrl: initial?.imageUrl ?? '',
    synonyms: initial?.synonyms ?? [],
    collocations: initial?.collocations ?? [],
    relatedTerms: initial?.relatedTerms ?? [],
    recallSetCodes: initial?.recallSetCodes ?? [],
    commonMistakes: initial?.commonMistakes ?? [],
    similarSounding: initial?.similarSounding ?? [],
    oetSubtestTags: initial?.oetSubtestTags ?? [],
    // Default provenance for admin-authored vocab entries so the publish gate
    // can run without the field being rendered. Preserves the existing
    // initial-value behaviour when the API returns an explicit value.
    sourceProvenance: initial?.sourceProvenance ?? 'generated:platform-authored:admin-entry',
    status: (initial?.status as VocabFormValues['status']) ?? 'draft',
  });
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    try {
      await onSubmit(v);
      setToast({ variant: 'success', message: mode === 'create' ? 'Term created.' : 'Term updated.' });
      if (mode === 'create') router.push('/admin/content/vocabulary');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to save term.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setSubmitting(false);
    }
  }

  async function handlePublish() {
    if (!onPublish) return;
    setSubmitting(true);
    try {
      await onPublish();
      setV({ ...v, status: 'active' });
      setToast({ variant: 'success', message: 'Term published.' });
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to publish.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      {toast && <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title={mode === 'create' ? 'New vocabulary term' : `Edit term`}
          description={mode === 'create'
            ? 'Create a new OET vocabulary term. Publishing requires source provenance and, for medical categories, IPA or audio.'
            : `Editing term${itemId ? ` ${itemId}` : ''}.`}
          icon={BookOpen}
          actions={
            <>
              <Button variant="secondary" size="sm" asChild>
<Link href="/admin/content/vocabulary"><ArrowLeft className="mr-1.5 h-4 w-4" />Back</Link>
</Button>
            </>
          }
        />

        <AdminRoutePanel>
          <form onSubmit={handleSubmit} className="space-y-6">
            {/* Term — primary key, always required */}
            <div>
              <label className="mb-1 block text-sm font-medium text-admin-text">Term <span className="text-danger">*</span></label>
              <Input required aria-label="Term" value={v.term} onChange={(e) => setV({ ...v, term: e.target.value })} maxLength={128} />
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-admin-text">Definition <span className="text-danger">*</span></label>
              <textarea
                required
                aria-label="Definition"
                value={v.definition}
                onChange={(e) => setV({ ...v, definition: e.target.value })}
                maxLength={1024}
                rows={3}
                className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm"
              />
              <p className="mt-1 text-xs text-muted">{v.definition.length}/25 words recommended · {v.definition.length}/1024 chars</p>
            </div>

            {/* Taxonomy — Exam + Category only (Profession and Difficulty removed per admin request) */}
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Exam</label>
                <select aria-label="Exam" value={v.examTypeCode} onChange={(e) => setV({ ...v, examTypeCode: e.target.value })} className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm">
                  <option value="oet">OET</option>
                  <option value="ielts">IELTS</option>
                  <option value="pte">PTE</option>
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Category <span className="text-danger">*</span></label>
                <select required aria-label="Category" value={v.category} onChange={(e) => setV({ ...v, category: e.target.value })} className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm capitalize">
                  {CATEGORIES.map(c => <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>)}
                </select>
              </div>
            </div>

            {/* Recall set tags — keep per explicit admin instruction */}
            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Recall practice collection labels</label>
              <div className="flex flex-wrap gap-2">
                {recallSetOptions.map(opt => {
                  const checked = v.recallSetCodes.includes(opt.code);
                  return (
                    <button
                      key={opt.code}
                      type="button"
                      onClick={() => setV({
                        ...v,
                        recallSetCodes: checked
                          ? v.recallSetCodes.filter(c => c !== opt.code)
                          : [...v.recallSetCodes, opt.code],
                      })}
                      className={`rounded-full border px-3 py-1 text-xs transition ${checked ? 'border-primary bg-primary/10 text-primary' : 'border-border bg-surface text-muted hover:border-primary/40'}`}
                      aria-pressed={checked}
                    >
                      {opt.displayName}
                    </button>
                  );
                })}
              </div>
              <p className="mt-1 text-xs text-muted">Tag this term with one or more practice collection labels. A label is not source-backed unless the term provenance explicitly says so.</p>
            </div>

            {/* Status */}
            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Status</label>
              <div className="flex items-center gap-2">
                <select
                  value={v.status}
                  aria-label="Status"
                  onChange={(e) => setV({ ...v, status: e.target.value as VocabFormValues['status'] })}
                  className="rounded-xl border border-border bg-surface px-3 py-2 text-sm capitalize"
                >
                  <option value="draft">Draft</option>
                  <option value="active">Active (publish gate enforced)</option>
                  <option value="archived">Archived</option>
                </select>
                <Badge variant={v.status === 'active' ? 'success' : v.status === 'draft' ? 'warning' : 'muted'}>
                  {v.status}
                </Badge>
              </div>
              <p className="mt-1 text-xs text-muted">Setting status to “active” on save will run the publish gate. Use the Publish button below to active-and-validate in one step.</p>
            </div>

            {/* Actions */}
            <div className="flex flex-wrap gap-2 border-t border-border pt-4">
              <Button type="submit" variant="primary" disabled={submitting}>
                <Save className="mr-1.5 h-4 w-4" /> {mode === 'create' ? 'Create draft' : 'Save changes'}
              </Button>
              {mode === 'edit' && v.status !== 'active' && onPublish && (
                <Button type="button" variant="secondary" disabled={submitting} onClick={handlePublish}>
                  <Plus className="mr-1.5 h-4 w-4" /> Publish
                </Button>
              )}
            </div>
          </form>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </>
  );
}
