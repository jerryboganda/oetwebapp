import type { Metadata } from 'next';
import Link from 'next/link';
import { ArrowLeft, BadgeCheck, ClipboardList } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Accordion } from '@/components/ui/accordion';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import {
  SPEAKING_CLINICAL_CRITERIA,
  SPEAKING_LINGUISTIC_CRITERIA,
} from '@/lib/speaking-candidate-resources';

export const metadata: Metadata = {
  title: 'Speaking Assessment Criteria | OET with Dr Hesham',
  description:
    'Candidate reference: the 9 OET Speaking assessment criteria, grouped into 4 linguistic and 5 clinical descriptors.',
};

const overviewRows = [
  ...SPEAKING_LINGUISTIC_CRITERIA.map((criterion) => ({
    id: criterion.id,
    name: criterion.name,
    scale: '0–6',
  })),
  ...SPEAKING_CLINICAL_CRITERIA.map((criterion) => ({
    id: criterion.id,
    name: criterion.name,
    scale: '0–3',
  })),
];

export default function SpeakingAssessmentCriteriaPage() {
  return (
    <>
      <LearnerPageHero
        eyebrow="Speaking reference · All professions"
        icon={<ClipboardList />}
        accent="speaking"
        title="Speaking Assessment Criteria"
        highlights={[
          { icon: <ClipboardList />, label: 'Criteria', value: '9 total' },
          { icon: <BadgeCheck />, label: 'Scales', value: '0–6 / 0–3' },
        ]}
        aside={(
          <Button asChild variant="outline" size="sm">
            <Link href="/speaking">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Speaking
            </Link>
          </Button>
        )}
      />

      <MotionSection>
        <Card padding="md">
          <LearnerSurfaceSectionHeader
            title="At a glance"
            description="Language criteria are scored 0–6; clinical communication criteria are scored 0–3."
          />
          <div className="mt-4 overflow-x-auto rounded-xl border border-border">
            <table className="w-full min-w-[420px] border-collapse text-start text-sm">
              <thead>
                <tr className="bg-lavender/60 text-navy">
                  <th scope="col" className="w-14 px-4 py-3 text-start eyebrow">
                    No.
                  </th>
                  <th scope="col" className="px-4 py-3 text-start eyebrow">
                    Criterion
                  </th>
                  <th scope="col" className="w-28 px-4 py-3 text-end eyebrow">
                    Scale
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {overviewRows.map((criterion, index) => (
                  <tr key={criterion.id} className="bg-surface">
                    <td className="px-4 py-2.5 font-bold tabular-nums text-primary">{index + 1}</td>
                    <td className="px-4 py-2.5 font-medium text-navy">{criterion.name}</td>
                    <td className="px-4 py-2.5 text-end tabular-nums text-muted">{criterion.scale}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      </MotionSection>

      <MotionSection delayIndex={1} className="space-y-4">
        <LearnerSurfaceSectionHeader title="Linguistic criteria (0–6)" />
        <Accordion
          allowMultiple
          className="overflow-hidden bg-surface shadow-sm"
          items={SPEAKING_LINGUISTIC_CRITERIA.map((criterion, index) => ({
            id: criterion.id,
            defaultOpen: index === 0,
            title: (
              <span className="flex flex-wrap items-center gap-2">
                <span className="font-bold">{criterion.name}</span>
                <Badge variant="muted" size="sm">
                  Band 0–6
                </Badge>
              </span>
            ),
            content: (
              <div className="divide-y divide-border">
                {criterion.bands.map((band) => (
                  <div key={band.band} className="py-3 first:pt-0 last:pb-0">
                    <p className="eyebrow tabular-nums text-muted">Band {band.band}</p>
                    <ul className="mt-2 list-disc space-y-1 ps-5 text-sm leading-6 text-navy/90">
                      {band.descriptors.map((descriptor) => (
                        <li key={`${criterion.id}-${band.band}-${descriptor}`}>{descriptor}</li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            ),
          }))}
        />
      </MotionSection>

      <MotionSection delayIndex={2} className="space-y-4">
        <LearnerSurfaceSectionHeader title="Clinical criteria (0–3)" />
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          {SPEAKING_CLINICAL_CRITERIA.map((criterion, index) => (
            <MotionItem key={criterion.id} delayIndex={Math.min(index, 5)} className="h-full">
              <Card padding="md" className="h-full">
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p className="eyebrow text-muted">{criterion.letter}</p>
                    <h3 className="text-base font-bold text-navy">{criterion.name}</h3>
                  </div>
                  <Badge variant="muted" size="sm" className="shrink-0">
                    0–3
                  </Badge>
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  {criterion.scale.map((level) => (
                    <Badge key={`${criterion.id}-${level}`} variant="outline" size="sm">
                      {level}
                    </Badge>
                  ))}
                </div>
                <ul className="mt-4 list-disc space-y-2 ps-5 text-sm leading-6 text-navy/85">
                  {criterion.indicators.map((indicator) => (
                    <li key={indicator.code}>
                      <span className="font-bold text-primary">{indicator.code}</span>{' '}
                      {indicator.text}
                    </li>
                  ))}
                </ul>
              </Card>
            </MotionItem>
          ))}
        </div>
      </MotionSection>

      <Card padding="md" className="border-dashed">
        <p className="text-sm leading-6 text-muted">
          Candidate reference only. This page is separate from internal grading prompts, AI audit rules,
          and tutor teaching material. Revisit it any time from{' '}
          <Link href="/speaking/selection" className="font-semibold text-primary hover:underline">
            Speaking selection
          </Link>
          .
        </p>
      </Card>
    </>
  );
}
