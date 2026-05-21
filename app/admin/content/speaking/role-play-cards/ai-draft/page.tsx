'use client';

/**
 * OET Speaking — Phase 11 P11.3 — role-play card AI-assisted draft (placeholder).
 *
 * Backend wiring contract (NOT yet implemented; Phase 11 follow-up):
 *   POST /v1/admin/speaking/role-play-cards/ai-draft
 *     → routes through IAiGatewayService.BuildGroundedPrompt(
 *         Kind = Speaking,
 *         Task = GenerateRolePlayCard,
 *         FeatureCode = AiFeatureCodes.AdminSpeakingDraft)  // platform-only
 *       and returns AdminSpeakingRolePlayCardDraftResponse{ draft, warnings }.
 *
 * This page provides the admin UX surface so the navigation tree is complete
 * and the backend implementation has a stable contract to satisfy. Until the
 * endpoint ships, the form produces a deterministic starter template the
 * admin can refine and persist via the existing role-play-card create endpoint.
 */
import Link from 'next/link';
import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Input, Select } from '@/components/ui/form-controls';
import {
  adminCreateRolePlayCard,
  PROFESSION_OPTIONS,
  type CreateRolePlayCardInput,
} from '@/lib/api/speaking-role-play-cards';

interface DraftSeed {
  professionId: string;
  clinicalTopic: string;
  patientEmotion: string;
  difficulty: 'core' | 'extension' | 'exam';
}

function deterministicSeed(seed: DraftSeed): CreateRolePlayCardInput {
  const profession = seed.professionId || 'nursing';
  const topic = seed.clinicalTopic || 'medication adherence';
  const emotion = seed.patientEmotion || 'anxious';
  return {
    professionId: profession,
    scenarioTitle: `${topic} consultation — ${emotion} patient`,
    setting: 'Outpatient clinic, mid-morning',
    candidateRole: `You are the ${profession} on duty.`,
    interlocutorRole: `You are the patient.`,
    patientName: null,
    patientAge: null,
    background:
      `The patient presents about ${topic}. They are ${emotion} about their condition and recent ` +
      `treatment plan. They are looking for clear guidance and reassurance.`,
    task1: `Greet the patient and confirm their identity.`,
    task2: `Explore the patient's current concerns about ${topic}.`,
    task3: `Provide clear, jargon-free information.`,
    task4: `Negotiate a manageable next step with the patient.`,
    task5: `Summarise the plan and check for understanding before closing.`,
    allowedNotes: true,
    prepTimeSeconds: 180,
    rolePlayTimeSeconds: 300,
    patientEmotion: emotion,
    communicationGoal: `Reassure and align on a shared plan for ${topic}.`,
    clinicalTopic: topic,
    difficulty: seed.difficulty,
    criteriaFocus: ['relationshipBuilding', 'understandingPatientPerspective', 'providingStructure'],
    disclaimer:
      'Practice scenario only — not a substitute for clinical judgement or real patient care.',
    isLiveTutorEligible: true,
  };
}

export default function AdminSpeakingRolePlayAiDraftPage() {
  const router = useRouter();
  const [seed, setSeed] = useState<DraftSeed>({
    professionId: 'nursing',
    clinicalTopic: '',
    patientEmotion: 'anxious',
    difficulty: 'core',
  });
  const [draft, setDraft] = useState<CreateRolePlayCardInput | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function generate() {
    setError(null);
    setDraft(deterministicSeed(seed));
  }

  async function accept() {
    if (!draft) return;
    setSaving(true);
    setError(null);
    try {
      const created = await adminCreateRolePlayCard(draft);
      router.push(`/admin/content/speaking/role-play-cards/${encodeURIComponent(created.cardId)}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create role-play card.');
      setSaving(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="AI-assisted role-play card draft">
      <div className="space-y-6">
        <header>
          <Link href="/admin/content/speaking/role-play-cards" className="text-sm text-slate-500 hover:underline">
            ← Back to role-play cards
          </Link>
          <h1 className="text-2xl font-semibold text-slate-900">Speaking · AI-assisted role-play card draft</h1>
          <p className="text-slate-600">
            Generate a starter scenario from a profession + topic + patient-emotion seed. The admin
            remains accountable for accuracy, clinical safety, and rulebook alignment before
            publishing.
          </p>
        </header>

        <InlineAlert variant="info">
          Backend grounded-gateway endpoint (<code>POST /v1/admin/speaking/role-play-cards/ai-draft</code>) is
          tracked as a Phase 11 follow-up. Until then this page produces a deterministic starter
          scenario you can refine through the standard role-play card workflow.
        </InlineAlert>

        <Card className="space-y-3 p-4">
          <h2 className="font-semibold text-slate-900">Seed</h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Select
              value={seed.professionId}
              onChange={(e) => setSeed({ ...seed, professionId: e.target.value })}
            >
              {PROFESSION_OPTIONS.map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label}
                </option>
              ))}
            </Select>
            <Input
              placeholder="Clinical topic"
              value={seed.clinicalTopic}
              onChange={(e) => setSeed({ ...seed, clinicalTopic: e.target.value })}
            />
            <Input
              placeholder="Patient emotion"
              value={seed.patientEmotion}
              onChange={(e) => setSeed({ ...seed, patientEmotion: e.target.value })}
            />
            <Select
              value={seed.difficulty}
              onChange={(e) =>
                setSeed({
                  ...seed,
                  difficulty: e.target.value as 'core' | 'extension' | 'exam',
                })
              }
            >
              <option value="core">Core</option>
              <option value="extension">Extension</option>
              <option value="exam">Exam</option>
            </Select>
          </div>
          <div className="flex justify-end">
            <Button onClick={generate}>Generate starter</Button>
          </div>
        </Card>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {draft && (
          <Card className="space-y-3 p-4">
            <h2 className="font-semibold text-slate-900">Draft preview</h2>
            <div className="space-y-2 text-sm">
              <div>
                <strong className="text-slate-700">Title:</strong> {draft.scenarioTitle}
              </div>
              <div>
                <strong className="text-slate-700">Setting:</strong> {draft.setting}
              </div>
              <div>
                <strong className="text-slate-700">Background:</strong> {draft.background}
              </div>
              <ol className="list-decimal pl-5 space-y-1">
                {[draft.task1, draft.task2, draft.task3, draft.task4, draft.task5]
                  .filter((t): t is string => typeof t === 'string' && t.length > 0)
                  .map((t, i) => (
                    <li key={i}>{t}</li>
                  ))}
              </ol>
              <p className="text-xs text-slate-500">
                Difficulty: {draft.difficulty} · prep {draft.prepTimeSeconds}s · role-play{' '}
                {draft.rolePlayTimeSeconds}s · live-tutor eligible:{' '}
                {draft.isLiveTutorEligible ? 'yes' : 'no'}
              </p>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setDraft(null)} disabled={saving}>
                Discard
              </Button>
              <Button onClick={accept} disabled={saving}>
                {saving ? 'Creating…' : 'Accept & open card editor'}
              </Button>
            </div>
          </Card>
        )}
      </div>
    </AdminRouteWorkspace>
  );
}
