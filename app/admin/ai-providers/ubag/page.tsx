'use client';

/**
 * UBAG provider control board — ON/OFF toggles routing OET AI features to
 * the UBAG OpenAI facade (`http://ubag-vps-gateway-1:8080/v1/openai`,
 * provider Code `ubag`, OpenAiCompatible dialect) instead of the Anthropic /
 * OpenAI defaults. Every toggle writes an AiFeatureRoute row (or deletes it),
 * so changes are instant and need no deploy. Rollback is the same switch.
 *
 * Grouping (see docs/AI-USAGE-POLICY.md UBAG section):
 *  A. Admin & content drafts — safe first-adopter group (internal users).
 *  B. Learner, non-scoring — advisory features that tolerate 10–60s latency.
 *  C. Conversation partner — works, but every reply waits on a browser job.
 *  D. Scoring-critical — behind an explicit confirmation modal; browser-model
 *     grading quality is unverified vs the Anthropic baseline.
 *  E. Not servable by UBAG — locked rows with reasons (STT/TTS/OCR/audio/
 *     embeddings/strict-JSON/non-routable).
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { PlugZap } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  deleteAiFeatureRoute,
  discoverAiProviderModels,
  fetchAiFeatureRoutes,
  fetchAiProviders,
  testAiProvider,
  updateAiProvider,
  upsertAiFeatureRoute,
  type AiFeatureRouteRow,
  type AiProviderRow,
  type AiProviderTestStatus,
} from '@/lib/ai-management-api';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type Risk = 'standard' | 'latency' | 'scoring' | 'locked';

interface BoardFeature {
  code: string;
  label: string;
  /** Facade model ID used when the toggle is switched on. */
  model: string;
  note?: string;
}

interface BoardGroup {
  id: string;
  title: string;
  blurb: string;
  risk: Risk;
  features: BoardFeature[];
  lockedReason?: string;
}

const UBAG_PROVIDER_CODE = 'ubag';

const UBAG_MODEL_FALLBACK = [
  'mock',
  'chatgpt_web',
  'chatgpt_web|GPT-5.6 Sol',
  'deepseek_web',
  'deepseek_web|Instant',
  'gemini_web',
  'gemini_web|3.6 Flash',
  'claude_web',
  'mistral_lechat',
  'perplexity_web',
];

const CHATGPT = 'chatgpt_web';
const DEEPSEEK = 'deepseek_web';

const GROUPS: BoardGroup[] = [
  {
    id: 'admin',
    title: 'A · Admin & content drafts',
    blurb: 'Internal users, async-tolerant, zero learner risk. Recommended first.',
    risk: 'standard',
    features: [
      { code: 'admin.content_generation', label: 'Content generation', model: CHATGPT },
      { code: 'admin.grammar_draft', label: 'Grammar lesson drafting', model: CHATGPT },
      { code: 'admin.pronunciation_draft', label: 'Pronunciation drill drafting', model: CHATGPT },
      { code: 'admin.conversation_draft', label: 'Conversation scenario drafting', model: DEEPSEEK },
      { code: 'admin.vocabulary_draft', label: 'Vocabulary term drafting', model: DEEPSEEK },
      { code: 'admin.listening_draft', label: 'Listening structure drafting', model: CHATGPT },
      { code: 'admin.reading_draft', label: 'Reading extraction drafting', model: CHATGPT },
      { code: 'admin.writing_draft', label: 'Writing material drafting', model: CHATGPT },
      { code: 'admin.listening.skill_tag', label: 'Listening skill tagging', model: CHATGPT },
      { code: 'admin.listening.transcript_segment', label: 'Transcript segmentation', model: CHATGPT },
      { code: 'writing.model_answer_pregenerate', label: 'Writing model-answer pregeneration', model: CHATGPT, note: 'Admin-triggered, one-time per task — not itself a score.' },
      { code: 'card.draft.v1', label: 'Role-play card drafting', model: CHATGPT },
    ],
  },
  {
    id: 'learner',
    title: 'B · Learner, non-scoring',
    blurb: 'Advisory features. Accept 10–60s latency before enabling each one.',
    risk: 'standard',
    features: [
      { code: 'vocabulary.gloss', label: 'Word gloss (ideal canary)', model: DEEPSEEK },
      { code: 'summarise.passage', label: 'Study-notes summarisation', model: DEEPSEEK },
      { code: 'reading.explanation.v1', label: 'Reading explanations', model: CHATGPT },
      { code: 'reading.passage_qna.v1', label: 'Passage Q&A', model: CHATGPT },
      { code: 'reading.vocabulary.card', label: 'Reading vocabulary cards', model: DEEPSEEK },
      { code: 'listening.explanation.v1', label: 'Listening explanations', model: CHATGPT },
      { code: 'pronunciation.tip', label: 'Pronunciation tips', model: CHATGPT },
      { code: 'pronunciation.feedback', label: 'Pronunciation coaching', model: CHATGPT },
      { code: 'writing.coach.suggest', label: 'Inline writing suggestions', model: CHATGPT },
      { code: 'writing.coach.explain', label: 'Why-is-this-wrong explanations', model: CHATGPT },
      { code: 'writing.coach.v1', label: 'Writing coach v1', model: CHATGPT },
      { code: 'writing.rewrite.v1', label: 'Writing rewrite', model: CHATGPT },
      { code: 'writing.scenario.generate.v1', label: 'Scenario generation', model: CHATGPT },
      { code: 'writing.outline.v1', label: 'Outline drafting', model: CHATGPT },
      { code: 'writing.paraphrase.v1', label: 'Paraphrase drills', model: CHATGPT },
      { code: 'writing.ask.v1', label: 'Writing Q&A', model: CHATGPT },
      { code: 'writing.canon.detect.v1', label: 'Canon detection', model: CHATGPT },
      { code: 'recalls.mistake_explain', label: 'Recall mistake explanations', model: DEEPSEEK },
      { code: 'recalls.revision_plan', label: 'Recall revision plans', model: DEEPSEEK },
      { code: 'mock.remediation_draft', label: 'Mock remediation intro', model: CHATGPT, note: 'Non-scoring enrichment text only.' },
      { code: 'tutor.recommendation.v1', label: 'Tutor recommendations', model: CHATGPT },
      { code: 'class.assistant.qna.v1', label: 'Class assistant Q&A', model: CHATGPT },
      { code: 'class.recording.translate.v1', label: 'Class summary translation (EN→AR)', model: CHATGPT },
    ],
  },
  {
    id: 'conversation',
    title: 'C · Conversation partner',
    blurb: 'Works, but every reply waits on a 10–60s browser job. Enable only with UX acceptance.',
    risk: 'latency',
    features: [
      { code: 'conversation.opening', label: 'AI partner opening', model: DEEPSEEK },
      { code: 'conversation.reply', label: 'AI partner mid-session replies', model: DEEPSEEK },
      { code: 'speaking.patient.turn.v1', label: 'Speaking patient turns', model: DEEPSEEK },
      { code: 'ai_assistant.admin', label: 'AI assistant (admin)', model: CHATGPT },
      { code: 'ai_assistant.expert', label: 'AI assistant (expert)', model: CHATGPT },
      { code: 'ai_assistant.learner', label: 'AI assistant (learner)', model: CHATGPT },
    ],
  },
  {
    id: 'scoring',
    title: 'D · Scoring-critical',
    blurb: 'Affects score predictions. Browser-model grading is unverified vs Anthropic — confirm each enable, and run a parallel-evaluation window first.',
    risk: 'scoring',
    features: [
      { code: 'writing.grade', label: 'Writing grading', model: CHATGPT },
      { code: 'writing.sample_score', label: 'Writing sample scoring', model: CHATGPT },
      { code: 'writing.drill.grade.v1', label: 'Drill grading', model: CHATGPT },
      { code: 'writing.appeal.v1', label: 'Appeal second opinion', model: CHATGPT },
      { code: 'speaking.grade', label: 'Speaking evaluation', model: CHATGPT },
      { code: 'speaking.score.v2', label: 'Speaking dual-grader v2', model: CHATGPT },
      { code: 'mock.full_grade', label: 'Full mock grading', model: CHATGPT, note: 'Applies only where AI grading is allowed; mock contexts stay human-marked by policy.' },
      { code: 'conversation.evaluation', label: 'Post-session scoring + rubric', model: CHATGPT },
      { code: 'pronunciation.score', label: 'Pronunciation attempt scoring', model: CHATGPT },
    ],
  },
  {
    id: 'locked',
    title: 'E · Not servable by UBAG',
    blurb: 'Locked OFF with reasons. UBAG has no STT/TTS/OCR/audio/embeddings and cannot guarantee strict JSON.',
    risk: 'locked',
    lockedReason: 'Capability gap — keep on existing providers.',
    features: [
      { code: 'pronunciation.linguistic.score.v1', label: 'Linguistic pronunciation scoring', model: '', note: 'Requires Gemini native inline-audio.' },
      { code: 'ocr.listening.parta', label: 'Listening Part A OCR', model: '', note: 'OCR — Mistral only.' },
      { code: 'ocr.content.pdf_fallback', label: 'Scanned-PDF OCR fallback', model: '', note: 'OCR — Mistral only.' },
      { code: 'ocr.writing.handwriting', label: 'Handwriting OCR', model: '', note: 'OCR — Mistral only.' },
      { code: 'ocr.listening.partbc', label: 'Listening Part B/C OCR', model: '', note: 'OCR — Mistral only.' },
      { code: 'listening.parta.extract', label: 'Part A manifest structuring', model: '', note: 'Not gateway-routable (direct Claude call).' },
      { code: 'listening.parta.score', label: 'Part A per-gap marking', model: '', note: 'Not gateway-routable (direct Claude call).' },
      { code: 'listening.partbc.extract', label: 'Part B/C key structuring', model: '', note: 'Not gateway-routable (direct Claude call).' },
      { code: 'stt.speaking.transcribe', label: 'Speaking transcription', model: '', note: 'ASR — Whisper only.' },
      { code: 'stt.pronunciation.transcribe', label: 'Pronunciation transcription', model: '', note: 'ASR — Whisper only.' },
      { code: 'stt.conversation.transcribe', label: 'Conversation transcription', model: '', note: 'ASR — Whisper only.' },
      { code: 'class.recording.transcribe.v1', label: 'Class recording transcription', model: '', note: 'ASR — Whisper only.' },
      { code: 'embeddings.generate', label: 'Embeddings', model: '', note: 'No embedding model behind UBAG.' },
      { code: 'writing.exemplar.embed.v1', label: 'Exemplar embeddings', model: '', note: 'No embedding model behind UBAG.' },
      { code: 'class.recording.summarize.v1', label: 'Class summary JSON', model: '', note: 'Requires strict JSON output.' },
    ],
  },
];

const KNOWN_GROUP_CODES = new Set(GROUPS.flatMap((g) => g.features.map((f) => f.code)));

function riskBadge(risk: Risk) {
  switch (risk) {
    case 'standard':
      return <Badge variant="muted">standard</Badge>;
    case 'latency':
      return <Badge variant="warning">latency-sensitive</Badge>;
    case 'scoring':
      return <Badge variant="danger">scoring-critical</Badge>;
    case 'locked':
      return <Badge variant="muted">locked</Badge>;
  }
}

function testStatusVariant(status: AiProviderTestStatus): 'success' | 'danger' | 'warning' | 'muted' {
  switch (status) {
    case 'ok':
      return 'success';
    case 'auth':
      return 'danger';
    case 'rate_limited':
      return 'warning';
    case 'network':
    case 'unknown':
    default:
      return 'muted';
  }
}

export default function UbagBoardPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [error, setError] = useState<string | null>(null);
  const [providers, setProviders] = useState<AiProviderRow[]>([]);
  const [routes, setRoutes] = useState<AiFeatureRouteRow[]>([]);
  const [knownCodes, setKnownCodes] = useState<string[]>([]);
  const [models, setModels] = useState<string[]>(UBAG_MODEL_FALLBACK);
  const [modelDrafts, setModelDrafts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [confirmScoring, setConfirmScoring] = useState<BoardFeature[] | null>(null);
  const [rotatingKey, setRotatingKey] = useState(false);
  const [newPat, setNewPat] = useState('');
  const [testing, setTesting] = useState(false);

  const ubag = useMemo(() => providers.find((p) => p.code === UBAG_PROVIDER_CODE) ?? null, [providers]);
  const ubagReady = !!ubag && ubag.isActive && ubag.apiKeyHint !== '';

  const routesByCode = useMemo(() => {
    const map = new Map<string, AiFeatureRouteRow>();
    for (const r of routes) {
      if (r.isActive) map.set(r.featureCode, r);
    }
    return map;
  }, [routes]);

  const otherCodes = useMemo(
    () => knownCodes.filter((c) => !KNOWN_GROUP_CODES.has(c)).sort(),
    [knownCodes],
  );

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const [p, r] = await Promise.all([fetchAiProviders(), fetchAiFeatureRoutes()]);
      setProviders(p);
      setRoutes(r.rows);
      setKnownCodes(r.knownFeatureCodes);
      setStatus('success');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load UBAG board.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    queueMicrotask(() => {
      void load();
    });
  }, [load]);

  const fail = (action: string, e: unknown) => {
    setToast({ variant: 'error', message: `${action} failed: ${(e as Error).message}` });
  };

  const enableFeature = async (feature: BoardFeature, model?: string) => {
    const chosen = model ?? modelDrafts[feature.code] ?? feature.model;
    setBusy(feature.code);
    try {
      await upsertAiFeatureRoute({
        featureCode: feature.code,
        providerCode: UBAG_PROVIDER_CODE,
        model: chosen || null,
        isActive: true,
      });
      setModelDrafts((d) => ({ ...d, [feature.code]: chosen }));
      setToast({ variant: 'success', message: `${feature.code} → UBAG (${chosen}).` });
      await load();
    } catch (e) {
      fail(`Enable ${feature.code}`, e);
    } finally {
      setBusy(null);
    }
  };

  const disableFeature = async (code: string) => {
    setBusy(code);
    try {
      await deleteAiFeatureRoute(code);
      setToast({ variant: 'success', message: `${code} back to default provider.` });
      await load();
    } catch (e) {
      fail(`Disable ${code}`, e);
    } finally {
      setBusy(null);
    }
  };

  const enableGroup = async (group: BoardGroup) => {
    if (group.risk === 'locked') return;
    if (group.risk === 'scoring') {
      setConfirmScoring(group.features);
      return;
    }
    setBusy(`group:${group.id}`);
    try {
      for (const f of group.features) {
        const chosen = modelDrafts[f.code] ?? f.model;
        await upsertAiFeatureRoute({
          featureCode: f.code,
          providerCode: UBAG_PROVIDER_CODE,
          model: chosen || null,
          isActive: true,
        });
      }
      setToast({ variant: 'success', message: `${group.title}: ${group.features.length} features → UBAG.` });
      await load();
    } catch (e) {
      fail(`Enable ${group.title}`, e);
    } finally {
      setBusy(null);
    }
  };

  const confirmEnableScoring = async () => {
    if (!confirmScoring) return;
    const features = confirmScoring;
    setConfirmScoring(null);
    setBusy('group:scoring');
    try {
      for (const f of features) {
        const chosen = modelDrafts[f.code] ?? f.model;
        await upsertAiFeatureRoute({
          featureCode: f.code,
          providerCode: UBAG_PROVIDER_CODE,
          model: chosen || null,
          isActive: true,
        });
      }
      setToast({ variant: 'success', message: `${features.length} scoring features → UBAG. Watch grading quality.` });
      await load();
    } catch (e) {
      fail('Enable scoring features', e);
    } finally {
      setBusy(null);
    }
  };

  const disableGroup = async (group: BoardGroup) => {
    setBusy(`group:${group.id}`);
    try {
      for (const f of group.features) {
        if (routesByCode.has(f.code)) await deleteAiFeatureRoute(f.code);
      }
      setToast({ variant: 'success', message: `${group.title} back to defaults.` });
      await load();
    } catch (e) {
      fail(`Disable ${group.title}`, e);
    } finally {
      setBusy(null);
    }
  };

  const toggleProvider = async () => {
    if (!ubag) return;
    setBusy('provider');
    try {
      await updateAiProvider(ubag.id, { ...ubag, isActive: !ubag.isActive });
      setToast({ variant: 'success', message: ubag.isActive ? 'UBAG provider deactivated — all traffic on defaults.' : 'UBAG provider activated.' });
      await load();
    } catch (e) {
      fail('Toggle UBAG provider', e);
    } finally {
      setBusy(null);
    }
  };

  const runTest = async () => {
    setTesting(true);
    try {
      const result = await testAiProvider(UBAG_PROVIDER_CODE);
      setToast({
        variant: result.status === 'ok' ? 'success' : 'error',
        message: `UBAG test: ${result.status}${result.errorMessage ? ', ' + result.errorMessage : ''} (${result.latencyMs} ms)`,
      });
      await load();
    } catch (e) {
      fail('UBAG test', e);
    } finally {
      setTesting(false);
    }
  };

  const discoverModels = async () => {
    setBusy('models');
    try {
      const result = await discoverAiProviderModels(UBAG_PROVIDER_CODE);
      if (result.models.length > 0) setModels(result.models);
      setToast({ variant: 'success', message: `Discovered ${result.models.length} facade models.` });
    } catch (e) {
      fail('Discover models', e);
    } finally {
      setBusy(null);
    }
  };

  const rotatePat = async () => {
    if (!ubag || newPat.trim().length < 16) {
      setToast({ variant: 'error', message: 'PAT must be at least 16 characters.' });
      return;
    }
    setBusy('pat');
    try {
      await updateAiProvider(ubag.id, { ...ubag, apiKey: newPat.trim() });
      setNewPat('');
      setRotatingKey(false);
      setToast({ variant: 'success', message: 'UBAG PAT rotated.' });
      await load();
    } catch (e) {
      fail('Rotate PAT', e);
    } finally {
      setBusy(null);
    }
  };

  const renderFeatureRow = (feature: BoardFeature, group: BoardGroup) => {
    const route = routesByCode.get(feature.code);
    const onUbag = !!route && route.providerCode === UBAG_PROVIDER_CODE;
    const effective = route ? `${route.providerCode}${route.model ? ` · ${route.model}` : ''}` : 'default';
    const draft = modelDrafts[feature.code] ?? route?.model ?? feature.model;
    const isBusy = busy === feature.code;
    return (
      <tr key={feature.code} className="border-t border-[var(--border)]">
        <td className="px-3 py-2">
          <div className="font-medium">{feature.label}</div>
          <div className="text-xs text-muted-foreground font-mono">{feature.code}</div>
          {feature.note && <div className="text-xs text-muted-foreground mt-0.5">{feature.note}</div>}
        </td>
        <td className="px-3 py-2">
          <Badge variant={onUbag ? 'default' : 'muted'}>{effective}</Badge>
        </td>
        <td className="px-3 py-2">
          <Select
            aria-label={`Model for ${feature.code}`}
            value={draft}
            disabled={group.risk === 'locked' || isBusy}
            options={(models.includes(draft) || draft === '' ? models : [...models, draft]).map((m) => ({
              value: m,
              label: m,
            }))}
            onChange={(e) => {
              const next = e.target.value;
              setModelDrafts((d) => ({ ...d, [feature.code]: next }));
              if (onUbag) void enableFeature(feature, next);
            }}
          />
        </td>
        <td className="px-3 py-2 text-right">
          {group.risk === 'locked' ? (
            <Badge variant="muted">{group.lockedReason ?? 'locked'}</Badge>
          ) : onUbag ? (
            <Button variant="primary" size="sm" aria-pressed="true" disabled={isBusy} onClick={() => void disableFeature(feature.code)}>
              ON
            </Button>
          ) : (
            <Button
              variant="outline"
              size="sm"
              aria-pressed="false"
              disabled={isBusy || !ubagReady}
              title={!ubagReady ? 'Activate the UBAG provider and set its PAT first' : `Route ${feature.code} to UBAG`}
              onClick={() => {
                if (group.risk === 'scoring') setConfirmScoring([feature]);
                else void enableFeature(feature);
              }}
            >
              OFF
            </Button>
          )}
        </td>
      </tr>
    );
  };

  return (
    <AdminTableLayout
      title="UBAG provider board"
      description="Route OET AI features to the UBAG browser-AI facade with ON/OFF switches. Changes apply instantly — no deploy."
    >
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AsyncStateWrapper
        status={status === 'loading' ? 'loading' : status === 'error' ? 'error' : 'success'}
        errorMessage={error ?? undefined}
      >
        {!ubag ? (
          <EmptyState
            title="UBAG provider not seeded"
            description="Deploy the backend containing UbagProviderSeeder, then restart the API so the ubag row is created. Paste the tenant PAT to activate."
          />
        ) : (
          <Card className="mb-4">
            <CardHeader>
              <CardTitle>
                <span className="inline-flex items-center gap-2">
                  <PlugZap size={16} /> UBAG provider
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <Badge variant={ubag.isActive ? 'success' : 'muted'}>{ubag.isActive ? 'active' : 'inactive'}</Badge>
                <Badge variant={ubag.apiKeyHint ? 'success' : 'warning'}>
                  {ubag.apiKeyHint ? `PAT ${ubag.apiKeyHint}` : 'no PAT'}
                </Badge>
                {ubag.lastTestStatus && <Badge variant={testStatusVariant(ubag.lastTestStatus)}>test: {ubag.lastTestStatus}</Badge>}
                <span className="text-muted-foreground font-mono text-xs">{ubag.baseUrl} · default {ubag.defaultModel}</span>
              </div>
              <div className="text-xs text-muted-foreground mt-2">
                UBAG usage figures are character-based estimates, not metered model tokens. Prompts flow into
                platform-owned provider browser sessions (approved).
              </div>
              <div className="flex flex-wrap gap-2 mt-3">
                <Button variant={ubag.isActive ? 'outline' : 'primary'} size="sm" disabled={busy === 'provider'} onClick={() => void toggleProvider()}>
                  {ubag.isActive ? 'Deactivate provider' : 'Activate provider'}
                </Button>
                <Button variant="outline" size="sm" disabled={testing || !ubag.apiKeyHint} onClick={() => void runTest()}>
                  {testing ? 'Testing…' : 'Test connection'}
                </Button>
                <Button variant="outline" size="sm" disabled={busy === 'models'} onClick={() => void discoverModels()}>
                  Discover models
                </Button>
                <Button variant="outline" size="sm" onClick={() => setRotatingKey(true)}>
                  Rotate PAT
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {GROUPS.map((group) => (
          <Card key={group.id} className="mb-4">
            <CardHeader>
              <CardTitle>
                <span className="inline-flex items-center gap-2">
                  {group.title} {riskBadge(group.risk)}
                </span>
              </CardTitle>
              <p className="text-sm text-muted-foreground mt-1">{group.blurb}</p>
              {group.risk !== 'locked' && (
                <div className="flex gap-2 mt-2">
                  <Button variant="outline" size="sm" disabled={busy === `group:${group.id}`} onClick={() => void enableGroup(group)}>
                    Enable group
                  </Button>
                  <Button variant="outline" size="sm" disabled={busy === `group:${group.id}`} onClick={() => void disableGroup(group)}>
                    Disable group
                  </Button>
                </div>
              )}
            </CardHeader>
            <CardContent>
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-muted-foreground">
                    <th className="px-3 py-1 font-medium">Feature</th>
                    <th className="px-3 py-1 font-medium">Effective route</th>
                    <th className="px-3 py-1 font-medium">UBAG model</th>
                    <th className="px-3 py-1 font-medium text-right">UBAG</th>
                  </tr>
                </thead>
                <tbody>{group.features.map((f) => renderFeatureRow(f, group))}</tbody>
              </table>
            </CardContent>
          </Card>
        ))}

        {otherCodes.length > 0 && (
          <Card className="mb-4">
            <CardHeader>
              <CardTitle>Other known features</CardTitle>
              <p className="text-sm text-muted-foreground mt-1">
                Recognised by the gateway but not classified for UBAG yet. Toggling routes them with the provider default model.
              </p>
            </CardHeader>
            <CardContent>
              <table className="w-full text-sm">
                <tbody>
                  {otherCodes.map((code) => {
                    const route = routesByCode.get(code);
                    const onUbag = !!route && route.providerCode === UBAG_PROVIDER_CODE;
                    return (
                      <tr key={code} className="border-t border-[var(--border)]">
                        <td className="px-3 py-2 font-mono text-xs">{code}</td>
                        <td className="px-3 py-2">
                          <Badge variant={onUbag ? 'default' : 'muted'}>{route ? route.providerCode : 'default'}</Badge>
                        </td>
                        <td className="px-3 py-2 text-right">
                          {onUbag ? (
                            <Button variant="primary" size="sm" aria-pressed="true" disabled={busy === code} onClick={() => void disableFeature(code)}>
                              ON
                            </Button>
                          ) : (
                            <Button
                              variant="outline"
                              size="sm"
                              aria-pressed="false"
                              disabled={busy === code || !ubagReady}
                              onClick={() =>
                                void enableFeature({ code, label: code, model: ubag?.defaultModel ?? 'mock' })
                              }
                            >
                              OFF
                            </Button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </CardContent>
          </Card>
        )}
      </AsyncStateWrapper>

      <Modal open={confirmScoring !== null} onClose={() => setConfirmScoring(null)} title="Enable scoring-critical UBAG routing?">
        <p className="text-sm text-muted-foreground">
          This routes score-affecting features to browser models whose grading quality is unverified vs the Anthropic
          baseline. Confirm you have run (or accept the risk of skipping) a parallel-evaluation window.
        </p>
        <div className="flex gap-2 mt-4 justify-end">
          <Button variant="outline" onClick={() => setConfirmScoring(null)}>
            Cancel
          </Button>
          <Button variant="primary" onClick={() => void confirmEnableScoring()}>
            Enable anyway
          </Button>
        </div>
      </Modal>

      <Modal open={rotatingKey} onClose={() => { setRotatingKey(false); setNewPat(''); }} title="Rotate UBAG PAT">
        <p className="text-sm text-muted-foreground">
          Paste the new tenant PAT (issued via UBAG POST /v1/auth/pat for tenant_oet / oet-platform). It is encrypted
          at rest and never displayed again.
        </p>
        <Input
          aria-label="New UBAG PAT"
          type="password"
          value={newPat}
          onChange={(e) => setNewPat(e.target.value)}
          placeholder="ubag_pat_…"
          className="mt-2"
        />
        <div className="flex gap-2 mt-4 justify-end">
          <Button variant="outline" onClick={() => { setRotatingKey(false); setNewPat(''); }}>
            Cancel
          </Button>
          <Button variant="primary" disabled={busy === 'pat'} onClick={() => void rotatePat()}>
            Save PAT
          </Button>
        </div>
      </Modal>
    </AdminTableLayout>
  );
}
