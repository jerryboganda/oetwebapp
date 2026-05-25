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
import { useState } from 'react';
import { useRouter } from 'next/navigation';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';

import { InlineAlert } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
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

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards', href: '/admin/content/speaking/role-play-cards' },
  { label: 'AI-assisted draft' },
];

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
    <AdminCatalogLayout
      title="Speaking · AI-assisted role-play card draft"
      description="Generate a Draft scenario from a profession + topic + patient-emotion seed. The backend builds a grounded prompt against the canonical rulebook + scoring, persists the card and its hidden interlocutor script, and returns the saved draft for review. The admin remains accountable for accuracy, clinical safety, and rulebook alignment before publishing."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS · AI"
      backHref="/admin/content/speaking/role-play-cards"
      hideViewModeToggle
    >
      <Card className="col-span-full">
        <CardHeader>
          <CardTitle>Seed</CardTitle>
        </CardHeader>
        <CardContent>
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
          <div className="mt-4 flex justify-end">
            <Button onClick={generate} disabled={submitting}>
              {submitting ? 'Drafting…' : 'Generate role-play draft'}
            </Button>
          </div>
        </CardContent>
      </Card>

      {error && (
        <div className="col-span-full">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      )}
      {warning && (
        <div className="col-span-full">
          <InlineAlert variant="warning">{warning}</InlineAlert>
        </div>
      )}

      {result && (
        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Draft saved</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-admin-fg-muted">
              Card <code>{result.cardId}</code> persisted as <code>{result.card.status}</code>.
            </p>
            <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
              <div className="sm:col-span-2">
                <dt className="font-medium text-admin-fg-muted">Scenario title</dt>
                <dd className="text-admin-fg-strong">{result.card.scenarioTitle}</dd>
              </div>
              <div>
                <dt className="font-medium text-admin-fg-muted">Profession</dt>
                <dd className="text-admin-fg-strong">{result.card.professionId}</dd>
              </div>
              <div>
                <dt className="font-medium text-admin-fg-muted">Difficulty</dt>
                <dd className="text-admin-fg-strong">{result.card.difficulty}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="font-medium text-admin-fg-muted">Setting</dt>
                <dd className="text-admin-fg-strong">{result.card.setting}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="font-medium text-admin-fg-muted">Background</dt>
                <dd className="whitespace-pre-wrap text-admin-fg-strong">{result.card.background}</dd>
              </div>
            </dl>
            <div className="mt-4 flex justify-end gap-2">
              <Button variant="outline" onClick={() => setResult(null)}>
                Draft another
              </Button>
              <Button onClick={openCardEditor}>Open card editor</Button>
            </div>
          </CardContent>
        </Card>
      )}
    </AdminCatalogLayout>
  );
}
