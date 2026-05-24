'use client';

/**
 * OET Speaking — Phase 11 P11.3 — role-play card AI-assisted draft.
 *
 * Backend wiring (live):
 *   POST /v1/admin/speaking/role-play-cards/ai-draft
 *     → routes through IAiGatewayService.BuildGroundedPrompt(
 *         Kind = Speaking,
 *         Task = GenerateContent,
 *         FeatureCode = AiFeatureCodes.AdminContentGeneration)  // platform-only
 *       persists a Draft RolePlayCard + paired hidden InterlocutorScript
 *       atomically, and returns the persisted card detail with an
 *       optional `warning` if the AI reply could not be parsed and a
 *       deterministic fallback was used.
 *
 * On success the admin lands directly on the card editor for review +
 * publishing — they remain accountable for clinical safety and rulebook
 * alignment before changing status from Draft.
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
  draftSpeakingRolePlayCard,
  PROFESSION_OPTIONS,
  type AdminRolePlayCardAiDraftResponse,
} from '@/lib/api/speaking-role-play-cards';

interface DraftSeed {
  professionId: string;
  clinicalTopic: string;
  patientEmotion: string;
  difficulty: 'core' | 'extension' | 'exam';
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  communicationGoal: string;
}

export default function AdminSpeakingRolePlayAiDraftPage() {
  const router = useRouter();
  const [seed, setSeed] = useState<DraftSeed>({
    professionId: 'nursing',
    clinicalTopic: '',
    patientEmotion: 'anxious',
    difficulty: 'core',
    setting: '',
    candidateRole: '',
    interlocutorRole: '',
    communicationGoal: '',
  });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  const [result, setResult] = useState<AdminRolePlayCardAiDraftResponse | null>(null);

  async function generate() {
    setError(null);
    setWarning(null);
    setResult(null);
    setSubmitting(true);
    try {
      const response = await draftSpeakingRolePlayCard({
        professionId: seed.professionId,
        topic: seed.clinicalTopic.trim() || null,
        emotion: seed.patientEmotion.trim() || null,
        difficulty: seed.difficulty,
        setting: seed.setting.trim() || null,
        candidateRole: seed.candidateRole.trim() || null,
        interlocutorRole: seed.interlocutorRole.trim() || null,
        communicationGoal: seed.communicationGoal.trim() || null,
      });
      setResult(response);
      if (response.warning) {
        setWarning(response.warning);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not draft role-play card.');
    } finally {
      setSubmitting(false);
    }
  }

  function openCardEditor() {
    if (!result) return;
    router.push(`/admin/content/speaking/role-play-cards/${encodeURIComponent(result.cardId)}`);
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="AI-assisted role-play card draft">
      <div className="space-y-6">
        <header>
          <Link href="/admin/content/speaking/role-play-cards" className="text-sm text-muted-foreground hover:underline">
            ← Back to role-play cards
          </Link>
          <h1 className="text-2xl font-semibold text-foreground">Speaking · AI-assisted role-play card draft</h1>
          <p className="text-muted-foreground">
            Generate a Draft scenario from a profession + topic + patient-emotion seed. The backend
            builds a grounded prompt against the canonical rulebook + scoring, persists the card
            and its hidden interlocutor script, and returns the saved draft for review. The admin
            remains accountable for accuracy, clinical safety, and rulebook alignment before
            publishing.
          </p>
        </header>

        <Card className="space-y-3 p-4">
          <h2 className="font-semibold text-foreground">Seed</h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Select
              value={seed.professionId}
              onChange={(e) => setSeed({ ...seed, professionId: e.target.value })}
              options={PROFESSION_OPTIONS.map((p) => ({ value: p.value, label: p.label }))}
            />
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
              options={[
                { value: 'core', label: 'Core' },
                { value: 'extension', label: 'Extension' },
                { value: 'exam', label: 'Exam' },
              ]}
            />
            <Input
              placeholder="Setting (optional)"
              value={seed.setting}
              onChange={(e) => setSeed({ ...seed, setting: e.target.value })}
            />
            <Input
              placeholder="Candidate role (optional)"
              value={seed.candidateRole}
              onChange={(e) => setSeed({ ...seed, candidateRole: e.target.value })}
            />
            <Input
              placeholder="Interlocutor role (optional)"
              value={seed.interlocutorRole}
              onChange={(e) => setSeed({ ...seed, interlocutorRole: e.target.value })}
            />
            <Input
              placeholder="Communication goal (optional)"
              value={seed.communicationGoal}
              onChange={(e) => setSeed({ ...seed, communicationGoal: e.target.value })}
            />
          </div>
          <div className="flex justify-end">
            <Button onClick={generate} disabled={submitting}>
              {submitting ? 'Drafting…' : 'Generate role-play draft'}
            </Button>
          </div>
        </Card>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        {warning && <InlineAlert variant="warning">{warning}</InlineAlert>}

        {result && (
          <Card className="space-y-3 p-4">
            <h2 className="font-semibold text-foreground">Draft saved</h2>
            <p className="text-sm text-muted-foreground">
              Card <code>{result.cardId}</code> persisted as <code>{result.card.status}</code>.
            </p>
            <dl className="grid gap-2 text-sm sm:grid-cols-2">
              <div className="sm:col-span-2">
                <dt className="font-medium text-muted-foreground">Scenario title</dt>
                <dd className="text-foreground">{result.card.scenarioTitle}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Profession</dt>
                <dd className="text-foreground">{result.card.professionId}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Difficulty</dt>
                <dd className="text-foreground">{result.card.difficulty}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="font-medium text-muted-foreground">Setting</dt>
                <dd className="text-foreground">{result.card.setting}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="font-medium text-muted-foreground">Background</dt>
                <dd className="whitespace-pre-wrap text-foreground">{result.card.background}</dd>
              </div>
            </dl>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setResult(null)}>
                Draft another
              </Button>
              <Button onClick={openCardEditor}>Open card editor</Button>
            </div>
          </Card>
        )}
      </div>
    </AdminRouteWorkspace>
  );
}
