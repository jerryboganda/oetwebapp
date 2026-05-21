'use client';

/**
 * OET Speaking — Phase 11 P11.5 — drill bank AI-assisted draft (placeholder).
 *
 * Backend wiring contract (NOT yet implemented; Phase 11 follow-up):
 *   POST /v1/admin/speaking/drills/ai-draft
 *     → routes through IAiGatewayService.BuildGroundedPrompt(
 *         Kind = Speaking,
 *         Task = GenerateSpeakingDrill,
 *         FeatureCode = AiFeatureCodes.AdminSpeakingDraft)  // platform-only
 *       and returns AdminSpeakingDrillDraftResponse{ draft: AdminDrillCreateInput, warnings: [] }.
 *
 * This page provides the admin UX surface so the navigation tree is complete
 * and so that the backend implementation has a stable contract to satisfy.
 * Until the endpoint ships, the form falls back to a deterministic starter
 * template the admin can edit + manually save via the drill bank list page.
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
  createAdminDrill,
  SPEAKING_DRILL_KINDS,
  type AdminDrillCreateInput,
  type SpeakingDrillKind,
} from '@/lib/api/speaking-drills';

interface DraftSeed {
  drillKind: SpeakingDrillKind;
  professionId: string;
  weakCriterion: string;
}

function deterministicSeed(seed: DraftSeed): AdminDrillCreateInput {
  const profession = seed.professionId || 'general';
  const criterion = seed.weakCriterion || 'fluency';
  return {
    drillKind: seed.drillKind,
    professionId: seed.professionId || null,
    title: `${seed.drillKind.replace(/_/g, ' ')} drill — ${profession} (${criterion})`,
    instructionText:
      `Practice ${seed.drillKind.replace(/_/g, ' ')} for the ${criterion} criterion. ` +
      `Focus on the patient-care scenarios most common for ${profession}. ` +
      `Speak for 60–90 seconds, then review the transcript and self-mark against the rubric.`,
    targetCriteria: [criterion],
    recommendedAfterSessionScoreBelow: 350,
  };
}

export default function AdminSpeakingDrillAiDraftPage() {
  const router = useRouter();
  const [seed, setSeed] = useState<DraftSeed>({
    drillKind: 'fluency_relay',
    professionId: '',
    weakCriterion: 'fluency',
  });
  const [draft, setDraft] = useState<AdminDrillCreateInput | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  function generate() {
    setError(null);
    setDraft(deterministicSeed(seed));
  }

  async function accept() {
    if (!draft) return;
    setSaving(true);
    setError(null);
    try {
      await createAdminDrill(draft);
      router.push('/admin/content/speaking/drills');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save drill.');
      setSaving(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="AI-assisted speaking drill draft">
      <div className="space-y-6">
        <header>
          <Link href="/admin/content/speaking/drills" className="text-sm text-slate-500 hover:underline">
            ← Back to drill bank
          </Link>
          <h1 className="text-2xl font-semibold text-slate-900">Speaking · AI-assisted drill draft</h1>
          <p className="text-slate-600">
            Generate a starter drill skeleton from a profession + weak-criterion seed. Review the draft
            carefully — the canonical rulebook is the source of truth and the admin remains accountable
            for the published content.
          </p>
        </header>

        <InlineAlert variant="info">
          Backend grounded-gateway endpoint (<code>POST /v1/admin/speaking/drills/ai-draft</code>) is
          tracked as a Phase 11 follow-up. Until then this page produces a deterministic starter
          template you can refine and save through the standard drill admin endpoint.
        </InlineAlert>

        <Card className="space-y-3 p-4">
          <h2 className="font-semibold text-slate-900">Seed</h2>
          <div className="grid gap-3 sm:grid-cols-3">
            <Select
              value={seed.drillKind}
              onChange={(e) => setSeed({ ...seed, drillKind: e.target.value as SpeakingDrillKind })}
            >
              {SPEAKING_DRILL_KINDS.map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </Select>
            <Input
              placeholder="Profession id (e.g. nursing)"
              value={seed.professionId}
              onChange={(e) => setSeed({ ...seed, professionId: e.target.value })}
            />
            <Input
              placeholder="Weak criterion (e.g. fluency)"
              value={seed.weakCriterion}
              onChange={(e) => setSeed({ ...seed, weakCriterion: e.target.value })}
            />
          </div>
          <div className="flex justify-end">
            <Button onClick={generate}>Generate starter</Button>
          </div>
        </Card>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {draft && (
          <Card className="space-y-3 p-4">
            <h2 className="font-semibold text-slate-900">Draft preview</h2>
            <div className="grid gap-3 sm:grid-cols-2">
              <Input
                placeholder="Title"
                value={draft.title}
                onChange={(e) => setDraft({ ...draft, title: e.target.value })}
              />
              <Input
                placeholder="Profession id"
                value={draft.professionId ?? ''}
                onChange={(e) => setDraft({ ...draft, professionId: e.target.value || null })}
              />
              <Input
                className="sm:col-span-2"
                placeholder="Target criteria (comma-separated)"
                value={draft.targetCriteria.join(', ')}
                onChange={(e) => setDraft({ ...draft, targetCriteria: e.target.value.split(',') })}
              />
              <textarea
                className="rounded border border-slate-300 p-2 text-sm sm:col-span-2"
                rows={5}
                value={draft.instructionText}
                onChange={(e) => setDraft({ ...draft, instructionText: e.target.value })}
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setDraft(null)} disabled={saving}>
                Discard
              </Button>
              <Button onClick={accept} disabled={saving}>
                {saving ? 'Saving…' : 'Accept & create drill'}
              </Button>
            </div>
          </Card>
        )}
      </div>
    </AdminRouteWorkspace>
  );
}
