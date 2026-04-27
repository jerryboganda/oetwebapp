'use client';

import { useState, type FormEvent } from 'react';
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
  audioUrl: string;
  imageUrl: string;
  synonyms: string[];
  collocations: string[];
  relatedTerms: string[];
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

const PROFESSIONS = [
  { value: '', label: 'General (all)' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
];

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
      <div className="flex flex-wrap gap-2 rounded-xl border border-gray-200 bg-surface p-2">
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
    audioUrl: initial?.audioUrl ?? '',
    imageUrl: initial?.imageUrl ?? '',
    synonyms: initial?.synonyms ?? [],
    collocations: initial?.collocations ?? [],
    relatedTerms: initial?.relatedTerms ?? [],
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
                <label className="mb-1 block text-sm font-medium text-navy">Term <span className="text-red-500">*</span></label>
                <Input required value={v.term} onChange={(e) => setV({ ...v, term: e.target.value })} maxLength={128} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">IPA Pronunciation</label>
                <Input value={v.ipaPronunciation} onChange={(e) => setV({ ...v, ipaPronunciation: e.target.value })} placeholder="/ˈ.../" maxLength={64} />
              </div>
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Definition <span className="text-red-500">*</span></label>
              <textarea
                required
                value={v.definition}
                onChange={(e) => setV({ ...v, definition: e.target.value })}
                maxLength={1024}
                rows={3}
                className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm"
              />
              <p className="mt-1 text-xs text-muted">{v.definition.length}/25 words recommended · {v.definition.length}/1024 chars</p>
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Example sentence <span className="text-red-500">*</span></label>
              <textarea
                required
                value={v.exampleSentence}
                onChange={(e) => setV({ ...v, exampleSentence: e.target.value })}
                maxLength={2048}
                rows={2}
                className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm"
              />
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Context notes</label>
              <textarea
                value={v.contextNotes}
                onChange={(e) => setV({ ...v, contextNotes: e.target.value })}
                maxLength={1024}
                rows={2}
                className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm"
              />
            </div>

            {/* Taxonomy */}
            <div className="grid gap-4 md:grid-cols-4">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Exam</label>
                <select value={v.examTypeCode} onChange={(e) => setV({ ...v, examTypeCode: e.target.value })} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm">
                  <option value="oet">OET</option>
                  <option value="ielts">IELTS</option>
                  <option value="pte">PTE</option>
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Profession</label>
                <select value={v.professionId} onChange={(e) => setV({ ...v, professionId: e.target.value })} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm">
                  {PROFESSIONS.map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Category <span className="text-red-500">*</span></label>
                <select required value={v.category} onChange={(e) => setV({ ...v, category: e.target.value })} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm capitalize">
                  {CATEGORIES.map(c => <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>)}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Difficulty</label>
                <select value={v.difficulty} onChange={(e) => setV({ ...v, difficulty: e.target.value })} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm capitalize">
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

            {/* Provenance + status */}
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Source provenance <span className="text-red-500">*</span> (required to publish)</label>
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
                  <Badge variant={v.status === 'active' ? 'success' : v.status === 'draft' ? 'warning' : 'muted'}>
                    {v.status}
                  </Badge>
                  <span className="text-xs text-muted">
                    Publish via the button below (enforces publish gate).
                  </span>
                </div>
              </div>
            </div>

            {/* Actions */}
            <div className="flex flex-wrap gap-2 border-t border-gray-100 pt-4">
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
