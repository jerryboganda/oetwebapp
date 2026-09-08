import type { Metadata } from 'next';
import Link from 'next/link';
import { ArrowLeft, BadgeCheck, ClipboardList } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Accordion } from '@/components/ui/accordion';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
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
    <LearnerDashboardShell pageTitle="Speaking Assessment Criteria" requireAuth={false}>
      <div className="mx-auto w-full max-w-4xl space-y-6">
        <Link
          href="/speaking"
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden /> Back to Speaking
        </Link>

        <LearnerPageHero
          eyebrow="Speaking reference · All professions"
          icon={ClipboardList}
          accent="purple"
          title="Speaking Assessment Criteria"
          description="Use the same page for all professions. There are 9 criteria in total: 4 linguistic (0–6) and 5 clinical communication (0–3)."
          highlights={[
            { icon: ClipboardList, label: 'Criteria', value: '9 total' },
            { icon: BadgeCheck, label: 'Scales', value: '0–6 / 0–3' },
          ]}
        />

        <Card padding="md">
          <h2 className="text-base font-bold text-navy sm:text-lg">At a glance</h2>
          <p className="mt-1 text-sm text-muted">
            Language criteria are scored 0–6; clinical communication criteria are scored 0–3.
          </p>
          <div className="mt-4 overflow-x-auto rounded-xl border border-border">
            <table className="w-full min-w-[420px] border-collapse text-left text-sm">
              <thead>
                <tr className="bg-purple-50 text-purple-900 dark:bg-white/5 dark:text-white">
                  <th scope="col" className="w-14 px-4 py-3 text-xs font-black uppercase tracking-wider">
                    No.
                  </th>
                  <th scope="col" className="px-4 py-3 text-xs font-black uppercase tracking-wider">
                    Criterion
                  </th>
                  <th scope="col" className="w-28 px-4 py-3 text-right text-xs font-black uppercase tracking-wider">
                    Scale
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {overviewRows.map((criterion, index) => (
                  <tr key={criterion.id} className="bg-surface">
                    <td className="px-4 py-2.5 font-bold text-purple-800 dark:text-purple-200">{index + 1}</td>
                    <td className="px-4 py-2.5 font-medium text-navy">{criterion.name}</td>
                    <td className="px-4 py-2.5 text-right text-muted">{criterion.scale}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>

        <div className="space-y-3">
          <h2 className="text-base font-bold text-navy sm:text-lg">Linguistic criteria (0–6)</h2>
          <Accordion
            allowMultiple
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
                <div className="space-y-3">
                  {criterion.bands.map((band) => (
                    <div key={band.band} className="rounded-xl border border-border bg-surface p-3">
                      <p className="text-[11px] font-black uppercase tracking-wider text-muted">Band {band.band}</p>
                      <ul className="mt-2 list-disc space-y-1 pl-5 text-sm leading-6 text-navy/90 dark:text-white/90">
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
        </div>

        <div className="space-y-4">
          <h2 className="text-base font-bold text-navy sm:text-lg">Clinical criteria (0–3)</h2>
          <div className="grid gap-4 lg:grid-cols-2">
            {SPEAKING_CLINICAL_CRITERIA.map((criterion) => (
              <Card key={criterion.id} padding="md" className="h-full">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-black uppercase tracking-wider text-muted">{criterion.letter}</p>
                    <h3 className="text-base font-bold text-navy">{criterion.name}</h3>
                  </div>
                  <Badge variant="muted" size="sm">
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
                <ul className="mt-4 list-disc space-y-2 pl-5 text-sm leading-6 text-navy/85 dark:text-white/85">
                  {criterion.indicators.map((indicator) => (
                    <li key={indicator.code}>
                      <span className="font-bold text-purple-800 dark:text-purple-200">{indicator.code}</span>{' '}
                      {indicator.text}
                    </li>
                  ))}
                </ul>
              </Card>
            ))}
          </div>
        </div>

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
      </div>
    </LearnerDashboardShell>
  );
}
