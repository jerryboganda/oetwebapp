'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Sparkles, CheckCircle2, X, RefreshCcw } from 'lucide-react';
import { AdminDashboardShell } from '@/components/layout';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  requestAdminVocabularyAiDraft,
  acceptAdminVocabularyAiDrafts,
} from '@/lib/api';

type DraftTerm = {
  term: string;
  definition: string;
  exampleSentence: string;
  contextNotes: string | null;
  category: string;
  difficulty: string;
  ipaPronunciation: string | null;
  synonyms: string[];
  collocations: string[];
  relatedTerms: string[];
  appliedRuleIds: string[];
};

type DraftResponse = {
  rulebookVersion: string;
  drafts: DraftTerm[];
  warning: string | null;
};

export default function AdminVocabularyAiDraftPage() {
  const [count, setCount] = useState(5);
  const [examTypeCode] = useState('oet');
  const [professionId, setProfessionId] = useState('');
  const [category, setCategory] = useState('medical');
  const [difficulty, setDifficulty] = useState('medium');
  const [seedPrompt, setSeedPrompt] = useState('');
  const [loading, setLoading] = useState(false);
  const [response, setResponse] = useState<DraftResponse | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [accepting, setAccepting] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  async function handleGenerate() {
    setLoading(true);
    setResponse(null);
    setSelected(new Set());
    try {
      const res = await requestAdminVocabularyAiDraft({
        count,
        examTypeCode,
        professionId: professionId || null,
        category,
        difficulty,
        seedPrompt,
      });
      const d = res as DraftResponse;
      setResponse(d);
      setSelected(new Set(d.drafts.map((_, i) => i)));
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Generate failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setLoading(false);
    }
  }

  async function handleAccept() {
    if (!response || selected.size === 0) return;
    setAccepting(true);
    try {
      const acceptedDrafts = response.drafts
        .map((d, i) => (selected.has(i) ? d : null))
        .filter((d): d is DraftTerm => d !== null);
      const res = await acceptAdminVocabularyAiDrafts({
        examTypeCode,
        professionId: professionId || null,
        sourceProvenance: `AI draft v${response.rulebookVersion} reviewed by admin on ${new Date().toISOString().slice(0, 10)}`,
        drafts: acceptedDrafts,
      });
      const r = res as { createdIds: string[]; count: number };
      setToast({ variant: 'success', message: `Accepted ${r.count} draft${r.count === 1 ? '' : 's'}.` });
      setResponse(null);
      setSelected(new Set());
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Accept failed.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setAccepting(false);
    }
  }

  function toggleSelect(i: number) {
    setSelected(prev => {
      const s = new Set(prev);
      if (s.has(i)) s.delete(i); else s.add(i);
      return s;
    });
  }

  return (
    <AdminDashboardShell>
      {toast && <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="AI-draft vocabulary terms"
          description="Uses the rulebook-grounded AI Gateway (admin.vocabulary_draft feature code, platform-only). Every generated term must cite at least one vocabulary rule ID from the active rulebook. Review and edit before accepting."
          icon={Sparkles}
          actions={
            <Link href="/admin/content/vocabulary">
              <Button variant="secondary" size="sm"><ArrowLeft className="mr-1.5 h-4 w-4" />Back</Button>
            </Link>
          }
        />

        <AdminRoutePanel>
          <div className="space-y-4">
            <div className="grid gap-3 md:grid-cols-4">
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Count (1–25)</label>
                <Input type="number" min={1} max={25} value={count} onChange={(e) => setCount(Math.max(1, Math.min(25, parseInt(e.target.value) || 5)))} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Profession</label>
                <select value={professionId} onChange={(e) => setProfessionId(e.target.value)} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm">
                  <option value="">General (medicine)</option>
                  <option value="medicine">Medicine</option>
                  <option value="nursing">Nursing</option>
                  <option value="dentistry">Dentistry</option>
                  <option value="pharmacy">Pharmacy</option>
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Category</label>
                <select value={category} onChange={(e) => setCategory(e.target.value)} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm capitalize">
                  {['medical', 'anatomy', 'symptoms', 'procedures', 'pharmacology', 'conditions', 'clinical_communication', 'diagnostics'].map(c => (
                    <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-navy">Difficulty</label>
                <select value={difficulty} onChange={(e) => setDifficulty(e.target.value)} className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm">
                  <option value="easy">Easy</option>
                  <option value="medium">Medium</option>
                  <option value="hard">Hard</option>
                </select>
              </div>
            </div>

            <div>
              <label className="mb-1 block text-sm font-medium text-navy">Seed hint (optional)</label>
              <textarea
                value={seedPrompt}
                onChange={(e) => setSeedPrompt(e.target.value)}
                rows={2}
                maxLength={500}
                placeholder="e.g. 'Focus on respiratory symptoms for asthma patients.'"
                className="w-full rounded-xl border border-gray-200 bg-surface px-3 py-2 text-sm"
              />
            </div>

            <div className="flex gap-2">
              <Button variant="primary" disabled={loading} onClick={handleGenerate}>
                <Sparkles className="mr-1.5 h-4 w-4" /> {loading ? 'Generating…' : 'Generate drafts'}
              </Button>
              {response && (
                <Button variant="secondary" disabled={loading} onClick={handleGenerate}>
                  <RefreshCcw className="mr-1.5 h-4 w-4" /> Re-generate
                </Button>
              )}
            </div>

            {response?.warning && <InlineAlert variant="warning">{response.warning}</InlineAlert>}

            {response && response.drafts.length > 0 && (
              <div className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="text-sm text-muted">
                    <span className="font-semibold text-navy">{selected.size}</span> / {response.drafts.length} selected ·
                    rulebook v{response.rulebookVersion}
                  </div>
                  <div className="flex gap-2">
                    <Button size="sm" variant="secondary" onClick={() => setSelected(new Set(response.drafts.map((_, i) => i)))}>Select all</Button>
                    <Button size="sm" variant="secondary" onClick={() => setSelected(new Set())}>Clear</Button>
                    <Button size="sm" variant="primary" disabled={accepting || selected.size === 0} onClick={handleAccept}>
                      {accepting ? 'Accepting…' : `Accept ${selected.size} as drafts`}
                    </Button>
                  </div>
                </div>

                <div className="space-y-2">
                  {response.drafts.map((d, i) => (
                    <div key={i} className={`rounded-2xl border p-4 ${selected.has(i) ? 'border-primary/40 bg-primary/5' : 'border-gray-200 bg-surface'}`}>
                      <div className="flex items-start gap-3">
                        <input
                          type="checkbox"
                          checked={selected.has(i)}
                          onChange={() => toggleSelect(i)}
                          className="mt-1.5 h-4 w-4"
                          aria-label={`Select ${d.term}`}
                        />
                        <div className="flex-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-bold text-navy">{d.term}</span>
                            {d.ipaPronunciation && <span className="text-xs italic text-muted">{d.ipaPronunciation}</span>}
                            <span className="rounded-full bg-background-light px-2 py-0.5 text-xs capitalize text-muted">{d.category.replace(/_/g, ' ')}</span>
                            <span className="rounded-full bg-background-light px-2 py-0.5 text-xs capitalize text-muted">{d.difficulty}</span>
                          </div>
                          <p className="mt-1 text-sm text-navy">{d.definition}</p>
                          {d.exampleSentence && <p className="mt-1 text-xs italic text-muted">&quot;{d.exampleSentence}&quot;</p>}
                          {d.synonyms.length > 0 && (
                            <div className="mt-2 flex flex-wrap gap-1">
                              <span className="text-xs text-muted">Synonyms:</span>
                              {d.synonyms.map((s, j) => (
                                <span key={j} className="rounded-full bg-gray-100 px-2 py-0.5 text-xs">{s}</span>
                              ))}
                            </div>
                          )}
                          {d.appliedRuleIds.length > 0 && (
                            <div className="mt-2 flex flex-wrap gap-1">
                              <span className="text-xs text-muted">Cited rules:</span>
                              {d.appliedRuleIds.map((r, j) => (
                                <span key={j} className="rounded-full border border-primary/30 bg-primary/5 px-2 py-0.5 text-xs text-primary">{r}</span>
                              ))}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </AdminDashboardShell>
  );
}
