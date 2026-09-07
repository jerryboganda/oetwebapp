import type { Metadata } from 'next';
import Link from 'next/link';
import { Fragment } from 'react';
import { ArrowLeft, MessageCircleQuestion, Users } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { SPEAKING_INTRO_QUESTIONS } from '@/lib/speaking-candidate-resources';

export const metadata: Metadata = {
  title: 'Speaking Intro Questions | OET with Dr Hesham',
  description:
    'Candidate reference: 12 common OET Speaking introductory questions with adaptable sample answers for all professions.',
};

/** Render [bracketed fields] as visible highlighted personalisation cues. */
function renderWithPlaceholders(text: string) {
  const parts = text.split(/(\[[^\]]+\])/g);
  return parts.map((part, i) =>
    /^\[.+\]$/.test(part) ? (
      <mark
        key={i}
        className="rounded-md bg-amber-100 px-1.5 py-0.5 font-semibold text-amber-900 dark:bg-amber-500/20 dark:text-amber-200"
      >
        {part}
      </mark>
    ) : (
      <Fragment key={i}>{part}</Fragment>
    ),
  );
}

export default function SpeakingIntroQuestionsPage() {
  return (
    <LearnerDashboardShell pageTitle="Speaking Intro Questions">
      <div className="mx-auto w-full max-w-4xl space-y-6">
        <Link
          href="/speaking"
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden /> Back to Speaking
        </Link>

        <LearnerPageHero
          eyebrow="Speaking reference · All professions"
          icon={MessageCircleQuestion}
          accent="purple"
          title="Speaking Intro Questions"
          description="Common introductory questions with adaptable sample answers for all professions. Each answer sits directly beneath its question — replace the highlighted details with your own."
          highlights={[
            { icon: MessageCircleQuestion, label: 'Questions', value: '12 with sample answers' },
            { icon: Users, label: 'Works for', value: 'Every profession' },
          ]}
        />

        <Card padding="md" className="border-amber-200 bg-amber-50/70 dark:border-amber-500/30 dark:bg-amber-500/10">
          <p className="text-xs font-black uppercase tracking-widest text-purple-900 dark:text-amber-200">
            Candidate rule
          </p>
          <p className="mt-1.5 text-sm leading-6 text-navy dark:text-white">
            The sample answers are examples and templates to personalise. Do not memorise them word-for-word —
            adapt the highlighted details to your own profession, experience, country, specialty, and career plan.
          </p>
        </Card>

        <div className="space-y-4">
          {SPEAKING_INTRO_QUESTIONS.map((item) => (
            <Card key={item.no} padding="md" aria-labelledby={`intro-q-${item.no}`}>
              <div className="flex items-start gap-3">
                <span
                  aria-hidden
                  className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-purple-100 text-sm font-black text-purple-800 dark:bg-purple-500/20 dark:text-purple-200"
                >
                  {item.no}
                </span>
                <h2 id={`intro-q-${item.no}`} className="text-base font-bold text-navy sm:text-lg">
                  {item.no}. {item.question}
                </h2>
              </div>
              <p className="mt-3 text-xs font-black uppercase tracking-wider text-purple-800 dark:text-purple-300">
                Sample answer — personalise the bracketed details:
              </p>
              <p className="mt-1.5 text-sm leading-7 text-navy/85 dark:text-white/85">
                {renderWithPlaceholders(item.sampleAnswer)}
              </p>
              {item.note ? (
                <p className="mt-2 border-t border-border pt-2 text-xs leading-5 text-muted">{item.note}</p>
              ) : null}
            </Card>
          ))}
        </div>

        <Card padding="md" className="border-dashed">
          <p className="text-sm leading-6 text-muted">
            Tip: practise each answer aloud in 20–30 seconds, keeping your own details. Revisit the{' '}
            <Link href="/speaking/assessment-criteria" className="font-semibold text-primary hover:underline">
              Speaking Assessment Criteria
            </Link>{' '}
            to see what the examiner listens for while you speak.
          </p>
        </Card>
      </div>
    </LearnerDashboardShell>
  );
}
