'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Sparkles, RefreshCcw } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { Checkbox } from '@/components/admin/ui/checkbox';
import { Badge } from '@/components/admin/ui/badge';
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
        sourceProvenance: `generated:platform-authored:ai-draft:admin-reviewed:${response.rulebookVersion};reviewedAt=${new Date().toISOString().slice(0, 10)}`,
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
    <>
      {toast && <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />}
      <AdminSettingsLayout
        eyebrow="CMS"
        title="AI-draft vocabulary terms"
        description="Uses the rulebook-grounded AI Gateway (admin.vocabulary_draft feature code, platform-only). Every generated term must cite at least one vocabulary rule ID from the active rulebook. Review and edit before accepting."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Vocabulary', href: '/admin/content/vocabulary' },
          { label: 'AI draft' },
        ]}
        actions={
          <Button variant="secondary" size="sm" asChild>
            <Link href="/admin/content/vocabulary"><ArrowLeft className="mr-1.5 h-4 w-4" />Back</Link>
          </Button>
        }
      >
        <SettingsSection title="Generation parameters">
          <div className="grid gap-3 md:grid-cols-3">
            <Input
              aria-label="Draft count"
              label="Count (1–25)"
              type="number"
              min={1}
              max={25}
              value={count}
              onChange={(e) => setCount(Math.max(1, Math.min(25, parseInt(e.target.value) || 5)))}
            />
            <div className="flex flex-col gap-1.5">
              <label className="text-sm font-medium text-admin-fg-strong">Profession</label>
              <select
                aria-label="Profession"
                value={professionId}
                onChange={(e) => setProfessionId(e.target.value)}
                className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
              >
                <option value="">General (medicine)</option>
                <option value="medicine">Medicine</option>
                <option value="nursing">Nursing</option>
                <option value="dentistry">Dentistry</option>
                <option value="pharmacy">Pharmacy</option>
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-sm font-medium text-admin-fg-strong">Category</label>
              <select
                aria-label="Category"
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)] capitalize"
              >
                {['medical', 'anatomy', 'symptoms', 'procedures', 'pharmacology', 'conditions', 'clinical_communication', 'diagnostics'].map(c => (
                  <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="mt-3">
            <Textarea
              label="Seed hint (optional)"
              value={seedPrompt}
              aria-label="Seed hint"
              onChange={(e) => setSeedPrompt(e.target.value)}
              rows={2}
              maxLength={500}
              placeholder="e.g. 'Focus on respiratory symptoms for asthma patients.'"
            />
          </div>

          <div className="mt-3 flex gap-2">
            <Button variant="primary" disabled={loading} onClick={handleGenerate}>
              <Sparkles className="mr-1.5 h-4 w-4" /> {loading ? 'Generating…' : 'Generate drafts'}
            </Button>
            {response && (
              <Button variant="secondary" disabled={loading} onClick={handleGenerate}>
                <RefreshCcw className="mr-1.5 h-4 w-4" /> Re-generate
              </Button>
            )}
          </div>
        </SettingsSection>

        {response?.warning && (
          <SettingsSection title="Warning">
            <InlineAlert variant="warning">{response.warning}</InlineAlert>
          </SettingsSection>
        )}

        {response && response.drafts.length > 0 && (
          <SettingsSection
            title="Review drafts"
            actions={
              <>
                <Button size="sm" variant="secondary" onClick={() => setSelected(new Set(response.drafts.map((_, i) => i)))}>Select all</Button>
                <Button size="sm" variant="secondary" onClick={() => setSelected(new Set())}>Clear</Button>
                <Button size="sm" variant="primary" disabled={accepting || selected.size === 0} onClick={handleAccept}>
                  {accepting ? 'Accepting…' : `Accept ${selected.size} as drafts`}
                </Button>
              </>
            }
          >
            <div className="mb-3 text-sm text-admin-fg-muted">
              <span className="font-semibold text-admin-fg-strong">{selected.size}</span> / {response.drafts.length} selected ·
              rulebook v{response.rulebookVersion}
            </div>

            <div className="space-y-2">
              {response.drafts.map((d, i) => (
                <Card
                  key={i}
                  surface={selected.has(i) ? 'tinted-primary' : 'default'}
                >
                  <CardContent className="p-4 pt-4">
                    <div className="flex items-start gap-3">
                      <Checkbox
                        checked={selected.has(i)}
                        onCheckedChange={() => toggleSelect(i)}
                        aria-label={`Select ${d.term}`}
                      />
                      <div className="flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-bold text-admin-fg-strong">{d.term}</span>
                          {d.ipaPronunciation && <span className="text-xs italic text-admin-fg-muted">{d.ipaPronunciation}</span>}
                          <Badge variant="default" size="sm" className="capitalize">{d.category.replace(/_/g, ' ')}</Badge>
                        </div>
                        <p className="mt-1 text-sm text-admin-fg-strong">{d.definition}</p>
                        {d.exampleSentence && <p className="mt-1 text-xs italic text-admin-fg-muted">&quot;{d.exampleSentence}&quot;</p>}
                        {d.synonyms.length > 0 && (
                          <div className="mt-2 flex flex-wrap gap-1 items-center">
                            <span className="text-xs text-admin-fg-muted">Synonyms:</span>
                            {d.synonyms.map((s, j) => (
                              <Badge key={j} variant="default" size="sm">{s}</Badge>
                            ))}
                          </div>
                        )}
                        {d.appliedRuleIds.length > 0 && (
                          <div className="mt-2 flex flex-wrap gap-1 items-center">
                            <span className="text-xs text-admin-fg-muted">Cited rules:</span>
                            {d.appliedRuleIds.map((r, j) => (
                              <Badge key={j} variant="primary" size="sm">{r}</Badge>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          </SettingsSection>
        )}
      </AdminSettingsLayout>
    </>
  );
}
