import type { Metadata } from 'next';
import Link from 'next/link';
import { ArrowLeft, BadgeCheck, ClipboardList } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Accordion } from '@/components/ui/accordion';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { SPEAKING_CRITERIA } from '@/lib/speaking-candidate-resources';

export const metadata: Metadata = {
  title: 'Speaking Assessment Criteria | OET with Dr Hesham',
  description:
    'Candidate reference: the 9 OET Speaking assessment criteria, weights, and what to do well — same for all professions.',
};

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
          description="Use the same page for all professions. These are the 9 criteria your Speaking role-plays are assessed against — review them before you practise."
          highlights={[
            { icon: ClipboardList, label: 'Criteria', value: '9 sections' },
            { icon: BadgeCheck, label: 'Total', value: '42 points' },
          ]}
        />

        {/* Overview table — required section structure */}
        <Card padding="md">
          <h2 className="text-base font-bold text-navy sm:text-lg">At a glance</h2>
          <p className="mt-1 text-sm text-muted">
            Language criteria carry 6 points each; clinical-communication criteria carry 3 points each.
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
                    Weight
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {SPEAKING_CRITERIA.map((c) => (
                  <tr key={c.id} className="bg-surface">
                    <td className="px-4 py-2.5 font-bold text-purple-800 dark:text-purple-200">{c.no}</td>
                    <td className="px-4 py-2.5 font-medium text-navy">{c.name}</td>
                    <td className="px-4 py-2.5 text-right text-muted">{c.weight}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>

        {/* Expandable detail — one section per criterion */}
        <div className="space-y-3">
          <h2 className="text-base font-bold text-navy sm:text-lg">What each criterion means</h2>
          <Accordion
            allowMultiple
            items={SPEAKING_CRITERIA.map((c, i) => ({
              id: c.id,
              defaultOpen: i === 0,
              title: (
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-bold">
                    {c.no}. {c.name}
                  </span>
                  <Badge variant="muted" size="sm">
                    {c.weight}
                  </Badge>
                </span>
              ),
              content: (
                <div className="space-y-3">
                  <p className="font-semibold text-navy">{c.summary}</p>
                  <div>
                    <p className="text-xs font-black uppercase tracking-wider text-muted">What it means</p>
                    <ul className="mt-1.5 list-disc space-y-1 pl-5 leading-6">
                      {c.whatItMeans.map((line) => (
                        <li key={line}>{line}</li>
                      ))}
                    </ul>
                  </div>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <div className="rounded-xl bg-emerald-50/70 p-3 dark:bg-emerald-500/10">
                      <p className="text-xs font-black uppercase tracking-wider text-emerald-700 dark:text-emerald-300">
                        Do well
                      </p>
                      <ul className="mt-1.5 list-disc space-y-1 pl-5 leading-6">
                        {c.doWell.map((line) => (
                          <li key={line}>{line}</li>
                        ))}
                      </ul>
                    </div>
                    <div className="rounded-xl bg-rose-50/70 p-3 dark:bg-rose-500/10">
                      <p className="text-xs font-black uppercase tracking-wider text-rose-700 dark:text-rose-300">
                        Avoid
                      </p>
                      <ul className="mt-1.5 list-disc space-y-1 pl-5 leading-6">
                        {c.avoid.map((line) => (
                          <li key={line}>{line}</li>
                        ))}
                      </ul>
                    </div>
                  </div>
                </div>
              ),
            }))}
          />
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
