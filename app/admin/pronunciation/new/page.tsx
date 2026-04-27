'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Save, Mic } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Toast } from '@/components/ui/alert';
import {
  AdminRouteHero,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { createAdminPronunciationDrill } from '@/lib/api';

export default function NewPronunciationDrillPage() {
  const router = useRouter();
  const [form, setForm] = useState({
    word: '',
    phoneticTranscription: '',
    profession: 'all',
    focus: 'phoneme',
    primaryRuleId: '',
    difficulty: 'medium',
    tipsHtml: '',
    exampleWordsJson: '[]',
    minimalPairsJson: '[]',
    sentencesJson: '[]',
    audioUrl: '',
  });
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  async function handleSave() {
    setSaving(true);
    try {
      const res = await createAdminPronunciationDrill({
        ...form,
        status: 'draft',
      }) as { id: string };
      router.push(`/admin/pronunciation/${res.id}`);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to create drill' });
    } finally {
      setSaving(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="New pronunciation drill">
      <Link href="/admin/pronunciation" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back to pronunciation drills">
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation drills
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Mic}
        accent="navy"
        title="New pronunciation drill"
        description="Draft a new drill. You can publish after uploading model audio and completing the content."
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap gap-2">
              <Link href="/admin/pronunciation">
                <Button variant="ghost">Cancel</Button>
              </Link>
              <Button variant="primary" onClick={handleSave} loading={saving} className="gap-2">
                <Save className="h-4 w-4" /> Save draft
              </Button>
            </div>
          </div>
        )}
      />

      <PronunciationDrillForm form={form} onChange={setForm} />

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}

type DrillForm = {
  word: string;
  phoneticTranscription: string;
  profession: string;
  focus: string;
  primaryRuleId: string;
  difficulty: string;
  tipsHtml: string;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  audioUrl: string;
};

function PronunciationDrillForm({
  form,
  onChange,
}: {
  form: DrillForm;
  onChange: (next: DrillForm) => void;
}) {
  function set<K extends keyof DrillForm>(key: K, value: DrillForm[K]) {
    onChange({ ...form, [key]: value });
  }
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <Card className="p-5 space-y-3">
        <h2 className="text-sm font-semibold uppercase tracking-[0.15em] text-muted">Metadata</h2>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Label <span className="text-rose-500">*</span></span>
          <input
            type="text"
            value={form.word}
            onChange={(e) => set('word', e.target.value)}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="e.g. th (voiceless) — as in 'think'"
            required
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Target phoneme (IPA) <span className="text-rose-500">*</span></span>
          <input
            type="text"
            value={form.phoneticTranscription}
            onChange={(e) => set('phoneticTranscription', e.target.value)}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="θ, ð, v, ɪ, æ, stress, intonation…"
          />
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Profession</span>
            <select
              value={form.profession}
              onChange={(e) => set('profession', e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="all">All</option>
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
            <span className="text-sm text-navy dark:text-white">Focus</span>
            <select
              value={form.focus}
              onChange={(e) => set('focus', e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="phoneme">Phoneme</option>
              <option value="cluster">Cluster</option>
              <option value="stress">Word stress</option>
              <option value="intonation">Intonation</option>
              <option value="prosody">Prosody</option>
            </select>
          </label>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Difficulty</span>
            <select
              value={form.difficulty}
              onChange={(e) => set('difficulty', e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="easy">Easy</option>
              <option value="medium">Medium</option>
              <option value="hard">Hard</option>
            </select>
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Primary rule ID</span>
            <input
              type="text"
              value={form.primaryRuleId}
              onChange={(e) => set('primaryRuleId', e.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              placeholder="e.g. P01.1"
            />
          </label>
        </div>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Model audio URL</span>
          <input
            type="url"
            value={form.audioUrl}
            onChange={(e) => set('audioUrl', e.target.value)}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="https://…/model.mp3"
          />
        </label>
      </Card>

      <Card className="p-5 space-y-3">
        <h2 className="text-sm font-semibold uppercase tracking-[0.15em] text-muted">Content</h2>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Tips (HTML)</span>
          <textarea
            value={form.tipsHtml}
            onChange={(e) => set('tipsHtml', e.target.value)}
            rows={4}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="<p>…</p>"
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Example words (JSON array of strings)</span>
          <textarea
            value={form.exampleWordsJson}
            onChange={(e) => set('exampleWordsJson', e.target.value)}
            rows={3}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder='["think","therapy","three"]'
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Minimal pairs (JSON)</span>
          <textarea
            value={form.minimalPairsJson}
            onChange={(e) => set('minimalPairsJson', e.target.value)}
            rows={3}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder='[{"a":"think","b":"sink"}]'
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Practice sentences (JSON array of strings)</span>
          <textarea
            value={form.sentencesJson}
            onChange={(e) => set('sentencesJson', e.target.value)}
            rows={3}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder='["Please breathe deeply through the mouth."]'
          />
        </label>
      </Card>
    </div>
  );
}
