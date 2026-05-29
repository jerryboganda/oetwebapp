'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Save } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Toast } from '@/components/ui/alert';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { createAdminPronunciationDrill } from '@/lib/api';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Pronunciation', href: '/admin/content/pronunciation' },
  { label: 'New drill' },
];

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
      router.push(`/admin/content/pronunciation/${res.id}`);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to create drill' });
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <AdminSettingsLayout
        title="New pronunciation drill"
        description="Draft a new drill. You can publish after uploading model audio and completing the content."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" asChild>
              <Link href="/admin/content/pronunciation">Cancel</Link>
            </Button>
            <Button size="sm" onClick={handleSave} disabled={saving}>
              <Save className="mr-2 h-4 w-4" /> {saving ? 'Saving…' : 'Save draft'}
            </Button>
          </div>
        }
      >
        <PronunciationDrillForm form={form} onChange={setForm} />
      </AdminSettingsLayout>

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </>
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
      <Card>
        <CardHeader>
          <CardTitle>Metadata</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Label <span className="text-[var(--admin-danger)]">*</span></span>
            <input
              type="text"
              value={form.word}
              onChange={(e) => set('word', e.target.value)}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder="e.g. th (voiceless), as in 'think'"
              required
            />
          </label>
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Target phoneme (IPA) <span className="text-[var(--admin-danger)]">*</span></span>
            <input
              type="text"
              value={form.phoneticTranscription}
              onChange={(e) => set('phoneticTranscription', e.target.value)}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder="θ, ð, v, ɪ, æ, stress, intonation…"
            />
          </label>
          <div className="grid grid-cols-2 gap-3">
            <label className="block">
              <span className="text-sm text-admin-fg-strong">Profession</span>
              <select
                value={form.profession}
                onChange={(e) => set('profession', e.target.value)}
                className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
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
              <span className="text-sm text-admin-fg-strong">Focus</span>
              <select
                value={form.focus}
                onChange={(e) => set('focus', e.target.value)}
                className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
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
              <span className="text-sm text-admin-fg-strong">Difficulty</span>
              <select
                value={form.difficulty}
                onChange={(e) => set('difficulty', e.target.value)}
                className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              >
                <option value="easy">Easy</option>
                <option value="medium">Medium</option>
                <option value="hard">Hard</option>
              </select>
            </label>
            <label className="block">
              <span className="text-sm text-admin-fg-strong">Primary rule ID</span>
              <input
                type="text"
                value={form.primaryRuleId}
                onChange={(e) => set('primaryRuleId', e.target.value)}
                className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
                placeholder="e.g. P01.1"
              />
            </label>
          </div>
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Model audio URL</span>
            <input
              type="url"
              value={form.audioUrl}
              onChange={(e) => set('audioUrl', e.target.value)}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder="https://…/model.mp3"
            />
          </label>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Content</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Tips (HTML)</span>
            <textarea
              value={form.tipsHtml}
              onChange={(e) => set('tipsHtml', e.target.value)}
              rows={4}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder="<p>…</p>"
            />
          </label>
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Example words (JSON array of strings)</span>
            <textarea
              value={form.exampleWordsJson}
              onChange={(e) => set('exampleWordsJson', e.target.value)}
              rows={3}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder='["think","therapy","three"]'
            />
          </label>
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Minimal pairs (JSON)</span>
            <textarea
              value={form.minimalPairsJson}
              onChange={(e) => set('minimalPairsJson', e.target.value)}
              rows={3}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder='[{"a":"think","b":"sink"}]'
            />
          </label>
          <label className="block">
            <span className="text-sm text-admin-fg-strong">Practice sentences (JSON array of strings)</span>
            <textarea
              value={form.sentencesJson}
              onChange={(e) => set('sentencesJson', e.target.value)}
              rows={3}
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
              placeholder='["Please breathe deeply through the mouth."]'
            />
          </label>
        </CardContent>
      </Card>
    </div>
  );
}
