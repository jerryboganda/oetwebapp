'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Sparkles, Save } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import {
  adminPronunciationAiDraft,
  createAdminPronunciationDrill,
} from '@/lib/api';

type DraftResult = {
  targetPhoneme: string;
  label: string;
  difficulty: string;
  focus: string;
  exampleWords: string[];
  minimalPairs: Array<{ a: string; b: string }>;
  sentences: string[];
  tipsHtml: string;
  appliedRuleIds: string[];
  primaryRuleId: string | null;
  warning: string | null;
  selfCheckNotes: string | null;
};

export default function PronunciationAiDraftPage() {
  const router = useRouter();
  const [phoneme, setPhoneme] = useState('θ');
  const [focus, setFocus] = useState('phoneme');
  const [profession, setProfession] = useState('medicine');
  const [difficulty, setDifficulty] = useState('medium');
  const [prompt, setPrompt] = useState('');
  const [primaryRuleId, setPrimaryRuleId] = useState('');
  const [generating, setGenerating] = useState(false);
  const [saving, setSaving] = useState(false);
  const [draft, setDraft] = useState<DraftResult | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  async function generate() {
    setGenerating(true);
    setDraft(null);
    try {
      const res = (await adminPronunciationAiDraft({
        phoneme,
        focus,
        profession,
        difficulty,
        prompt,
        primaryRuleId,
      })) as DraftResult;
      setDraft(res);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'AI draft failed' });
    } finally {
      setGenerating(false);
    }
  }

  async function saveAsDrill() {
    if (!draft) return;
    setSaving(true);
    try {
      const saved = (await createAdminPronunciationDrill({
        word: draft.label,
        phoneticTranscription: draft.targetPhoneme,
        profession,
        focus: draft.focus,
        primaryRuleId: draft.primaryRuleId,
        difficulty: draft.difficulty,
        tipsHtml: draft.tipsHtml,
        exampleWordsJson: JSON.stringify(draft.exampleWords),
        minimalPairsJson: JSON.stringify(draft.minimalPairs),
        sentencesJson: JSON.stringify(draft.sentences),
        status: 'draft',
      })) as { id: string };
      router.push(`/admin/content/pronunciation/${saved.id}`);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed' });
    } finally {
      setSaving(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="AI draft pronunciation drill">
      <Link href="/admin/content/pronunciation" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back to pronunciation drills">
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation drills
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Sparkles}
        accent="navy"
        title="AI draft a pronunciation drill"
        description="Grounded in the pronunciation rulebook and canonical OET scoring. Drafts are saved as draft; publishing requires admin review."
      />

      <AdminRoutePanel title="Parameters">
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Target phoneme / IPA</span>
            <input
              type="text"
              value={phoneme}
              onChange={(e) => setPhoneme(e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Focus</span>
            <select
              value={focus}
              onChange={(e) => setFocus(e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="phoneme">Phoneme</option>
              <option value="cluster">Cluster</option>
              <option value="stress">Word stress</option>
              <option value="intonation">Intonation</option>
              <option value="prosody">Prosody</option>
            </select>
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Profession</span>
            <select
              value={profession}
              onChange={(e) => setProfession(e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="medicine">Medicine</option>
              <option value="nursing">Nursing</option>
              <option value="dentistry">Dentistry</option>
              <option value="pharmacy">Pharmacy</option>
              <option value="physiotherapy">Physiotherapy</option>
              <option value="occupational-therapy">Occupational therapy</option>
              <option value="speech-pathology">Speech pathology</option>
            </select>
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Difficulty</span>
            <select
              value={difficulty}
              onChange={(e) => setDifficulty(e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="easy">Easy</option>
              <option value="medium">Medium</option>
              <option value="hard">Hard</option>
            </select>
          </label>
          <label className="block col-span-2">
            <span className="text-sm text-navy dark:text-white">Primary rule ID (optional, e.g. P01.1)</span>
            <input
              type="text"
              value={primaryRuleId}
              onChange={(e) => setPrimaryRuleId(e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </label>
          <label className="block col-span-2">
            <span className="text-sm text-navy dark:text-white">Prompt (optional)</span>
            <textarea
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={3}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              placeholder="Describe any extra context you'd like the AI to consider (e.g. 'for nurses confusing /v/ and /w/')."
            />
          </label>
        </div>
        <Button variant="primary" onClick={generate} loading={generating} className="gap-2">
          <Sparkles className="h-4 w-4" /> Generate draft
        </Button>
      </AdminRoutePanel>

      {draft && (
        <AdminRoutePanel
          title="Generated draft"
          actions={(
            <Button variant="primary" onClick={saveAsDrill} loading={saving} className="gap-2">
              <Save className="h-4 w-4" /> Save as draft drill
            </Button>
          )}
        >
          {draft.warning && <InlineAlert variant="warning">{draft.warning}</InlineAlert>}
          <dl className="grid grid-cols-1 gap-2 text-sm md:grid-cols-2">
            <DraftRow label="Label" value={draft.label} />
            <DraftRow label="Phoneme" value={`/${draft.targetPhoneme}/`} mono />
            <DraftRow label="Focus" value={draft.focus} />
            <DraftRow label="Difficulty" value={draft.difficulty} />
            <DraftRow label="Applied rule IDs" value={draft.appliedRuleIds.join(', ') || '—'} mono />
            <DraftRow label="Primary rule" value={draft.primaryRuleId ?? '—'} mono />
          </dl>
          <div>
            <span className="text-xs uppercase tracking-[0.15em] text-muted">Example words</span>
            <div className="mt-1 flex flex-wrap gap-1">
              {draft.exampleWords.map((w) => (
                <span key={w} className="rounded-full bg-background-light px-2 py-0.5 text-xs">{w}</span>
              ))}
            </div>
          </div>
          {draft.minimalPairs.length > 0 && (
            <div>
              <span className="text-xs uppercase tracking-[0.15em] text-muted">Minimal pairs</span>
              <div className="mt-1 flex flex-wrap gap-1">
                {draft.minimalPairs.map((p, i) => (
                  <span key={i} className="rounded-full bg-background-light px-2 py-0.5 text-xs">
                    {p.a} vs {p.b}
                  </span>
                ))}
              </div>
            </div>
          )}
          <div>
            <span className="text-xs uppercase tracking-[0.15em] text-muted">Practice sentences</span>
            <ul className="mt-1 space-y-1 text-sm">
              {draft.sentences.map((s, i) => (
                <li key={i} className="italic text-muted">{s}</li>
              ))}
            </ul>
          </div>
          {draft.tipsHtml && (
            <div>
              <span className="text-xs uppercase tracking-[0.15em] text-muted">Tips</span>
              <div className="prose prose-sm mt-1 max-w-none" dangerouslySetInnerHTML={{ __html: draft.tipsHtml }} />
            </div>
          )}
        </AdminRoutePanel>
      )}

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}

function DraftRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex items-baseline gap-2">
      <dt className="text-xs uppercase tracking-[0.15em] text-muted w-32 shrink-0">{label}</dt>
      <dd className={`text-navy dark:text-white ${mono ? 'font-mono text-xs' : ''}`}>{value}</dd>
    </div>
  );
}
