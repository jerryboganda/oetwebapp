'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Save, Archive, Rocket, Undo2, Mic, Wand2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  AdminRouteHero,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import {
  fetchAdminPronunciationDrill,
  updateAdminPronunciationDrill,
  archiveAdminPronunciationDrill,
  generateAdminPronunciationModelAudio,
  type AdminPronunciationGenerateAudioResponse,
} from '@/lib/api';
import {
  PronunciationDrillForm,
  type DrillForm,
} from '@/components/domain/pronunciation/admin-pronunciation-drill-form';

type FullDrill = {
  id: string;
  label: string;
  targetPhoneme: string;
  profession: string;
  focus: string;
  primaryRuleId: string | null;
  audioModelUrl: string | null;
  audioModelAssetId: string | null;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  tipsHtml: string;
  difficulty: string;
  status: string;
};

export default function EditPronunciationDrillPage() {
  const params = useParams<{ drillId: string }>();
  const drillId = params?.drillId ?? '';
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [drill, setDrill] = useState<FullDrill | null>(null);
  const [form, setForm] = useState<DrillForm>({
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

  // ── Generate model reference audio (TTS) state ────────────────────────────
  const [generating, setGenerating] = useState(false);
  const [generateText, setGenerateText] = useState('');
  const [generateVoiceId, setGenerateVoiceId] = useState('');
  const [generatedAudio, setGeneratedAudio] =
    useState<AdminPronunciationGenerateAudioResponse | null>(null);

  const load = useCallback(async () => {
    if (!drillId) return;
    setLoading(true);
    try {
      const d = (await fetchAdminPronunciationDrill(drillId)) as FullDrill;
      setDrill(d);
      setForm({
        word: d.label,
        phoneticTranscription: d.targetPhoneme,
        profession: d.profession,
        focus: d.focus,
        primaryRuleId: d.primaryRuleId ?? '',
        difficulty: d.difficulty,
        tipsHtml: d.tipsHtml,
        exampleWordsJson: d.exampleWordsJson,
        minimalPairsJson: d.minimalPairsJson,
        sentencesJson: d.sentencesJson,
        audioUrl: d.audioModelUrl ?? '',
      });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load drill' });
    } finally {
      setLoading(false);
    }
  }, [drillId]);

  useEffect(() => { void load(); }, [load]);

  async function doSave(desiredStatus?: string) {
    if (!drill) return;
    setSaving(true);
    try {
      await updateAdminPronunciationDrill(drill.id, {
        ...form,
        status: desiredStatus ?? drill.status,
      });
      setToast({ variant: 'success', message: desiredStatus ? `Drill ${desiredStatus}` : 'Drill saved' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed' });
    } finally {
      setSaving(false);
    }
  }

  async function doArchive() {
    if (!drill) return;
    if (!confirm('Archive this drill?')) return;
    try {
      await archiveAdminPronunciationDrill(drill.id);
      router.push('/admin/content/pronunciation');
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Archive failed' });
    }
  }

  async function doGenerateAudio() {
    if (!drill) return;
    const text = generateText.trim();
    if (!text) {
      setToast({ variant: 'error', message: 'Enter the text the model voice should read.' });
      return;
    }
    setGenerating(true);
    try {
      const res = await generateAdminPronunciationModelAudio(drill.id, {
        text,
        voiceId: generateVoiceId.trim() || undefined,
      });
      setGeneratedAudio(res);
      // Persist the new media-asset URL into the editable audioUrl field so the
      // next "Save drill" survives reload even if the backend FK write is rolled
      // back. The backend service already wrote AudioModelAssetId server-side.
      setForm((prev) => ({ ...prev, audioUrl: res.url }));
      setToast({
        variant: 'success',
        message: `Generated ${Math.round(res.bytes / 1024)} KB via ${res.providerName} — save to keep the URL.`,
      });
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof Error ? err.message : 'Failed to generate model audio',
      });
    } finally {
      setGenerating(false);
    }
  }

  if (loading) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Edit pronunciation drill">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-96 rounded-2xl" />
      </AdminRouteWorkspace>
    );
  }

  if (!drill) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Edit pronunciation drill">
        <Link href="/admin/content/pronunciation" className="text-sm text-primary hover:underline">← Back</Link>
        <p className="text-muted">Drill not found.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Edit pronunciation drill">
      <Link href="/admin/content/pronunciation" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back to pronunciation drills">
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation drills
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Mic}
        accent="navy"
        title={drill.label}
        description={`Phoneme /${drill.targetPhoneme}/${drill.primaryRuleId ? ` • Rule ${drill.primaryRuleId}` : ''} • Status ${drill.status}`}
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={drill.status === 'active' ? 'success' : drill.status === 'archived' ? 'muted' : 'warning'}>
                {drill.status}
              </Badge>
              <Button
                variant="secondary"
                onClick={() => doSave()}
                loading={saving}
                className="gap-2"
              >
                <Save className="h-4 w-4" /> Save
              </Button>
              {drill.status === 'active' ? (
                <Button variant="secondary" onClick={() => doSave('draft')} loading={saving} className="gap-2">
                  <Undo2 className="h-4 w-4" /> Unpublish
                </Button>
              ) : drill.status !== 'archived' ? (
                <Button variant="primary" onClick={() => doSave('active')} loading={saving} className="gap-2">
                  <Rocket className="h-4 w-4" /> Publish
                </Button>
              ) : null}
              {drill.status !== 'archived' && (
                <Button variant="destructive" onClick={doArchive} className="gap-2">
                  <Archive className="h-4 w-4" /> Archive
                </Button>
              )}
            </div>
          </div>
        )}
      />

      <PronunciationDrillForm form={form} onChange={setForm} />

      <section
        aria-label="Generate model reference audio"
        className="rounded-2xl border border-border bg-background-light p-5 shadow-sm"
      >
        <header className="mb-3 flex items-center gap-2">
          <Wand2 className="h-4 w-4 text-primary" />
          <h2 className="text-sm font-semibold uppercase tracking-[0.15em] text-muted">
            Generate model reference audio
          </h2>
        </header>
        <p className="mb-3 text-sm text-muted">
          Synthesises the audio learners mimic via the configured TTS provider
          (Azure / ElevenLabs / CosyVoice / ChatTTS / GPT-SoVITS). Falls back to
          a clear error if only mock TTS is available — never publishes a silent
          file. The new asset is linked to this drill automatically.
        </p>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-[1fr_220px]">
          <label className="block">
            <span className="text-sm text-navy dark:text-white">
              Text to read <span className="text-rose-500">*</span>
            </span>
            <textarea
              value={generateText}
              onChange={(e) => setGenerateText(e.target.value)}
              rows={3}
              maxLength={5000}
              placeholder={
                drill.label
                  ? `e.g. ${drill.label} — please breathe deeply through the mouth.`
                  : 'e.g. think, therapy, three.'
              }
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Voice ID (optional)</span>
            <input
              type="text"
              value={generateVoiceId}
              onChange={(e) => setGenerateVoiceId(e.target.value)}
              placeholder="provider default"
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </label>
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-3">
          <Button
            variant="primary"
            onClick={doGenerateAudio}
            loading={generating}
            disabled={generating || !generateText.trim()}
            className="gap-2"
          >
            <Wand2 className="h-4 w-4" /> Generate audio
          </Button>
          {generatedAudio && (
            <div className="flex flex-wrap items-center gap-3 text-xs text-muted">
              <Badge variant="success">{generatedAudio.providerName}</Badge>
              <span>{Math.round(generatedAudio.bytes / 1024)} KB</span>
              <span>{(generatedAudio.durationMs / 1000).toFixed(1)}s</span>
              <span className="font-mono">{generatedAudio.sha256.slice(0, 12)}…</span>
            </div>
          )}
        </div>
        {generatedAudio && (
          <audio
            controls
            preload="metadata"
            src={generatedAudio.url}
            className="mt-3 w-full"
            aria-label="Generated model reference audio preview"
          />
        )}
      </section>

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
