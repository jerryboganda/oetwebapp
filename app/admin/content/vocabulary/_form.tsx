'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Plus, Save, X } from 'lucide-react';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { useProfessions } from '@/lib/hooks/use-professions';
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

const OET_SUBTEST_TAGS = [
  { value: 'listening_a', label: 'Listening A' },
  { value: 'listening_b', label: 'Listening B' },
  { value: 'listening_c', label: 'Listening C' },
  { value: 'reading_a', label: 'Reading A' },
  { value: 'reading_b', label: 'Reading B' },
  { value: 'reading_c', label: 'Reading C' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
] as const;

const PROFESSIONS_FALLBACK_HEAD = [{ value: '', label: 'General (all)' }] as const;

type RecallSetOption = { code: string; displayName: string };


function TagInput({ value, onChange, placeholder, label }: { value: string[]; onChange: (v: string[]) => void; placeholder: string; label: string }) {
  const [draft, setDraft] = useState('');
  function add() {
    const v = draft.trim();
    if (!v) return;
    if (!value.includes(v)) onChange([...value, v]);
    setDraft('');
  }
  return (
    <div>
      <label className="mb-1 block text-sm font-medium text-navy">{label}</label>
      <div className="flex flex-wrap gap-2 rounded-xl border border-border bg-surface p-2">
        {value.map((v, i) => (
          <span key={i} className="inline-flex items-center gap-1 rounded-full bg-background-light px-2 py-0.5 text-sm">
            {v}
            <button type="button" onClick={() => onChange(value.filter((_, j) => j !== i))} aria-label={`Remove ${v}`}>
              <X className="h-3 w-3 text-muted" />
            </button>
          </span>
        ))}
        <input
          type="text"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); add(); } }}
          onBlur={add}
          placeholder={placeholder}
          className="flex-1 min-w-[120px] border-0 bg-transparent text-sm outline-none"
        />
      </div>
      <p className="mt-1 text-xs text-muted">Press Enter or comma to add; click × to remove.</p>
    </div>
  );
}

export function VocabularyForm({ mode, initial, onSubmit, onPublish, itemId }: Props) {
  const router = useRouter();
  const { options: professionOptions } = useProfessions();
  const PROFESSIONS = [...PROFESSIONS_FALLBACK_HEAD, ...professionOptions];
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
    sourceProvenance: initial?.sourceProvenance ?? '',
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
              <Link href="/admin/content/vocabulary">
                <Button variant="secondary" size="sm"><ArrowLeft className="mr-1.5 h-4 w-4" />Back</Button>
              </Link>
            </>
          }
        />

        <AdminRoutePanel>
          <form onSubmit={handleSubmit} className="space-y-6">
            {/* Core */}
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Term <span className="text-danger">*</span></label>
                <Input required value={v.term} onChange={(e) => setV({ ...v, term: e.target.value })} maxLength={128} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">IPA Pronunciation</label>
                <Input value={v.ipaPronunciation} onChange={(e) => setV({ ...v, ipaPronunciation: e.target.value })} placeholder="/ˈ.../" maxLength={64} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">American spelling</label>
                <Input value={v.americanSpelling} onChange={(e) => setV({ ...v, americanSpelling: e.target.value })} placeholder="e.g. hemorrhage" maxLength={128} />
              </div>
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Definition <span className="text-danger">*</span></label>
              <textarea
                required
                value={v.definition}
                onChange={(e) => setV({ ...v, definition: e.target.value })}
                maxLength={1024}
                rows={3}
                className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm"
              />
              <p className="mt-1 text-xs text-muted">{v.definition.length}/25 words recommended · {v.definition.length}/1024 chars</p>
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Example sentence <span className="text-danger">*</span></label>
              <textarea
                required
                value={v.exampleSentence}
                onChange={(e) => setV({ ...v, exampleSentence: e.target.value })}
                maxLength={2048}
                rows={2}
                className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm"
              />
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Context notes</label>
              <textarea
                value={v.contextNotes}
                onChange={(e) => setV({ ...v, contextNotes: e.target.value })}
                maxLength={1024}
                rows={2}
                className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm"
              />
            </div>

            {/* Taxonomy */}
            <div className="grid gap-4 md:grid-cols-4">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Exam</label>
                <select value={v.examTypeCode} onChange={(e) => setV({ ...v, examTypeCode: e.target.value })} className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm">
                  <option value="oet">OET</option>
                  <option value="ielts">IELTS</option>
                  <option value="pte">PTE</option>
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Profession</label>
                <select value={v.professionId} onChange={(e) => setV({ ...v, professionId: e.target.value })} className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm">
                  {PROFESSIONS.map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Category <span className="text-danger">*</span></label>
                <select required value={v.category} onChange={(e) => setV({ ...v, category: e.target.value })} className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm capitalize">
                  {CATEGORIES.map(c => <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>)}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Difficulty</label>
                <select value={v.difficulty} onChange={(e) => setV({ ...v, difficulty: e.target.value })} className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm capitalize">
                  <option value="easy">Easy</option>
                  <option value="medium">Medium</option>
                  <option value="hard">Hard</option>
                </select>
              </div>
            </div>

            {/* Media */}
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Audio URL</label>
                <Input value={v.audioUrl} onChange={(e) => setV({ ...v, audioUrl: e.target.value })} placeholder="https://..." />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Slow audio URL</label>
                <Input value={v.audioSlowUrl} onChange={(e) => setV({ ...v, audioSlowUrl: e.target.value })} placeholder="https://..." maxLength={256} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Sentence audio URL</label>
                <Input value={v.audioSentenceUrl} onChange={(e) => setV({ ...v, audioSentenceUrl: e.target.value })} placeholder="https://..." maxLength={256} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Audio media asset ID</label>
                <Input value={v.audioMediaAssetId} onChange={(e) => setV({ ...v, audioMediaAssetId: e.target.value })} placeholder="MediaAsset id" maxLength={64} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Image URL</label>
                <Input value={v.imageUrl} onChange={(e) => setV({ ...v, imageUrl: e.target.value })} placeholder="https://..." />
              </div>
            </div>

            {/* Relations */}
            <div className="grid gap-4 md:grid-cols-3">
              <TagInput label="Synonyms" value={v.synonyms} onChange={(s) => setV({ ...v, synonyms: s })} placeholder="Add synonym…" />
              <TagInput label="Collocations" value={v.collocations} onChange={(s) => setV({ ...v, collocations: s })} placeholder="Add collocation…" />
              <TagInput label="Related terms" value={v.relatedTerms} onChange={(s) => setV({ ...v, relatedTerms: s })} placeholder="Add related term…" />
            </div>

            {/* Recall set tags */}
            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Recall sets (year tags)</label>
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
              <p className="mt-1 text-xs text-muted">Tag this term with one or more recall sets so learners can filter by year.</p>
            </div>

            {/* OET subtest tags */}
            <div>
              <label className="mb-1 block text-sm font-medium text-navy">OET subtest tags</label>
              <div className="flex flex-wrap gap-2">
                {OET_SUBTEST_TAGS.map(opt => {
                  const checked = v.oetSubtestTags.includes(opt.value);
                  return (
                    <button
                      key={opt.value}
                      type="button"
                      onClick={() => setV({
                        ...v,
                        oetSubtestTags: checked
                          ? v.oetSubtestTags.filter(c => c !== opt.value)
                          : [...v.oetSubtestTags, opt.value],
                      })}
                      className={`rounded-full border px-3 py-1 text-xs transition ${checked ? 'border-primary bg-primary/10 text-primary' : 'border-border bg-surface text-muted hover:border-primary/40'}`}
                      aria-pressed={checked}
                    >
                      {opt.label}
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Common mistakes + similar sounding */}
            <div className="grid gap-4 md:grid-cols-2">
              <TagInput label="Common mistakes" value={v.commonMistakes} onChange={(s) => setV({ ...v, commonMistakes: s })} placeholder="e.g. spelt 'embolus' instead of 'embolism'…" />
              <TagInput label="Similar sounding" value={v.similarSounding} onChange={(s) => setV({ ...v, similarSounding: s })} placeholder="e.g. ileum / ilium…" />
            </div>

            {/* Provenance + status */}
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Source provenance <span className="text-danger">*</span> (required to publish)</label>
                <Input
                  value={v.sourceProvenance}
                  onChange={(e) => setV({ ...v, sourceProvenance: e.target.value })}
                  placeholder='e.g. "Admin curation by Dr Hesham 2026-04-20"'
                  maxLength={512}
                />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Status</label>
                <div className="flex items-center gap-2">
                  <select
                    value={v.status}
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
