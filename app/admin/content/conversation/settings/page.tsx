'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertTriangle, Save, Volume2, Settings as SettingsIcon, ArrowLeft, RefreshCw, Sparkles, Mic2 } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/admin/ui/badge';
import { Modal } from '@/components/ui/modal';
import {
  fetchAdminConversationSettings,
  updateAdminConversationSettings,
  adminConversationTtsPreview,
  probeAdminQwen3Voices,
  previewAdminQwen3Voice,
  regenerateVocabularyAudio,
  type AdminQwen3VoiceProbeResult,
  type AdminVocabularyAudioRegenerateResult,
} from '@/lib/api';

type Settings = {
  enabled?: boolean;
  asrProvider?: string;
  ttsProvider?: string;
  azureSpeechRegion?: string;
  azureLocale?: string;
  azureTtsDefaultVoice?: string;
  whisperBaseUrl?: string;
  whisperModel?: string;
  deepgramModel?: string;
  deepgramLanguage?: string;
  realtimeSttEnabled?: boolean;
  realtimeAsrProvider?: string;
  realtimeSttAllowRealProvider?: boolean;
  realtimeSttRealProviderProductionAuthorized?: boolean;
  realtimeSttFallbackToBatch?: boolean;
  realtimeSttProviderConnectTimeoutSeconds?: number;
  realtimeSttMaxChunkBytes?: number;
  realtimeSttPartialMinIntervalMs?: number;
  realtimeSttTurnIdleTimeoutSeconds?: number;
  realtimeSttMaxConcurrentStreamsPerUser?: number;
  realtimeSttMaxAudioSecondsPerSession?: number;
  realtimeSttDailyAudioSecondsPerUser?: number;
  realtimeSttMonthlyBudgetCapUsd?: number;
  realtimeSttEstimatedCostUsdPerMinute?: number;
  realtimeSttProviderSessionTopology?: string;
  realtimeSttRegionId?: string;
  realtimeSttAssumeLearnersAdult?: boolean;
  realtimeSttAllowManagedLearnerRealProvider?: boolean;
  realtimeSttConsentVersion?: string;
  realtimeSttRollbackMode?: string;
  realtimeSttAllowedMimeTypes?: string[];
  elevenLabsSttBaseUrl?: string;
  elevenLabsSttModel?: string;
  elevenLabsSttLanguage?: string;
  elevenLabsSttAudioFormat?: string;
  elevenLabsSttCommitStrategy?: string;
  elevenLabsSttKeytermsCsv?: string;
  elevenLabsSttEnableProviderLogging?: boolean;
  elevenLabsSttTokenTtlSeconds?: number;
  elevenLabsDefaultVoiceId?: string;
  elevenLabsModel?: string;
  cosyVoiceBaseUrl?: string;
  cosyVoiceDefaultVoice?: string;
  chatTtsBaseUrl?: string;
  chatTtsDefaultVoice?: string;
  qwen3ModelVariant?: 'flash' | 'voicedesign' | string;
  qwen3VoiceId?: string;
  qwen3VoiceInstructions?: string;
  gptSoVitsBaseUrl?: string;
  gptSoVitsDefaultVoice?: string;
  maxAudioBytes?: number;
  audioRetentionDays?: number;
  prepDurationSeconds?: number;
  maxSessionDurationSeconds?: number;
  maxTurnDurationSeconds?: number;
  enabledTaskTypes?: string[];
  freeTierSessionsLimit?: number;
  freeTierWindowDays?: number;
  replyModel?: string;
  evaluationModel?: string;
  replyTemperature?: number;
  evaluationTemperature?: number;
  azureSpeechKeyPresent?: boolean;
  whisperApiKeyPresent?: boolean;
  deepgramApiKeyPresent?: boolean;
  elevenLabsSttApiKeyPresent?: boolean;
  elevenLabsApiKeyPresent?: boolean;
  cosyVoiceApiKeyPresent?: boolean;
  chatTtsApiKeyPresent?: boolean;
  gptSoVitsApiKeyPresent?: boolean;
};

export default function AdminConversationSettingsPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [draft, setDraft] = useState<Record<string, unknown>>({});
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const s = (await fetchAdminConversationSettings()) as Settings;
      setSettings(s);
      setDraft({});
    } catch {
      setToast({ variant: 'error', message: 'Failed to load settings.' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  function setField<K extends keyof Settings>(key: K, value: Settings[K]) {
    setDraft((prev) => ({ ...prev, [key]: value }));
  }
  function setSecret(key: string, value: string) {
    setDraft((prev) => ({ ...prev, [key]: value }));
  }

  async function handleSave() {
    setSaving(true);
    try {
      await updateAdminConversationSettings(draft);
      setToast({ variant: 'success', message: 'Conversation settings saved. Takes effect within 30 s.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed.' });
    } finally {
      setSaving(false);
    }
  }

  async function handlePreview() {
    try {
      const provider = String(v('ttsProvider') ?? '').toLowerCase();
      const voice = provider === 'elevenlabs'
        ? String(v('elevenLabsDefaultVoiceId') ?? '')
        : provider === 'cosyvoice'
          ? String(v('cosyVoiceDefaultVoice') ?? '')
          : provider === 'chattts'
            ? String(v('chatTtsDefaultVoice') ?? '')
            : provider === 'digitalocean-qwen3-tts'
              ? String(v('chatTtsDefaultVoice') ?? '')
              : provider === 'gptsovits'
                ? String(v('gptSoVitsDefaultVoice') ?? '')
                : provider === 'azure'
                  ? String(v('azureTtsDefaultVoice') ?? '')
                  : '';
      const blob = await adminConversationTtsPreview({
        voice,
        locale: 'en-GB',
        text: 'Good morning. Thank you for coming in today. How can I help you?',
      });
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      await audio.play();
      setToast({ variant: 'success', message: 'Playing TTS sample…' });
    } catch {
      setToast({ variant: 'error', message: 'TTS preview failed (check the configured provider + key).' });
    }
  }

  const v = <K extends keyof Settings>(key: K) =>
    (draft[key as string] as Settings[K] | undefined) ?? settings?.[key];

  return (
    <>
      <AdminSettingsLayout
        eyebrow="Content"
        title="AI Conversation — Runtime Settings"
        description="Admin-editable overrides for the AI Conversation subsystem. Saved values override env defaults and take effect within 30 seconds of save. API keys are encrypted via Data Protection; leave blank to keep the current key."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Conversation', href: '/admin/content/conversation' },
          { label: 'Settings' },
        ]}
        actions={
          <>
            <Button variant="secondary" onClick={() => router.push('/admin/content/conversation')}>
              <ArrowLeft className="mr-1 h-4 w-4" /> Back
            </Button>
            <Button variant="secondary" onClick={handlePreview}>
              <Volume2 className="mr-1 h-4 w-4" /> TTS Preview
            </Button>
            <Button variant="primary" onClick={handleSave} disabled={saving}>
              <Save className="mr-1 h-4 w-4" /> {saving ? 'Saving…' : 'Save'}
            </Button>
          </>
        }
      >
        {loading || !settings ? (
            <p className="text-sm text-admin-fg-muted">Loading…</p>
          ) : (
            <div className="space-y-8">
              <Section title="Feature">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={Boolean(v('enabled') ?? true)}
                    onChange={(e) => setField('enabled', e.target.checked)}
                  />
                  Enabled (kill-switch — false blocks all new sessions)
                </label>
                <Input
                  label="Enabled Task Types (CSV)"
                  value={((v('enabledTaskTypes') as string[]) ?? []).join(',')}
                  onChange={(e) =>
                    setField(
                      'enabledTaskTypes',
                      e.target.value
                        .split(',')
                        .map((s) => s.trim())
                        .filter(Boolean),
                    )
                  }
                />
              </Section>

              <Section title="Session Shape">
                <Grid>
                  <Input label="Prep Duration (s)" type="number" value={String(v('prepDurationSeconds') ?? '')}
                    onChange={(e) => setField('prepDurationSeconds', Number(e.target.value))} />
                  <Input label="Max Session Duration (s)" type="number" value={String(v('maxSessionDurationSeconds') ?? '')}
                    onChange={(e) => setField('maxSessionDurationSeconds', Number(e.target.value))} />
                  <Input label="Max Turn Duration (s)" type="number" value={String(v('maxTurnDurationSeconds') ?? '')}
                    onChange={(e) => setField('maxTurnDurationSeconds', Number(e.target.value))} />
                  <Input label="Max Audio Bytes" type="number" value={String(v('maxAudioBytes') ?? '')}
                    onChange={(e) => setField('maxAudioBytes', Number(e.target.value))} />
                  <Input label="Audio Retention (days)" type="number" value={String(v('audioRetentionDays') ?? '')}
                    onChange={(e) => setField('audioRetentionDays', Number(e.target.value))} />
                </Grid>
              </Section>

              <Section title="Entitlement (free tier)">
                <Grid>
                  <Input label="Free tier sessions limit" type="number" value={String(v('freeTierSessionsLimit') ?? '')}
                    onChange={(e) => setField('freeTierSessionsLimit', Number(e.target.value))} />
                  <Input label="Free tier window (days)" type="number" value={String(v('freeTierWindowDays') ?? '')}
                    onChange={(e) => setField('freeTierWindowDays', Number(e.target.value))} />
                </Grid>
              </Section>

              <Section title="AI Models">
                <Grid>
                  <Input label="Reply Model" value={String(v('replyModel') ?? '')}
                    onChange={(e) => setField('replyModel', e.target.value)}
                    placeholder="anthropic-claude-opus-4.7" />
                  <Input label="Evaluation Model" value={String(v('evaluationModel') ?? '')}
                    onChange={(e) => setField('evaluationModel', e.target.value)}
                    placeholder="anthropic-claude-opus-4.7" />
                  <Input label="Reply Temperature" type="number" step="0.1" value={String(v('replyTemperature') ?? '')}
                    onChange={(e) => setField('replyTemperature', Number(e.target.value))} />
                  <Input label="Evaluation Temperature" type="number" step="0.1" value={String(v('evaluationTemperature') ?? '')}
                    onChange={(e) => setField('evaluationTemperature', Number(e.target.value))} />
                </Grid>
              </Section>

              <Section title="ASR (Speech → Text)">
                <label className="block">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-admin-fg-muted">ASR Provider</span>
                  <select
                    value={String(v('asrProvider') ?? 'auto')}
                    onChange={(e) => setField('asrProvider', e.target.value)}
                    className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
                  >
                    <option value="auto">auto (prefer azure → whisper → deepgram → mock)</option>
                    <option value="azure">Azure Speech</option>
                    <option value="whisper">Whisper (OpenAI-compatible)</option>
                    <option value="deepgram">Deepgram</option>
                    <option value="mock">Mock (testing only)</option>
                  </select>
                </label>
                <Grid>
                  <KeyInput label="Azure Speech API Key" present={settings.azureSpeechKeyPresent} draftKey="azureSpeechKey" draft={draft} set={setSecret} />
                  <Input label="Azure Speech Region" value={String(v('azureSpeechRegion') ?? '')} onChange={(e) => setField('azureSpeechRegion', e.target.value)} placeholder="uksouth" />
                  <Input label="Azure Locale" value={String(v('azureLocale') ?? '')} onChange={(e) => setField('azureLocale', e.target.value)} placeholder="en-GB" />
                  <KeyInput label="Whisper API Key" present={settings.whisperApiKeyPresent} draftKey="whisperApiKey" draft={draft} set={setSecret} />
                  <Input label="Whisper Base URL" value={String(v('whisperBaseUrl') ?? '')} onChange={(e) => setField('whisperBaseUrl', e.target.value)} placeholder="https://api.openai.com/v1" />
                  <Input label="Whisper Model" value={String(v('whisperModel') ?? '')} onChange={(e) => setField('whisperModel', e.target.value)} placeholder="whisper-1" />
                  <KeyInput label="Deepgram API Key" present={settings.deepgramApiKeyPresent} draftKey="deepgramApiKey" draft={draft} set={setSecret} />
                  <Input label="Deepgram Model" value={String(v('deepgramModel') ?? '')} onChange={(e) => setField('deepgramModel', e.target.value)} placeholder="nova-2-medical" />
                  <Input label="Deepgram Language" value={String(v('deepgramLanguage') ?? '')} onChange={(e) => setField('deepgramLanguage', e.target.value)} placeholder="en-GB" />
                </Grid>
              </Section>

              <Section title="Realtime STT">
                <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800/60 dark:bg-amber-950/30 dark:text-amber-100">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                  <span>ElevenLabs realtime STT uses the backend-held encrypted key only. Production use requires legal/privacy approval, topology/region proof, spend cap approval, and protected smoke evidence.</span>
                </div>
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={Boolean(v('realtimeSttEnabled') ?? false)}
                      onChange={(e) => setField('realtimeSttEnabled', e.target.checked)}
                    />
                    Enable realtime transcript events
                  </label>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={Boolean(v('realtimeSttFallbackToBatch') ?? true)}
                      onChange={(e) => setField('realtimeSttFallbackToBatch', e.target.checked)}
                    />
                    Fallback to full-turn ASR
                  </label>
                </div>
                <label className="block">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-admin-fg-muted">Realtime ASR Provider</span>
                  <select
                    value={String(v('realtimeAsrProvider') ?? 'mock')}
                    onChange={(e) => setField('realtimeAsrProvider', e.target.value)}
                    className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
                  >
                    <option value="mock">Mock (testing only)</option>
                    <option value="elevenlabs-stt">ElevenLabs Scribe realtime</option>
                  </select>
                </label>
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={Boolean(v('realtimeSttAllowRealProvider') ?? false)}
                      onChange={(e) => setField('realtimeSttAllowRealProvider', e.target.checked)}
                    />
                    Allow paid realtime provider
                  </label>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={Boolean(v('realtimeSttRealProviderProductionAuthorized') ?? false)}
                      onChange={(e) => setField('realtimeSttRealProviderProductionAuthorized', e.target.checked)}
                    />
                    Production authorization approved
                  </label>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={Boolean(v('realtimeSttAssumeLearnersAdult') ?? false)}
                      onChange={(e) => setField('realtimeSttAssumeLearnersAdult', e.target.checked)}
                    />
                    Audience is adult learners only
                  </label>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={Boolean(v('realtimeSttAllowManagedLearnerRealProvider') ?? false)}
                      onChange={(e) => setField('realtimeSttAllowManagedLearnerRealProvider', e.target.checked)}
                    />
                    Allow managed learner cohorts
                  </label>
                </div>
                <Grid>
                  <KeyInput label="ElevenLabs STT API Key" present={settings.elevenLabsSttApiKeyPresent} draftKey="elevenLabsSttApiKey" draft={draft} set={setSecret} />
                  <Input label="ElevenLabs STT Base URL" value={String(v('elevenLabsSttBaseUrl') ?? '')} onChange={(e) => setField('elevenLabsSttBaseUrl', e.target.value)} placeholder="https://api.elevenlabs.io/v1" />
                  <Input label="ElevenLabs STT Model" value={String(v('elevenLabsSttModel') ?? '')} onChange={(e) => setField('elevenLabsSttModel', e.target.value)} placeholder="scribe_v2_realtime" />
                  <Input label="ElevenLabs STT Language" value={String(v('elevenLabsSttLanguage') ?? '')} onChange={(e) => setField('elevenLabsSttLanguage', e.target.value)} placeholder="auto" />
                  <Input label="ElevenLabs Audio Format" value={String(v('elevenLabsSttAudioFormat') ?? '')} onChange={(e) => setField('elevenLabsSttAudioFormat', e.target.value)} placeholder="pcm_16000" />
                  <Input label="ElevenLabs Commit Strategy" value={String(v('elevenLabsSttCommitStrategy') ?? '')} onChange={(e) => setField('elevenLabsSttCommitStrategy', e.target.value)} placeholder="manual" />
                  <Input label="ElevenLabs Keyterms CSV" value={String(v('elevenLabsSttKeytermsCsv') ?? '')} onChange={(e) => setField('elevenLabsSttKeytermsCsv', e.target.value)} />
                  <Input label="ElevenLabs Token TTL (s)" type="number" value={String(v('elevenLabsSttTokenTtlSeconds') ?? '')} onChange={(e) => setField('elevenLabsSttTokenTtlSeconds', Number(e.target.value))} />
                  <Input label="Provider Connect Timeout (s)" type="number" value={String(v('realtimeSttProviderConnectTimeoutSeconds') ?? '')} onChange={(e) => setField('realtimeSttProviderConnectTimeoutSeconds', Number(e.target.value))} />
                  <Input label="Max Chunk Bytes" type="number" value={String(v('realtimeSttMaxChunkBytes') ?? '')} onChange={(e) => setField('realtimeSttMaxChunkBytes', Number(e.target.value))} />
                  <Input label="Partial Min Interval (ms)" type="number" value={String(v('realtimeSttPartialMinIntervalMs') ?? '')} onChange={(e) => setField('realtimeSttPartialMinIntervalMs', Number(e.target.value))} />
                  <Input label="Turn Idle Timeout (s)" type="number" value={String(v('realtimeSttTurnIdleTimeoutSeconds') ?? '')} onChange={(e) => setField('realtimeSttTurnIdleTimeoutSeconds', Number(e.target.value))} />
                  <Input label="Concurrent Streams / User" type="number" value={String(v('realtimeSttMaxConcurrentStreamsPerUser') ?? '')} onChange={(e) => setField('realtimeSttMaxConcurrentStreamsPerUser', Number(e.target.value))} />
                  <Input label="Session Audio Limit (s)" type="number" value={String(v('realtimeSttMaxAudioSecondsPerSession') ?? '')} onChange={(e) => setField('realtimeSttMaxAudioSecondsPerSession', Number(e.target.value))} />
                  <Input label="Daily Audio Limit / User (s)" type="number" value={String(v('realtimeSttDailyAudioSecondsPerUser') ?? '')} onChange={(e) => setField('realtimeSttDailyAudioSecondsPerUser', Number(e.target.value))} />
                  <Input label="Monthly Budget Cap (USD)" type="number" step="0.01" value={String(v('realtimeSttMonthlyBudgetCapUsd') ?? '')} onChange={(e) => setField('realtimeSttMonthlyBudgetCapUsd', Number(e.target.value))} />
                  <Input label="Estimated Cost / Minute (USD)" type="number" step="0.0001" value={String(v('realtimeSttEstimatedCostUsdPerMinute') ?? '')} onChange={(e) => setField('realtimeSttEstimatedCostUsdPerMinute', Number(e.target.value))} />
                  <Input label="Provider Topology" value={String(v('realtimeSttProviderSessionTopology') ?? '')} onChange={(e) => setField('realtimeSttProviderSessionTopology', e.target.value)} placeholder="single-instance" />
                  <Input label="Provider Region ID" value={String(v('realtimeSttRegionId') ?? '')} onChange={(e) => setField('realtimeSttRegionId', e.target.value)} placeholder="uk-prod-1" />
                  <Input label="Consent Version" value={String(v('realtimeSttConsentVersion') ?? '')} onChange={(e) => setField('realtimeSttConsentVersion', e.target.value)} placeholder="realtime-stt-v1-2026-05-14" />
                  <Input label="Rollback Mode" value={String(v('realtimeSttRollbackMode') ?? '')} onChange={(e) => setField('realtimeSttRollbackMode', e.target.value)} placeholder="disable-conversation-audio" />
                  <Input label="Allowed realtime MIME types (CSV)" value={((v('realtimeSttAllowedMimeTypes') as string[]) ?? []).join(',')}
                    onChange={(e) => setField('realtimeSttAllowedMimeTypes', e.target.value.split(',').map((s) => s.trim()).filter(Boolean))}
                    placeholder="audio/pcm,audio/l16,audio/raw,application/octet-stream" />
                </Grid>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={Boolean(v('elevenLabsSttEnableProviderLogging') ?? false)}
                    onChange={(e) => setField('elevenLabsSttEnableProviderLogging', e.target.checked)}
                  />
                  Enable sanitized provider diagnostics
                </label>
              </Section>

              <Section title="TTS (Text → Speech)">
                <label className="block">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-admin-fg-muted">TTS Provider</span>
                  <select
                    value={String(v('ttsProvider') ?? 'auto')}
                    onChange={(e) => setField('ttsProvider', e.target.value)}
                    className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
                  >
                    <option value="auto">auto</option>
                    <option value="azure">Azure Speech</option>
                    <option value="elevenlabs">ElevenLabs</option>
                    <option value="cosyvoice">CosyVoice</option>
                    <option value="digitalocean-qwen3-tts">DigitalOcean Serverless Inference Qwen3 TTS</option>
                    <option value="chattts">ChatTTS</option>
                    <option value="gptsovits">GPT-SoVITS</option>
                    <option value="mock">Mock</option>
                    <option value="off">Off (text-only)</option>
                  </select>
                </label>
                <Grid>
                  <Input label="Azure TTS Default Voice" value={String(v('azureTtsDefaultVoice') ?? '')}
                    onChange={(e) => setField('azureTtsDefaultVoice', e.target.value)} placeholder="en-GB-SoniaNeural" />
                  <KeyInput label="ElevenLabs API Key" present={settings.elevenLabsApiKeyPresent} draftKey="elevenLabsApiKey" draft={draft} set={setSecret} />
                  <Input label="ElevenLabs Voice ID" value={String(v('elevenLabsDefaultVoiceId') ?? '')} onChange={(e) => setField('elevenLabsDefaultVoiceId', e.target.value)} />
                  <Input label="ElevenLabs Model" value={String(v('elevenLabsModel') ?? '')} onChange={(e) => setField('elevenLabsModel', e.target.value)} placeholder="eleven_multilingual_v2" />
                  <KeyInput label="CosyVoice API Key" present={settings.cosyVoiceApiKeyPresent} draftKey="cosyVoiceApiKey" draft={draft} set={setSecret} />
                  <Input label="CosyVoice Base URL" value={String(v('cosyVoiceBaseUrl') ?? '')} onChange={(e) => setField('cosyVoiceBaseUrl', e.target.value)} />
                  <Input label="CosyVoice Default Voice" value={String(v('cosyVoiceDefaultVoice') ?? '')} onChange={(e) => setField('cosyVoiceDefaultVoice', e.target.value)} />
                  <KeyInput label="ChatTTS API Key" present={settings.chatTtsApiKeyPresent} draftKey="chatTtsApiKey" draft={draft} set={setSecret} />
                  <Input label="ChatTTS Base URL" value={String(v('chatTtsBaseUrl') ?? '')} onChange={(e) => setField('chatTtsBaseUrl', e.target.value)} />
                  <Input label="ChatTTS Default Voice" value={String(v('chatTtsDefaultVoice') ?? '')} onChange={(e) => setField('chatTtsDefaultVoice', e.target.value)} />
                  <Input label="DigitalOcean Qwen3 TTS Base URL" value={String(v('chatTtsBaseUrl') ?? '')} onChange={(e) => setField('chatTtsBaseUrl', e.target.value)} placeholder="https://<endpoint>/v1" />
                  <KeyInput label="GPT-SoVITS API Key" present={settings.gptSoVitsApiKeyPresent} draftKey="gptSoVitsApiKey" draft={draft} set={setSecret} />
                  <Input label="GPT-SoVITS Base URL" value={String(v('gptSoVitsBaseUrl') ?? '')} onChange={(e) => setField('gptSoVitsBaseUrl', e.target.value)} />
                  <Input label="GPT-SoVITS Default Voice" value={String(v('gptSoVitsDefaultVoice') ?? '')} onChange={(e) => setField('gptSoVitsDefaultVoice', e.target.value)} />
                </Grid>
              </Section>

              {/* ── Qwen3 Voice Studio (Phase Q1) ──────────────── */}
              <Qwen3VoiceStudio
                variant={String(v('qwen3ModelVariant') ?? 'flash')}
                voiceId={String(v('qwen3VoiceId') ?? '')}
                instructions={String(v('qwen3VoiceInstructions') ?? '')}
                onChangeVariant={(val) => setField('qwen3ModelVariant', val)}
                onChangeVoice={(val) => setField('qwen3VoiceId', val)}
                onChangeInstructions={(val) => setField('qwen3VoiceInstructions', val)}
                onToast={setToast}
              />

              {/* ── Regenerate Vocabulary Audio (Phase Q1) ────────── */}
              <RegenerateVocabularyAudioPanel
                variant={(String(v('qwen3ModelVariant') ?? 'flash') === 'voicedesign' ? 'voicedesign' : 'flash')}
                voiceId={String(v('qwen3VoiceId') ?? '')}
                instructions={String(v('qwen3VoiceInstructions') ?? '')}
                onToast={setToast}
              />
            </div>
          )}
      </AdminSettingsLayout>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-3 text-xs font-bold uppercase tracking-[0.2em] text-admin-fg-muted">{title}</h3>
      <div className="space-y-3">{children}</div>
    </section>
  );
}

function Grid({ children }: { children: React.ReactNode }) {
  return <div className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">{children}</div>;
}

function KeyInput({
  label,
  present,
  draftKey,
  draft,
  set,
}: {
  label: string;
  present?: boolean;
  draftKey: string;
  draft: Record<string, unknown>;
  set: (k: string, v: string) => void;
}) {
  return (
    <div>
      <div className="mb-1 flex items-center justify-between">
        <span className="text-xs font-medium uppercase tracking-wide text-admin-fg-muted">{label}</span>
        {present ? <Badge variant="success" size="sm">set</Badge> : <Badge variant="muted" size="sm">not set</Badge>}
      </div>
      <input
        type="password"
        placeholder="Leave blank to keep current"
        value={(draft[draftKey] as string) ?? ''}
        onChange={(e) => set(draftKey, e.target.value)}
        className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
      />
    </div>
  );
}

// ── Qwen3 Voice Studio (Phase Q1) ─────────────────────────────────────
// Lets the admin fetch all 46 known flash-preset voices, audition each
// one, and pin the platform-wide active voice. When the variant is
// "voicedesign" we show a free-text instructions box instead of the
// preset grid because that model is prompt-driven (note: voicedesign
// outputs are inconsistent across calls — this is why "flash" + a single
// preset is the recommended setting for production audio generation).
function Qwen3VoiceStudio({
  variant,
  voiceId,
  instructions,
  onChangeVariant,
  onChangeVoice,
  onChangeInstructions,
  onToast,
}: {
  variant: string;
  voiceId: string;
  instructions: string;
  onChangeVariant: (v: 'flash' | 'voicedesign') => void;
  onChangeVoice: (v: string) => void;
  onChangeInstructions: (v: string) => void;
  onToast: (t: { variant: 'success' | 'error'; message: string }) => void;
}) {
  const [probing, setProbing] = useState(false);
  const [voices, setVoices] = useState<AdminQwen3VoiceProbeResult[] | null>(null);
  const [previewing, setPreviewing] = useState<string | null>(null);
  const normalisedVariant: 'flash' | 'voicedesign' =
    variant === 'voicedesign' ? 'voicedesign' : 'flash';

  async function handleFetchVoices() {
    setProbing(true);
    try {
      const res = await probeAdminQwen3Voices();
      setVoices(res.voices);
      const ok = res.voices.filter((v) => v.available).length;
      onToast({ variant: 'success', message: `Probed ${res.voices.length} voices — ${ok} available.` });
    } catch (err) {
      onToast({ variant: 'error', message: err instanceof Error ? err.message : 'Voice probe failed.' });
    } finally {
      setProbing(false);
    }
  }

  async function handlePreview(id: string) {
    setPreviewing(id);
    try {
      const blob = await previewAdminQwen3Voice({
        modelVariant: 'flash',
        voiceId: id,
        text: 'Good morning. Take a deep breath in, then slowly let it out.',
        locale: 'en-GB',
      });
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      await audio.play();
    } catch (err) {
      onToast({ variant: 'error', message: err instanceof Error ? err.message : 'Preview failed.' });
    } finally {
      setPreviewing(null);
    }
  }

  async function handleVoicedesignPreview() {
    if (!instructions.trim()) {
      onToast({ variant: 'error', message: 'Enter voicedesign instructions first.' });
      return;
    }
    setPreviewing('__voicedesign__');
    try {
      const blob = await previewAdminQwen3Voice({
        modelVariant: 'voicedesign',
        instructions,
        text: 'Good morning. Take a deep breath in, then slowly let it out.',
        locale: 'en-GB',
      });
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      await audio.play();
    } catch (err) {
      onToast({ variant: 'error', message: err instanceof Error ? err.message : 'Preview failed.' });
    } finally {
      setPreviewing(null);
    }
  }

  return (
    <section>
      <h3 className="mb-3 flex items-center gap-2 text-xs font-bold uppercase tracking-[0.2em] text-admin-fg-muted">
        <Mic2 className="h-3.5 w-3.5" /> Qwen3 Voice Studio
      </h3>
      <div className="space-y-3">
        <div className="flex items-start gap-2 rounded-xl border border-blue-200 bg-blue-50 px-3 py-2 text-sm text-blue-900 dark:border-blue-800/60 dark:bg-blue-950/30 dark:text-blue-100">
          <Sparkles className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            <strong>flash</strong> uses a fixed preset voice (deterministic, consistent — recommended). <strong>voicedesign</strong> is prompt-driven and outputs vary between calls; use only when no preset matches your need.
          </span>
        </div>
        <label className="block">
          <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-admin-fg-muted">Model Variant</span>
          <select
            value={normalisedVariant}
            onChange={(e) => onChangeVariant(e.target.value as 'flash' | 'voicedesign')}
            className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
          >
            <option value="flash">flash (preset voices — consistent)</option>
            <option value="voicedesign">voicedesign (instructions — variable)</option>
          </select>
        </label>

        {normalisedVariant === 'flash' && (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="secondary" onClick={handleFetchVoices} disabled={probing}>
                <RefreshCw className={`mr-1 h-4 w-4 ${probing ? 'animate-spin' : ''}`} />
                {probing ? 'Probing 46 voices…' : 'Fetch Voices'}
              </Button>
              {voiceId && (
                <span className="text-xs text-admin-fg-muted">
                  Current: <code className="rounded bg-surface px-1.5 py-0.5">{voiceId}</code>
                </span>
              )}
            </div>
            {voices && voices.length > 0 && (
              <div className="grid grid-cols-1 gap-2 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                {voices.map((vc) => {
                  const isSelected = vc.id === voiceId;
                  return (
                    <div
                      key={vc.id}
                      className={`rounded-xl border p-3 ${
                        isSelected ? 'border-primary bg-primary/5' : 'border-admin-border bg-admin-bg-surface'
                      } ${!vc.available ? 'opacity-60' : ''}`}
                    >
                      <div className="flex items-center justify-between">
                        <div>
                          <div className="text-sm font-medium">{vc.label}</div>
                          <div className="text-xs text-admin-fg-muted">
                            {vc.id} · {vc.gender}
                          </div>
                        </div>
                        {vc.available ? (
                          <Badge variant="success" size="sm">ok</Badge>
                        ) : (
                          <Badge variant="danger" size="sm">down</Badge>
                        )}
                      </div>
                      {!vc.available && vc.errorMessage && (
                        <div className="mt-1 truncate text-xs text-danger" title={vc.errorMessage}>
                          {vc.errorMessage}
                        </div>
                      )}
                      <div className="mt-2 flex items-center gap-2">
                        <Button
                          variant={isSelected ? 'primary' : 'secondary'}
                          size="sm"
                          onClick={() => onChangeVoice(vc.id)}
                          disabled={!vc.available}
                        >
                          {isSelected ? 'Selected' : 'Select'}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handlePreview(vc.id)}
                          disabled={!vc.available || previewing === vc.id}
                        >
                          <Volume2 className="mr-1 h-3.5 w-3.5" />
                          {previewing === vc.id ? '…' : 'Preview'}
                        </Button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
            {voices && voices.length === 0 && (
              <p className="text-sm text-admin-fg-muted">No voices returned by the probe.</p>
            )}
          </>
        )}

        {normalisedVariant === 'voicedesign' && (
          <div className="space-y-2">
            <label className="block">
              <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-admin-fg-muted">
                Voice Instructions (≤ 1000 chars; spec recommends ≤ 100)
              </span>
              <textarea
                value={instructions}
                onChange={(e) => onChangeInstructions(e.target.value.slice(0, 1000))}
                rows={4}
                placeholder="A warm, calm British nurse in her 30s speaking gently and clearly."
                className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
              />
              <span className="mt-1 block text-right text-xs text-admin-fg-muted">{instructions.length} / 1000</span>
            </label>
            <Button variant="secondary" onClick={handleVoicedesignPreview} disabled={previewing === '__voicedesign__'}>
              <Volume2 className="mr-1 h-4 w-4" />
              {previewing === '__voicedesign__' ? 'Synthesising…' : 'Preview voicedesign'}
            </Button>
          </div>
        )}
      </div>
    </section>
  );
}

// ── Regenerate Vocabulary Audio (Phase Q1) ───────────────────────────
// Bulk action: re-synthesises vocabulary audio using the currently
// configured Qwen3 voice. Always runs a dry-run first so the admin sees
// the projected job count and confirms in a modal before enqueueing.
function RegenerateVocabularyAudioPanel({
  variant,
  voiceId,
  instructions,
  onToast,
}: {
  variant: 'flash' | 'voicedesign';
  voiceId: string;
  instructions: string;
  onToast: (t: { variant: 'success' | 'error'; message: string }) => void;
}) {
  const [scope, setScope] = useState<'all' | 'missing' | 'different-voice'>('missing');
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState<AdminVocabularyAudioRegenerateResult | null>(null);

  const disabled = useMemo(() => {
    if (variant === 'flash' && !voiceId) return 'Select a Qwen3 voice first.';
    if (variant === 'voicedesign' && !instructions.trim()) return 'Set voicedesign instructions first.';
    return null;
  }, [variant, voiceId, instructions]);

  async function handleDryRun() {
    if (disabled) {
      onToast({ variant: 'error', message: disabled });
      return;
    }
    setBusy(true);
    try {
      const res = await regenerateVocabularyAudio({
        scope,
        modelVariant: variant,
        voiceId: variant === 'flash' ? voiceId : undefined,
        instructions: variant === 'voicedesign' ? instructions : undefined,
        dryRun: true,
      });
      setConfirm(res);
    } catch (err) {
      onToast({ variant: 'error', message: err instanceof Error ? err.message : 'Dry-run failed.' });
    } finally {
      setBusy(false);
    }
  }

  async function handleCommit() {
    if (!confirm) return;
    setBusy(true);
    try {
      const res = await regenerateVocabularyAudio({
        scope,
        modelVariant: variant,
        voiceId: variant === 'flash' ? voiceId : undefined,
        instructions: variant === 'voicedesign' ? instructions : undefined,
        dryRun: false,
      });
      onToast({
        variant: 'success',
        message: `Queued ${res.queuedCount} terms for regeneration (batch ${res.batchId}). Worker drains in background.`,
      });
      setConfirm(null);
    } catch (err) {
      onToast({ variant: 'error', message: err instanceof Error ? err.message : 'Enqueue failed.' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      <h3 className="mb-3 flex items-center gap-2 text-xs font-bold uppercase tracking-[0.2em] text-admin-fg-muted">
        <RefreshCw className="h-3.5 w-3.5" /> Regenerate Vocabulary Audio
      </h3>
      <div className="space-y-3">
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800/60 dark:bg-amber-950/30 dark:text-amber-100">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Re-synthesises learner-facing vocabulary audio with the Qwen3 voice configured above. Old MediaAsset rows are hard-deleted from storage once the new audio is committed and no other content references them. Capped at 10,000 terms per click.
          </span>
        </div>
        <fieldset className="space-y-1">
          <legend className="mb-1 text-xs font-medium uppercase tracking-wide text-admin-fg-muted">Scope</legend>
          {([
            ['missing', 'Only terms with NO audio'],
            ['different-voice', 'Terms whose current voice ≠ selected voice'],
            ['all', 'ALL active vocabulary terms (heavy)'],
          ] as const).map(([val, label]) => (
            <label key={val} className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                name="regen-scope"
                value={val}
                checked={scope === val}
                onChange={() => setScope(val)}
              />
              {label}
            </label>
          ))}
        </fieldset>
        <Button variant="primary" onClick={handleDryRun} disabled={busy}>
          <RefreshCw className={`mr-1 h-4 w-4 ${busy ? 'animate-spin' : ''}`} />
          {busy ? 'Counting…' : 'Preview count'}
        </Button>
      </div>

      <Modal open={confirm !== null} onClose={() => setConfirm(null)} title="Confirm vocabulary audio regeneration">
        {confirm && (
          <div className="space-y-3 text-sm">
            <p>
              This will enqueue <strong>{confirm.queuedCount.toLocaleString()}</strong> vocabulary terms for re-synthesis
              using <code className="rounded bg-surface px-1.5 py-0.5">{variant}</code>
              {variant === 'flash' && voiceId && (
                <> with voice <code className="rounded bg-surface px-1.5 py-0.5">{voiceId}</code></>
              )}
              .
            </p>
            <p className="text-xs text-admin-fg-muted">
              Worker drains the queue in the background (1 in-flight, 8 retries, 429 cooldown). Old audio remains audible to learners until the new audio is committed for each term.
            </p>
            <div className="flex items-center justify-end gap-2 pt-2">
              <Button variant="secondary" onClick={() => setConfirm(null)} disabled={busy}>
                Cancel
              </Button>
              <Button variant="primary" onClick={handleCommit} disabled={busy || confirm.queuedCount === 0}>
                {busy ? 'Enqueuing…' : `Enqueue ${confirm.queuedCount.toLocaleString()} jobs`}
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </section>
  );
}
