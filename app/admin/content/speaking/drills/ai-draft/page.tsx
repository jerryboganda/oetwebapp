'use client';

/**
 * OET Speaking — Phase 11 P11.5 — drill bank AI-assisted draft.
 *
 * Backend wiring (live):
 *   POST /v1/admin/speaking/drills/ai-draft
 *     → routes through IAiGatewayService.BuildGroundedPrompt(
 *         Kind = Speaking,
 *         Task = GenerateContent,
 *         FeatureCode = AiFeatureCodes.AdminContentGeneration)  // platform-only
 *       persists a Draft SpeakingDrillItem + ContentItem atomically
 *       and returns a flat projection with an optional `warning` if
 *       the AI reply could not be parsed and the deterministic
 *       fallback was used.
 *
 * The admin reviews the persisted draft on the drill bank list and
 * edits + publishes from there.
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
  draftSpeakingDrill,
  SPEAKING_DRILL_KINDS,
  type AdminSpeakingDrillAiDraftResponse,
  type SpeakingDrillKind,
} from '@/lib/api/speaking-drills';

interface DraftSeed {
  drillKind: SpeakingDrillKind;
  professionId: string;
  weakCriterion: string;
}

export default function AdminSpeakingDrillAiDraftPage() {
  const router = useRouter();
  const [seed, setSeed] = useState<DraftSeed>({
    drillKind: 'Fluency',
    professionId: '',
    weakCriterion: 'fluency',
  });
  const [error, setError] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<AdminSpeakingDrillAiDraftResponse | null>(null);

  async function generate() {
    setError(null);
    setWarning(null);
    setResult(null);
    setSubmitting(true);
    try {
      const response = await draftSpeakingDrill({
        drillKind: seed.drillKind,
        professionId: seed.professionId.trim() || null,
        criterionFocus: seed.weakCriterion.trim() || null,
      });
      setResult(response);
      if (response.warning) {
        setWarning(response.warning);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not draft drill.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="AI-assisted speaking drill draft">
      <div className="space-y-6">
        <header>
          <Link href="/admin/content/speaking/drills" className="text-sm text-muted-foreground hover:underline">
            ← Back to drill bank
          </Link>
          <h1 className="text-2xl font-semibold text-foreground">Speaking · AI-assisted drill draft</h1>
          <p className="text-muted-foreground">
            Generate a Draft drill from a profession + weak-criterion seed. The backend builds a
            grounded prompt against the canonical rulebook + scoring and persists the draft
            automatically. Review and publish from the drill bank list — the admin remains
            accountable for the published content.
          </p>
        </header>

        <Card className="space-y-3 p-4">
          <h2 className="font-semibold text-foreground">Seed</h2>
          <div className="grid gap-3 sm:grid-cols-3">
            <Select
              value={seed.drillKind}
              onChange={(e) => setSeed({ ...seed, drillKind: e.target.value as SpeakingDrillKind })}
              options={SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k }))}
            />
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
            <Button onClick={generate} disabled={submitting}>
              {submitting ? 'Drafting…' : 'Generate drill draft'}
            </Button>
          </div>
        </Card>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        {warning && <InlineAlert variant="warning">{warning}</InlineAlert>}

        {result && (
          <Card className="space-y-3 p-4">
            <h2 className="font-semibold text-foreground">Draft saved</h2>
            <p className="text-sm text-muted-foreground">
              Drill <code>{result.drillId}</code> persisted as <code>{result.status}</code>.
            </p>
            <dl className="grid gap-2 text-sm sm:grid-cols-2">
              <div>
                <dt className="font-medium text-muted-foreground">Title</dt>
                <dd className="text-foreground">{result.title}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Drill kind</dt>
                <dd className="text-foreground">{result.drillKind}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Profession</dt>
                <dd className="text-foreground">{result.professionId ?? '—'}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Target criteria</dt>
                <dd className="text-foreground">{result.targetCriteria.join(', ') || '—'}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="font-medium text-muted-foreground">Instruction</dt>
                <dd className="whitespace-pre-wrap text-foreground">{result.instructionText}</dd>
              </div>
            </dl>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setResult(null)}>
                Draft another
              </Button>
              <Button onClick={() => router.push('/admin/content/speaking/drills')}>
                Open drill bank
              </Button>
            </div>
          </Card>
        )}
      </div>
    </AdminRouteWorkspace>
  );
}
