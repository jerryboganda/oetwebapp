'use client';

import {
    AdminRoutePanel,
    AdminRouteSectionHeader, AdminRouteWorkspace
} from '@/components/domain/admin-route-surface';
import { AdminDashboardShell } from "@/components/layout/admin-dashboard-shell";
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import {
    adminConversationTtsPreview, fetchAdminConversationSettings,
    updateAdminConversationSettings
} from '@/lib/api';
import { ArrowLeft, Save, Settings as SettingsIcon, Volume2 } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

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
  elevenLabsDefaultVoiceId?: string;
  elevenLabsModel?: string;
  cosyVoiceBaseUrl?: string;
  cosyVoiceDefaultVoice?: string;
  chatTtsBaseUrl?: string;
  chatTtsDefaultVoice?: string;
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
      const blob = await adminConversationTtsPreview({
        voice: (draft.azureTtsDefaultVoice as string) ?? settings?.azureTtsDefaultVoice ?? '',
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
    <AdminDashboardShell>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Content"
            title="AI Conversation — Runtime Settings"
            description="Admin-editable overrides for the AI Conversation subsystem. Saved values override env defaults and take effect within 30 seconds of save. API keys are encrypted via Data Protection; leave blank to keep the current key."
            icon={SettingsIcon}
            actions={
              <div className="flex items-center gap-2">
                <Button variant="secondary" onClick={() => router.push('/admin/content/conversation')}>
                  <ArrowLeft className="mr-1 h-4 w-4" /> Back
                </Button>
                <Button variant="secondary" onClick={handlePreview}>
                  <Volume2 className="mr-1 h-4 w-4" /> TTS Preview
                </Button>
                <Button variant="primary" onClick={handleSave} disabled={saving}>
                  <Save className="mr-1 h-4 w-4" /> {saving ? 'Saving…' : 'Save'}
                </Button>
              </div>
            }
          />

          {loading || !settings ? (
            <p className="text-sm text-muted">Loading…</p>
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
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">ASR Provider</span>
                  <select
                    value={String(v('asrProvider') ?? 'auto')}
                    onChange={(e) => setField('asrProvider', e.target.value)}
                    className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm"
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

              <Section title="TTS (Text → Speech)">
                <label className="block">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">TTS Provider</span>
                  <select
                    value={String(v('ttsProvider') ?? 'auto')}
                    onChange={(e) => setField('ttsProvider', e.target.value)}
                    className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm"
                  >
                    <option value="auto">auto</option>
                    <option value="azure">Azure Speech</option>
                    <option value="elevenlabs">ElevenLabs</option>
                    <option value="cosyvoice">CosyVoice</option>
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
                  <KeyInput label="GPT-SoVITS API Key" present={settings.gptSoVitsApiKeyPresent} draftKey="gptSoVitsApiKey" draft={draft} set={setSecret} />
                  <Input label="GPT-SoVITS Base URL" value={String(v('gptSoVitsBaseUrl') ?? '')} onChange={(e) => setField('gptSoVitsBaseUrl', e.target.value)} />
                  <Input label="GPT-SoVITS Default Voice" value={String(v('gptSoVitsDefaultVoice') ?? '')} onChange={(e) => setField('gptSoVitsDefaultVoice', e.target.value)} />
                </Grid>
              </Section>
            </div>
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </AdminDashboardShell>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-3 text-xs font-bold uppercase tracking-[0.2em] text-muted">{title}</h3>
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
        <span className="text-xs font-medium uppercase tracking-wide text-muted">{label}</span>
        {present ? <Badge variant="success" size="sm">set</Badge> : <Badge variant="muted" size="sm">not set</Badge>}
      </div>
      <input
        type="password"
        placeholder="Leave blank to keep current"
        value={(draft[draftKey] as string) ?? ''}
        onChange={(e) => set(draftKey, e.target.value)}
        className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm"
      />
    </div>
  );
}
