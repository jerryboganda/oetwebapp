'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  AudioWaveform,
  ChevronDown,
  ChevronRight,
  Loader2,
  Lock,
  Mic2,
  RefreshCw,
  Settings2,
  Sparkles,
  Star,
  Volume2,
  XCircle,
} from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import {
  getElevenLabsVoices,
  previewAdminVoiceDesign,
  regenerateAllAudio,
  getAudioRegenerationBatches,
  cancelAudioRegenerationBatch,
  retryAudioRegenerationBatch,
  getAdminVoiceDesignConfig,
  saveAdminVoiceDesignConfig,
  uploadElevenLabsPronunciationDictionary,
  type AdminElevenLabsVoice,
  type AdminAudioBatch,
} from '@/lib/api';

/* ─── Types ─── */
interface VoiceDesignSample {
  id: string;
  text: string;
  audioUrl: string | null;
  loading: boolean;
  error: string | null;
  rating: number;
}

type AudioBatch = AdminAudioBatch;

interface ElevenLabsSettings {
  apiKey: string;
  apiKeyPresent: boolean;
  baseUrl: string;
  voiceId: string;
  model: string;
  outputFormat: string;
  dictionaryId: string;
  dictionaryVersionId: string;
  stability: number;
  similarityBoost: number;
  style: number;
  useSpeakerBoost: boolean;
}

/* ─── Constants ─── */
const OET_SAMPLES = [
  'Good morning, Mrs Johnson. I\'m going to check your blood pressure today.',
  'The patient presents with acute respiratory distress requiring immediate intervention.',
  'Could you tell me about any allergies or medications you\'re currently taking?',
  'I\'d like to refer you to a specialist for further investigation.',
  'The results show elevated levels which may indicate an underlying condition.',
];

const DEFAULT_ELEVEN_SETTINGS: ElevenLabsSettings = {
  apiKey: '',
  apiKeyPresent: false,
  baseUrl: 'https://api.elevenlabs.io/v1',
  voiceId: 'auq43ws1oslv0tO4BDa7',
  model: 'eleven_multilingual_v2',
  outputFormat: 'mp3_44100_128',
  dictionaryId: '',
  dictionaryVersionId: '',
  stability: 0.45,
  similarityBoost: 0.85,
  style: 0,
  useSpeakerBoost: true,
};

/* ─── Section Collapse Hook ─── */
function useCollapsible(initial = true) {
  const [open, setOpen] = useState(initial);
  const toggle = useCallback(() => setOpen((v) => !v), []);
  return { open, toggle };
}

/* ─── Page Component ─── */
export default function AdminVoiceDesignPage() {
  // Voice browser (ElevenLabs catalogue)
  const [voices, setVoices] = useState<AdminElevenLabsVoice[] | null>(null);
  const [probing, setProbing] = useState(false);
  const [playingVoiceId, setPlayingVoiceId] = useState<string | null>(null);

  // Preview state
  const [samples, setSamples] = useState<VoiceDesignSample[]>([]);
  const [generatingSamples, setGeneratingSamples] = useState(false);
  const [sampleProgress, setSampleProgress] = useState(0);
  const [customSampleText, setCustomSampleText] = useState('');

  // Regeneration state
  const [audioType, setAudioType] = useState<'all' | 'listening' | 'vocabulary' | 'recalls'>('recalls');
  const [scope, setScope] = useState<'all' | 'missing' | 'different-voice'>('all');
  const [dryRunResult, setDryRunResult] = useState<{ count: number } | null>(null);
  const [regenerating, setRegenerating] = useState(false);
  const [confirmModal, setConfirmModal] = useState(false);

  // ElevenLabs settings
  const [elevenSettings, setElevenSettings] = useState<ElevenLabsSettings>(DEFAULT_ELEVEN_SETTINGS);
  const [savedElevenSettings, setSavedElevenSettings] = useState<ElevenLabsSettings>(DEFAULT_ELEVEN_SETTINGS);
  const [dictionaryFile, setDictionaryFile] = useState<File | null>(null);
  const [savingEleven, setSavingEleven] = useState(false);
  const [uploadingDictionary, setUploadingDictionary] = useState(false);

  // Job tracking
  const [batches, setBatches] = useState<AudioBatch[]>([]);
  const [loadingBatches, setLoadingBatches] = useState(false);

  // Toast
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  // Section collapse
  const voiceBrowser = useCollapsible(true);
  const previewPanel = useCollapsible(true);
  const recallAudio = useCollapsible(true);
  const bulkRegen = useCollapsible(true);
  const jobTracker = useCollapsible(true);

  // Refs
  const audioRef = useRef<HTMLAudioElement | null>(null); // voice-browser transient preview only
  const samplesRef = useRef<VoiceDesignSample[]>([]);
  const refreshIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Keep a ref of the latest samples so cleanup can revoke their object URLs.
  useEffect(() => { samplesRef.current = samples; }, [samples]);

  // Revoke any outstanding audio object URLs on unmount (avoid leaks).
  useEffect(() => () => {
    samplesRef.current.forEach((s) => { if (s.audioUrl) URL.revokeObjectURL(s.audioUrl); });
    if (audioRef.current?.src) URL.revokeObjectURL(audioRef.current.src);
  }, []);

  // ─── Toast auto-dismiss ───
  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 4000);
    return () => clearTimeout(t);
  }, [toast]);

  // ─── Auto-refresh batches ───
  const fetchBatches = useCallback(async () => {
    setLoadingBatches(true);
    try {
      const data = await getAudioRegenerationBatches();
      setBatches(data.batches);
    } catch {
      /* silent background fetch */
    } finally {
      setLoadingBatches(false);
    }
  }, []);

  useEffect(() => {
    void fetchBatches();
    refreshIntervalRef.current = setInterval(() => void fetchBatches(), 5000);
    return () => {
      if (refreshIntervalRef.current) clearInterval(refreshIntervalRef.current);
    };
  }, [fetchBatches]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const config = await getAdminVoiceDesignConfig();
        if (cancelled) return;
        const loaded: ElevenLabsSettings = {
          apiKey: '',
          apiKeyPresent: config.elevenLabsApiKeyPresent,
          baseUrl: config.elevenLabsTtsBaseUrl || DEFAULT_ELEVEN_SETTINGS.baseUrl,
          voiceId: config.elevenLabsDefaultVoiceId || DEFAULT_ELEVEN_SETTINGS.voiceId,
          model: config.elevenLabsModel || DEFAULT_ELEVEN_SETTINGS.model,
          outputFormat: config.elevenLabsOutputFormat || DEFAULT_ELEVEN_SETTINGS.outputFormat,
          dictionaryId: config.elevenLabsPronunciationDictionaryId ?? '',
          dictionaryVersionId: config.elevenLabsPronunciationDictionaryVersionId ?? '',
          stability: config.elevenLabsStability,
          similarityBoost: config.elevenLabsSimilarityBoost,
          style: config.elevenLabsStyle,
          useSpeakerBoost: config.elevenLabsUseSpeakerBoost,
        };
        setElevenSettings(loaded);
        setSavedElevenSettings(loaded);
      } catch {
        setToast({ variant: 'error', message: 'Failed to load voice settings' });
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const updateElevenSettings = useCallback(<K extends keyof ElevenLabsSettings>(
    key: K,
    value: ElevenLabsSettings[K],
  ) => {
    setElevenSettings((prev) => ({ ...prev, [key]: value }));
  }, []);

  const buildRegenerationPayload = useCallback((dryRun: boolean) => ({
    audioType,
    scope,
    dryRun,
    providerName: 'elevenlabs',
    modelVariant: elevenSettings.model,
    voiceId: elevenSettings.voiceId,
    forceRegenerate: scope === 'all',
  } as const), [audioType, scope, elevenSettings.model, elevenSettings.voiceId]);

  // ─── Handlers ───
  const handleFetchVoices = useCallback(async () => {
    setProbing(true);
    try {
      const result = await getElevenLabsVoices();
      setVoices(result.voices);
      setToast({ variant: 'success', message: `Loaded ${result.voices.length} ElevenLabs voices` });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to fetch voices' });
    } finally {
      setProbing(false);
    }
  }, []);

  const handlePreviewVoice = useCallback(async (voiceId: string) => {
    try {
      setPlayingVoiceId(voiceId);
      const blob = await previewAdminVoiceDesign({ voiceId, text: OET_SAMPLES[0] });
      if (audioRef.current) {
        audioRef.current.pause();
        URL.revokeObjectURL(audioRef.current.src);
      }
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      audioRef.current = audio;
      audio.onended = () => setPlayingVoiceId(null);
      await audio.play();
    } catch (err) {
      setPlayingVoiceId(null);
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Preview failed' });
    }
  }, []);

  const handleGenerateSamples = useCallback(async () => {
    const texts = [...OET_SAMPLES];
    if (customSampleText.trim()) texts.push(customSampleText.trim());

    // Free the previous batch's audio URLs before creating a new one.
    samplesRef.current.forEach((s) => { if (s.audioUrl) URL.revokeObjectURL(s.audioUrl); });

    const initial: VoiceDesignSample[] = texts.map((text, i) => ({
      id: `sample-${i}`,
      text,
      audioUrl: null,
      loading: true,
      error: null,
      rating: 0,
    }));
    setSamples(initial);
    setGeneratingSamples(true);
    setSampleProgress(0);

    for (let i = 0; i < texts.length; i++) {
      try {
        const blob = await previewAdminVoiceDesign({ voiceId: elevenSettings.voiceId, text: texts[i] });
        const url = URL.createObjectURL(blob);
        setSamples((prev) =>
          prev.map((s, idx) => (idx === i ? { ...s, audioUrl: url, loading: false } : s)),
        );
      } catch {
        setSamples((prev) =>
          prev.map((s, idx) => (idx === i ? { ...s, loading: false, error: 'Generation failed' } : s)),
        );
      }
      setSampleProgress(i + 1);
    }
    setGeneratingSamples(false);
  }, [elevenSettings.voiceId, customSampleText]);

  const handleDryRun = useCallback(async () => {
    try {
      const result = await regenerateAllAudio(buildRegenerationPayload(true));
      setDryRunResult({ count: result.totalItems });
    } catch {
      setToast({ variant: 'error', message: 'Dry run failed' });
    }
  }, [buildRegenerationPayload]);

  const handleStartRegeneration = useCallback(async () => {
    setConfirmModal(false);
    setRegenerating(true);
    try {
      await regenerateAllAudio(buildRegenerationPayload(false));
      setToast({ variant: 'success', message: 'Regeneration started' });
      void fetchBatches();
    } catch {
      setToast({ variant: 'error', message: 'Failed to start regeneration' });
    } finally {
      setRegenerating(false);
    }
  }, [buildRegenerationPayload, fetchBatches]);

  const handleSaveElevenSettings = useCallback(async () => {
    setSavingEleven(true);
    try {
      await saveAdminVoiceDesignConfig({
        ...(elevenSettings.apiKey.trim() ? { elevenLabsApiKey: elevenSettings.apiKey.trim() } : {}),
        elevenLabsTtsBaseUrl: elevenSettings.baseUrl.trim(),
        elevenLabsDefaultVoiceId: elevenSettings.voiceId.trim(),
        elevenLabsModel: elevenSettings.model.trim(),
        elevenLabsOutputFormat: elevenSettings.outputFormat.trim(),
        elevenLabsPronunciationDictionaryId: elevenSettings.dictionaryId.trim(),
        elevenLabsPronunciationDictionaryVersionId: elevenSettings.dictionaryVersionId.trim(),
        elevenLabsStability: elevenSettings.stability,
        elevenLabsSimilarityBoost: elevenSettings.similarityBoost,
        elevenLabsStyle: elevenSettings.style,
        elevenLabsUseSpeakerBoost: elevenSettings.useSpeakerBoost,
      });
      setElevenSettings((prev) => ({ ...prev, apiKey: '', apiKeyPresent: prev.apiKeyPresent || Boolean(prev.apiKey.trim()) }));
      setSavedElevenSettings((prev) => ({
        ...elevenSettings,
        apiKey: '',
        apiKeyPresent: prev.apiKeyPresent || Boolean(elevenSettings.apiKey.trim()),
      }));
      setToast({ variant: 'success', message: 'ElevenLabs settings saved' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save ElevenLabs settings' });
    } finally {
      setSavingEleven(false);
    }
  }, [elevenSettings]);

  const handleUploadDictionary = useCallback(async () => {
    if (!dictionaryFile) return;
    setUploadingDictionary(true);
    try {
      const result = await uploadElevenLabsPronunciationDictionary(dictionaryFile, dictionaryFile.name.replace(/\.pls$/i, ''));
      setElevenSettings((prev) => ({
        ...prev,
        dictionaryId: result.dictionaryId,
        dictionaryVersionId: result.versionId ?? '',
      }));
      setDictionaryFile(null);
      setToast({ variant: 'success', message: 'Pronunciation dictionary uploaded' });
    } catch (err) {
      // Surface the real ElevenLabs / validation error rather than a flat message.
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Dictionary upload failed' });
    } finally {
      setUploadingDictionary(false);
    }
  }, [dictionaryFile]);

  const handleCancelBatch = useCallback(async (batchId: string) => {
    try {
      await cancelAudioRegenerationBatch(batchId);
      setToast({ variant: 'success', message: 'Batch cancelled' });
      void fetchBatches();
    } catch {
      setToast({ variant: 'error', message: 'Failed to cancel batch' });
    }
  }, [fetchBatches]);

  const handleRetryBatch = useCallback(async (batchId: string) => {
    try {
      await retryAudioRegenerationBatch(batchId);
      setToast({ variant: 'success', message: 'Retry started' });
      void fetchBatches();
    } catch {
      setToast({ variant: 'error', message: 'Failed to retry batch' });
    }
  }, [fetchBatches]);

  const handleRateSample = useCallback((sampleId: string, rating: number) => {
    setSamples((prev) => prev.map((s) => (s.id === sampleId ? { ...s, rating } : s)));
  }, []);

  // ─── Computed ───
  const activeBatches = batches.filter((b) => b.status === 'running');
  const completedBatches = batches.filter((b) => b.status !== 'running').slice(0, 10);
  const todayCount = batches.filter((b) => b.completedAt && new Date(b.completedAt).toDateString() === new Date().toDateString()).reduce((sum, b) => sum + b.completedItems, 0);
  const successRate = batches.length > 0
    ? Math.round((batches.reduce((s, b) => s + b.completedItems, 0) / Math.max(1, batches.reduce((s, b) => s + b.totalItems, 0))) * 100)
    : 0;
  const elevenSettingsDirty = JSON.stringify({ ...elevenSettings, apiKey: '' }) !== JSON.stringify({ ...savedElevenSettings, apiKey: '' })
    || Boolean(elevenSettings.apiKey.trim());
  const settingsReady = Boolean(elevenSettings.apiKeyPresent)
    && Boolean(elevenSettings.voiceId.trim())
    && Boolean(elevenSettings.baseUrl.trim())
    && Boolean(elevenSettings.model.trim())
    && !elevenSettingsDirty;
  const canPreviewCount = settingsReady;
  const canStartRegeneration = Boolean(dryRunResult) && settingsReady && !regenerating;

  return (
    <AdminRouteWorkspace>
      <AdminPageShell>
        <PageHeader
          title="Voice Design Studio"
          description="Browse ElevenLabs voices, configure the platform voice, and manage bulk audio regeneration."
          breadcrumbs={[
            { label: 'Admin', href: '/admin' },
            { label: 'Voice Design' },
          ]}
          icon={<AudioWaveform className="h-5 w-5" aria-hidden="true" />}
        />

      {/* Global Voice Indicator */}
      <div className="flex items-center gap-2 rounded-admin-lg border border-admin-border bg-admin-bg-surface px-4 py-2.5">
        <Lock className="h-4 w-4 text-[var(--admin-primary)]" />
        <span className="text-xs font-bold text-admin-text-muted">ElevenLabs Voice:</span>
        <Badge variant="info" size="sm">{elevenSettings.voiceId || 'Not configured'}</Badge>
      </div>

      {/* ─── Section 1: Voice Browser ─── */}
      <AdminRoutePanel title="Voice Browser" className="overflow-visible">
        <SectionToggle label="Voice Browser" open={voiceBrowser.open} onToggle={voiceBrowser.toggle} />
        <AnimatePresence initial={false}>
          {voiceBrowser.open && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              className="overflow-hidden"
            >
              <div className="space-y-4 p-4">
                <div className="flex items-center justify-between">
                  <p className="text-xs text-admin-text-muted">Voices fetched live from your ElevenLabs account.</p>
                  <Button variant="secondary" size="sm" loading={probing} onClick={handleFetchVoices}>
                    <Sparkles className="mr-1.5 h-3.5 w-3.5" />
                    Fetch Voices
                  </Button>
                </div>

                {voices && (
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                    {voices.map((voice) => (
                      <div
                        key={voice.voiceId}
                        className={`relative rounded-xl border p-3 transition-colors ${elevenSettings.voiceId === voice.voiceId ? 'border-[var(--admin-primary)] bg-[var(--admin-primary-tint)]' : 'border-admin-border bg-admin-surface-raised hover:border-admin-border/80'}`}
                      >
                        <div className="flex items-start justify-between">
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-bold text-admin-text">{voice.name}</p>
                            <p className="mt-0.5 truncate text-xs text-admin-text-muted">{voice.voiceId}</p>
                          </div>
                          {playingVoiceId === voice.voiceId && (
                            <div className="ml-2 flex items-center gap-0.5">
                              {[1, 2, 3].map((bar) => (
                                <motion.div
                                  key={bar}
                                  className="w-0.5 rounded-full bg-[var(--admin-primary)]"
                                  animate={{ height: [8, 16, 8] }}
                                  transition={{ repeat: Infinity, duration: 0.6, delay: bar * 0.15 }}
                                />
                              ))}
                            </div>
                          )}
                        </div>
                        {voice.category && (
                          <div className="mt-2">
                            <Badge variant="muted" size="sm">{voice.category}</Badge>
                          </div>
                        )}
                        <div className="mt-3 flex gap-2">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handlePreviewVoice(voice.voiceId)}
                            disabled={playingVoiceId === voice.voiceId}
                          >
                            <Volume2 className="mr-1 h-3.5 w-3.5" />
                            Preview
                          </Button>
                          <Button
                            variant={elevenSettings.voiceId === voice.voiceId ? 'primary' : 'secondary'}
                            size="sm"
                            onClick={() => updateElevenSettings('voiceId', voice.voiceId)}
                          >
                            {elevenSettings.voiceId === voice.voiceId ? 'Selected' : 'Select'}
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                {!voices && !probing && (
                  <p className="py-8 text-center text-sm text-admin-text-muted">
                    Click &quot;Fetch Voices&quot; to load your ElevenLabs voices. Selecting one sets the default voice — remember to Save below.
                  </p>
                )}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </AdminRoutePanel>

      {/* ─── Section 2: Multi-Sample Preview ─── */}
      <AdminRoutePanel title="Multi-Sample Preview" className="overflow-visible">
        <SectionToggle label="Preview Panel" open={previewPanel.open} onToggle={previewPanel.toggle} />
        <AnimatePresence initial={false}>
          {previewPanel.open && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              className="overflow-hidden"
            >
              <div className="space-y-4 p-4">
                <p className="text-xs text-admin-text-muted">
                  Hear your selected voice (<span className="font-bold text-admin-text">{elevenSettings.voiceId || 'not set'}</span>) speak realistic OET clinical lines. Add a custom line to test specific words — e.g. drug names like &ldquo;Amlodipine&rdquo; — then press Generate. Each clip has full playback controls (replay, seek, volume) and an optional star rating to compare voices.
                </p>
                <div className="space-y-2">
                  <label className="text-xs font-bold text-admin-text-muted">Custom Sample (optional)</label>
                  <textarea
                    value={customSampleText}
                    onChange={(e) => setCustomSampleText(e.target.value)}
                    placeholder="Type a custom sentence to test (added to the OET samples)…"
                    className="w-full rounded-lg border border-admin-border bg-admin-surface-raised p-3 text-sm text-admin-text placeholder:text-admin-text-muted/50 focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                    rows={2}
                  />
                </div>
                <div className="flex items-center gap-3">
                  <Button
                    variant="primary"
                    size="sm"
                    loading={generatingSamples}
                    disabled={!elevenSettings.voiceId || generatingSamples}
                    onClick={handleGenerateSamples}
                  >
                    <Mic2 className="mr-1.5 h-3.5 w-3.5" />
                    Generate Samples
                  </Button>
                  {generatingSamples && (
                    <span className="text-xs font-bold text-admin-text-muted">
                      Progress: {sampleProgress}/{OET_SAMPLES.length + (customSampleText.trim() ? 1 : 0)}
                    </span>
                  )}
                </div>

                {samples.length > 0 && (
                  <div className="space-y-3">
                    {samples.map((sample) => (
                      <div key={sample.id} className="rounded-xl border border-admin-border bg-admin-surface-raised p-3">
                        <p className="mb-2 text-xs text-admin-text">{sample.text}</p>
                        {sample.loading ? (
                          <div className="flex items-center gap-2">
                            <Loader2 className="h-4 w-4 animate-spin text-[var(--admin-primary)]" />
                            <span className="text-xs text-admin-text-muted">Generating…</span>
                          </div>
                        ) : sample.error ? (
                          <span className="text-xs text-red-400">{sample.error}</span>
                        ) : sample.audioUrl ? (
                          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                            {/* Native player: replay, pause, seek, volume, download — keyboard + screen-reader accessible. */}
                            <audio
                              controls
                              preload="metadata"
                              src={sample.audioUrl}
                              aria-label={`Audio for: ${sample.text}`}
                              className="h-9 w-full sm:max-w-sm"
                            />
                            <div
                              className="flex items-center gap-0.5 sm:ml-auto"
                              role="radiogroup"
                              aria-label={`Rate the voice for: ${sample.text}`}
                            >
                              {[1, 2, 3, 4, 5].map((star) => (
                                <button
                                  key={star}
                                  type="button"
                                  role="radio"
                                  aria-checked={sample.rating === star}
                                  aria-label={`${star} star${star > 1 ? 's' : ''}`}
                                  onClick={() => handleRateSample(sample.id, star)}
                                  className="p-0.5"
                                >
                                  <Star className={`h-4 w-4 ${star <= sample.rating ? 'fill-amber-400 text-amber-400' : 'text-admin-text-muted/30'}`} />
                                </button>
                              ))}
                            </div>
                          </div>
                        ) : null}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </AdminRoutePanel>

      {/* ─── Section 3: ElevenLabs Settings ─── */}
      <AdminRoutePanel title="ElevenLabs Settings" className="overflow-visible">
        <SectionToggle label="ElevenLabs Settings" open={recallAudio.open} onToggle={recallAudio.toggle} />
        <AnimatePresence initial={false}>
          {recallAudio.open && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              className="overflow-hidden"
            >
              <div className="grid gap-4 p-4 lg:grid-cols-2">
                <div className="space-y-3">
                  <div className="flex items-center gap-2">
                    <Badge variant={elevenSettings.apiKeyPresent ? 'success' : 'warning'} size="sm">
                      {elevenSettings.apiKeyPresent ? 'API key saved' : 'API key missing'}
                    </Badge>
                    {elevenSettings.dictionaryId && <Badge variant="info" size="sm">PLS linked</Badge>}
                  </div>
                  <label className="block space-y-1.5">
                    <span className="text-xs font-bold text-admin-text-muted">ElevenLabs API Key</span>
                    <input
                      type="password"
                      value={elevenSettings.apiKey}
                      onChange={(event) => updateElevenSettings('apiKey', event.target.value)}
                      placeholder={elevenSettings.apiKeyPresent ? 'Saved. Enter a new key to rotate.' : 'Paste API key'}
                      className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text placeholder:text-admin-text-muted/50 focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                    />
                  </label>
                  <label className="block space-y-1.5">
                    <span className="text-xs font-bold text-admin-text-muted">ElevenLabs API Base URL</span>
                    <input
                      type="url"
                      value={elevenSettings.baseUrl}
                      onChange={(event) => updateElevenSettings('baseUrl', event.target.value)}
                      className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                    />
                  </label>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <label className="block space-y-1.5">
                      <span className="text-xs font-bold text-admin-text-muted">Default Voice ID</span>
                      <input
                        type="text"
                        value={elevenSettings.voiceId}
                        onChange={(event) => updateElevenSettings('voiceId', event.target.value)}
                        className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                      />
                      <p className="text-[11px] text-admin-text-muted">Used for all ElevenLabs generations (recalls, vocabulary, conversation, listening) when no override is set.</p>
                    </label>
                    <label className="block space-y-1.5">
                      <span className="text-xs font-bold text-admin-text-muted">Model</span>
                      <input
                        type="text"
                        value={elevenSettings.model}
                        onChange={(event) => updateElevenSettings('model', event.target.value)}
                        className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                      />
                    </label>
                    <label className="block space-y-1.5">
                      <span className="text-xs font-bold text-admin-text-muted">Output Format</span>
                      <input
                        type="text"
                        value={elevenSettings.outputFormat}
                        onChange={(event) => updateElevenSettings('outputFormat', event.target.value)}
                        className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                      />
                    </label>
                    <label className="block space-y-1.5">
                      <span className="text-xs font-bold text-admin-text-muted">Dictionary Version</span>
                      <input
                        type="text"
                        value={elevenSettings.dictionaryVersionId}
                        onChange={(event) => updateElevenSettings('dictionaryVersionId', event.target.value)}
                        className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                      />
                    </label>
                  </div>
                  <label className="block space-y-1.5">
                    <span className="text-xs font-bold text-admin-text-muted">Dictionary ID</span>
                    <input
                      type="text"
                      value={elevenSettings.dictionaryId}
                      onChange={(event) => updateElevenSettings('dictionaryId', event.target.value)}
                      className="w-full rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 text-sm text-admin-text focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                    />
                  </label>
                </div>

                <div className="space-y-4">
                  {([
                    ['stability', 'Stability', elevenSettings.stability, 0, 1, 0.01],
                    ['similarityBoost', 'Similarity', elevenSettings.similarityBoost, 0, 1, 0.01],
                    ['style', 'Style', elevenSettings.style, 0, 1, 0.01],
                  ] as const).map(([key, label, value, min, max, step]) => (
                    <label key={key} className="block space-y-2">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-admin-text-muted">{label}</span>
                        <span className="text-sm font-bold text-admin-text">{value.toFixed(2)}</span>
                      </div>
                      <input
                        type="range"
                        min={min}
                        max={max}
                        step={step}
                        value={value}
                        onChange={(event) => updateElevenSettings(key, Number(event.target.value))}
                        className="w-full accent-[var(--admin-primary)]"
                      />
                    </label>
                  ))}
                  <label className="flex items-center gap-2 rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2">
                    <input
                      type="checkbox"
                      checked={elevenSettings.useSpeakerBoost}
                      onChange={(event) => updateElevenSettings('useSpeakerBoost', event.target.checked)}
                      className="accent-[var(--admin-primary)]"
                    />
                    <span className="text-sm text-admin-text">Use speaker boost</span>
                  </label>
                  <div className="space-y-2 rounded-lg border border-admin-border bg-admin-surface-raised p-3">
                    <label className="block space-y-1.5">
                      <span className="text-xs font-bold text-admin-text-muted">PLS Pronunciation Dictionary</span>
                      <input
                        type="file"
                        accept=".pls,application/pls+xml,text/xml,application/xml"
                        onChange={(event) => setDictionaryFile(event.target.files?.[0] ?? null)}
                        className="block w-full text-xs text-admin-text file:mr-3 file:rounded-md file:border-0 file:bg-[var(--admin-primary)] file:px-3 file:py-1.5 file:text-xs file:font-bold file:text-[var(--admin-primary-fg)]"
                      />
                    </label>
                    <Button variant="secondary" size="sm" loading={uploadingDictionary} disabled={!dictionaryFile} onClick={handleUploadDictionary}>
                      Upload PLS
                    </Button>
                  </div>
                  <Button variant="primary" size="sm" loading={savingEleven} onClick={handleSaveElevenSettings}>
                    Save ElevenLabs Settings
                  </Button>
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </AdminRoutePanel>

      {/* ─── Section 4: Bulk Regeneration ─── */}
      <AdminRoutePanel title="Bulk Regeneration" className="overflow-visible">
        <SectionToggle label="Bulk Regeneration" open={bulkRegen.open} onToggle={bulkRegen.toggle} />
        <AnimatePresence initial={false}>
          {bulkRegen.open && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              className="overflow-hidden"
            >
              <div className="space-y-6 p-4">
                <fieldset className="space-y-2">
                  <legend className="text-xs font-bold text-admin-text-muted">Audio Type (all generated via ElevenLabs)</legend>
                  <div className="space-y-1.5">
                    {([
                      ['recalls', 'Recall Words Only'],
                      ['all', 'All Platform Audio (listening + vocabulary)'],
                      ['listening', 'Listening Module Only'],
                      ['vocabulary', 'Vocabulary Module Only'],
                    ] as const).map(([value, label]) => (
                      <label key={value} className="flex cursor-pointer items-center gap-2 rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 hover:bg-admin-surface-raised/80">
                        <input
                          type="radio"
                          name="audioType"
                          value={value}
                          checked={audioType === value}
                          onChange={() => { setAudioType(value); setDryRunResult(null); }}
                          className="accent-[var(--admin-primary)]"
                        />
                        <span className="text-sm text-admin-text">{label}</span>
                      </label>
                    ))}
                  </div>
                </fieldset>

                <fieldset className="space-y-2">
                  <legend className="text-xs font-bold text-admin-text-muted">Scope</legend>
                  <div className="space-y-1.5">
                    {([
                      ['all', 'All audio (full regeneration)'],
                      ['missing', 'Only items with missing audio'],
                      ['different-voice', 'Only items using a different voice'],
                    ] as const).map(([value, label]) => (
                      <label key={value} className="flex cursor-pointer items-center gap-2 rounded-lg border border-admin-border bg-admin-surface-raised px-3 py-2 hover:bg-admin-surface-raised/80">
                        <input
                          type="radio"
                          name="scope"
                          value={value}
                          checked={scope === value}
                          onChange={() => { setScope(value); setDryRunResult(null); }}
                          className="accent-[var(--admin-primary)]"
                        />
                        <span className="text-sm text-admin-text">{label}</span>
                      </label>
                    ))}
                  </div>
                </fieldset>

                {dryRunResult && (
                  <div className="rounded-xl border border-[var(--admin-primary)]/20 bg-[var(--admin-primary-tint)] p-4">
                    <p className="text-sm text-admin-text">
                      <span className="font-bold text-[var(--admin-primary)]">{dryRunResult.count.toLocaleString()}</span>{' '}
                      audio items will be regenerated
                    </p>
                  </div>
                )}

                <div className="flex gap-3">
                  <Button variant="secondary" size="sm" onClick={handleDryRun} disabled={!canPreviewCount}>
                    <Settings2 className="mr-1.5 h-3.5 w-3.5" />
                    Preview Count
                  </Button>
                  <Button variant="primary" size="sm" disabled={!canStartRegeneration} loading={regenerating} onClick={() => setConfirmModal(true)}>
                    Start Regeneration
                  </Button>
                </div>

                {!settingsReady && (
                  <p className="text-xs text-amber-400">Save a valid ElevenLabs API key, voice, base URL and model before previewing or starting regeneration.</p>
                )}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </AdminRoutePanel>

      {/* ─── Section 5: Job Progress Tracker ─── */}
      <AdminRoutePanel title="Job Progress" className="overflow-visible">
        <SectionToggle label="Job Progress Tracker" open={jobTracker.open} onToggle={jobTracker.toggle} />
        <AnimatePresence initial={false}>
          {jobTracker.open && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              className="overflow-hidden"
            >
              <div className="space-y-4 p-4">
                <div className="grid grid-cols-3 gap-3">
                  <div className="rounded-xl border border-admin-border bg-admin-surface-raised p-3 text-center">
                    <p className="text-lg font-bold text-admin-text">{todayCount.toLocaleString()}</p>
                    <p className="text-xs text-admin-text-muted">Generated today</p>
                  </div>
                  <div className="rounded-xl border border-admin-border bg-admin-surface-raised p-3 text-center">
                    <p className="text-lg font-bold text-admin-text">{successRate}%</p>
                    <p className="text-xs text-admin-text-muted">Success rate</p>
                  </div>
                  <div className="rounded-xl border border-admin-border bg-admin-surface-raised p-3 text-center">
                    <p className="text-lg font-bold text-admin-text">{activeBatches.length}</p>
                    <p className="text-xs text-admin-text-muted">Active batches</p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <Button variant="ghost" size="sm" onClick={() => void fetchBatches()} loading={loadingBatches}>
                    <RefreshCw className="mr-1 h-3.5 w-3.5" />
                    Refresh
                  </Button>
                  <span className="text-xs text-admin-text-muted">Auto-refreshes every 5s</span>
                </div>

                {activeBatches.length > 0 && (
                  <div className="space-y-2">
                    <h4 className="text-xs font-bold uppercase tracking-wider text-admin-text-muted">Active</h4>
                    {activeBatches.map((batch) => (
                      <BatchCard key={batch.batchId} batch={batch} onCancel={handleCancelBatch} onRetry={handleRetryBatch} />
                    ))}
                  </div>
                )}

                {completedBatches.length > 0 && (
                  <div className="space-y-2">
                    <h4 className="text-xs font-bold uppercase tracking-wider text-admin-text-muted">History (last 10)</h4>
                    {completedBatches.map((batch) => (
                      <BatchCard key={batch.batchId} batch={batch} onRetry={handleRetryBatch} />
                    ))}
                  </div>
                )}

                {batches.length === 0 && (
                  <p className="py-4 text-center text-sm text-admin-text-muted">No batches found.</p>
                )}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </AdminRoutePanel>

      {/* ─── Confirmation Modal ─── */}
      <Modal open={confirmModal} onClose={() => setConfirmModal(false)} title="Confirm Bulk Regeneration" size="md">
        <div className="space-y-4">
          <div className="rounded-xl border border-admin-border bg-admin-surface-raised p-4 space-y-2">
            <div className="flex justify-between text-sm">
              <span className="text-admin-text-muted">Voice:</span>
              <span className="font-bold text-admin-text">{elevenSettings.voiceId}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-admin-text-muted">Audio Type:</span>
              <span className="font-bold text-admin-text">{audioType}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-admin-text-muted">Scope:</span>
              <span className="font-bold text-admin-text">{scope}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-admin-text-muted">Items:</span>
              <span className="font-bold text-admin-text">{dryRunResult?.count.toLocaleString() ?? '-'}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-admin-text-muted">Est. time:</span>
              <span className="font-bold text-admin-text">
                {dryRunResult ? `~${Math.ceil(dryRunResult.count * 3 / 60)} min` : '-'}
              </span>
            </div>
          </div>

          <div className="rounded-xl border border-amber-500/20 bg-amber-500/5 p-3">
            <p className="text-xs text-amber-400">
              This will regenerate audio for {dryRunResult?.count.toLocaleString() ?? 0} items. Existing audio will be overwritten. This action cannot be undone.
            </p>
          </div>

          <div className="flex justify-end gap-3 pt-2">
            <Button variant="ghost" onClick={() => setConfirmModal(false)}>Cancel</Button>
            <Button variant="primary" loading={regenerating} onClick={handleStartRegeneration}>
              Confirm &amp; Start
            </Button>
          </div>
        </div>
      </Modal>

      {/* ─── Toast ─── */}
      <AnimatePresence>
        {toast && (
          <Toast
            variant={toast.variant === 'success' ? 'success' : 'error'}
            message={toast.message}
            onClose={() => setToast(null)}
          />
        )}
      </AnimatePresence>
      </AdminPageShell>
    </AdminRouteWorkspace>
  );
}

/* ─── Sub-components ─── */

function SectionToggle({ label, open, onToggle }: { label: string; open: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      className="flex w-full items-center gap-2 px-4 py-2.5 text-left"
    >
      {open ? (
        <ChevronDown className="h-4 w-4 text-admin-text-muted" />
      ) : (
        <ChevronRight className="h-4 w-4 text-admin-text-muted" />
      )}
      <span className="text-xs font-bold uppercase tracking-wider text-admin-text-muted">{label}</span>
    </button>
  );
}

function BatchCard({ batch, onCancel, onRetry }: { batch: AudioBatch; onCancel?: (id: string) => void; onRetry?: (id: string) => void }) {
  const progress = batch.totalItems > 0 ? Math.round((batch.completedItems / batch.totalItems) * 100) : 0;
  const [showErrors, setShowErrors] = useState(false);

  const statusBadgeVariant = {
    running: 'info' as const,
    completed: 'success' as const,
    failed: 'danger' as const,
    cancelled: 'warning' as const,
  }[batch.status];

  return (
    <div className="rounded-xl border border-admin-border bg-admin-surface-raised p-3 space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="text-xs font-mono text-admin-text-muted">{batch.batchId.slice(0, 8)}</span>
          <Badge variant={statusBadgeVariant} size="sm">{batch.status}</Badge>
          <Badge variant="muted" size="sm">{batch.audioType}</Badge>
        </div>
        <div className="flex items-center gap-2">
          {batch.audioType === 'recalls' && batch.status !== 'running' && batch.failedItems > 0 && onRetry && (
            <Button variant="secondary" size="sm" onClick={() => onRetry(batch.batchId)}>
              <RefreshCw className="mr-1 h-3.5 w-3.5" />
              Retry failed
            </Button>
          )}
          {batch.status === 'running' && onCancel && (
            <Button variant="ghost" size="sm" onClick={() => onCancel(batch.batchId)}>
              <XCircle className="mr-1 h-3.5 w-3.5" />
              Cancel
            </Button>
          )}
        </div>
      </div>

      <div className="space-y-1">
        <div className="h-2 w-full overflow-hidden rounded-full bg-admin-surface">
          <div
            className="h-full rounded-full bg-[var(--admin-primary)] transition-[width,background-color] duration-300"
            style={{ width: `${progress}%` }}
          />
        </div>
        <div className="flex justify-between text-xs text-admin-text-muted">
          <span>{batch.completedItems}/{batch.totalItems}</span>
          <span>{progress}%</span>
        </div>
      </div>

      <div className="flex flex-wrap gap-3 text-xs text-admin-text-muted">
        <span>Voice: {batch.voiceId}</span>
        <span>Provider: {batch.providerName}</span>
        <span>Started: {new Date(batch.startedAt).toLocaleTimeString()}</span>
        {batch.failedItems > 0 && (
          <button
            type="button"
            onClick={() => setShowErrors((v) => !v)}
            className="text-red-400 hover:underline"
          >
            {batch.failedItems} failed
          </button>
        )}
      </div>

      {showErrors && batch.failedItems > 0 && (
        <div className="rounded-lg border border-red-500/20 bg-red-500/5 p-2 text-xs text-red-400">
          {batch.failedItems} items failed during generation. Check server logs for details.
        </div>
      )}
    </div>
  );
}
