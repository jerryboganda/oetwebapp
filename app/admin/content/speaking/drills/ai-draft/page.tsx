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
import { useState } from 'react';
import { useRouter } from 'next/navigation';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';

import { InlineAlert } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
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

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Drill bank', href: '/admin/content/speaking/drills' },
  { label: 'AI-assisted draft' },
];

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
    <AdminCatalogLayout
      title="Speaking · AI-assisted drill draft"
      description="Generate a Draft drill from a profession + weak-criterion seed. The backend builds a grounded prompt against the canonical rulebook + scoring and persists the draft automatically. Review and publish from the drill bank list. The admin remains accountable for the published content."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Content · AI"
      backHref="/admin/content/speaking/drills"
      hideViewModeToggle
    >
      <Card className="col-span-full">
        <CardHeader>
          <CardTitle>Seed</CardTitle>
        </CardHeader>
        <CardContent>
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
          <div className="mt-3 flex justify-end">
            <Button onClick={generate} disabled={submitting}>
              {submitting ? 'Drafting…' : 'Generate drill draft'}
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
              Drill <code>{result.drillId}</code> persisted as <code>{result.status}</code>.
            </p>
            <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
              <div>
                <dt className="font-medium text-admin-fg-muted">Title</dt>
                <dd className="text-admin-fg-strong">{result.title}</dd>
              </div>
              <div>
                <dt className="font-medium text-admin-fg-muted">Drill kind</dt>
                <dd className="text-admin-fg-strong">{result.drillKind}</dd>
              </div>
              <div>
                <dt className="font-medium text-admin-fg-muted">Profession</dt>
                <dd className="text-admin-fg-strong">{result.professionId ?? '-'}</dd>
              </div>
              <div>
                <dt className="font-medium text-admin-fg-muted">Target criteria</dt>
                <dd className="text-admin-fg-strong">{result.targetCriteria.join(', ') || '-'}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="font-medium text-admin-fg-muted">Instruction</dt>
                <dd className="whitespace-pre-wrap text-admin-fg-strong">{result.instructionText}</dd>
              </div>
            </dl>
            <div className="mt-4 flex justify-end gap-2">
              <Button variant="outline" onClick={() => setResult(null)}>
                Draft another
              </Button>
              <Button onClick={() => router.push('/admin/content/speaking/drills')}>
                Open drill bank
              </Button>
            </div>
          </CardContent>
        </Card>
      )}
    </AdminCatalogLayout>
  );
}
