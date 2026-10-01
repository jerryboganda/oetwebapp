import type { Metadata } from 'next';
import Link from 'next/link';
import { Fragment } from 'react';
import { ArrowLeft, MessageCircleQuestion } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { MotionItem } from '@/components/ui/motion-primitives';
import { SPEAKING_INTRO_QUESTIONS } from '@/lib/speaking-candidate-resources';

export const metadata: Metadata = {
  title: 'Speaking Intro Questions | OET with Dr Hesham',
  description:
    'Candidate reference: 12 common OET Speaking introductory questions with adaptable sample answers for all professions.',
};

/** Render [bracketed fields] and (parenthetical cues) as visible highlighted personalisation markers. */
function renderWithPlaceholders(text: string) {
  const parts = text.split(/(\[[^\]]+\]|\([^\)]+\))/g);
  return parts.map((part, i) =>
    /^\[.+\]$/.test(part) || /^\(.+\)$/.test(part) ? (
      <mark
        key={i}
        className="rounded-md bg-warning/10 px-1.5 py-0.5 font-semibold text-warning-strong"
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
    <>
      <LearnerPageHero
        eyebrow="Speaking reference · All professions"
        icon={<MessageCircleQuestion />}
        accent="speaking"
        title="Speaking Intro Questions"
        description={`${SPEAKING_INTRO_QUESTIONS.length} common introductory questions with adaptable sample answers for all professions. Each answer sits directly beneath its question — replace the highlighted details with your own.`}
        highlights={[
          { icon: <MessageCircleQuestion />, label: 'Questions', value: `${SPEAKING_INTRO_QUESTIONS.length} with sample answers` },
        ]}
        aside={(
          <Button asChild variant="outline" size="sm">
            <Link href="/speaking">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Speaking
            </Link>
          </Button>
        )}
      />

      <InlineAlert variant="warning" live="polite" title="Candidate rule">
        These are sample answers to personalise, not memorise word-for-word. Adapt the highlighted details to
        your own profession, experience, country, specialty, and career plan.
      </InlineAlert>

      <ol className="space-y-4">
        {SPEAKING_INTRO_QUESTIONS.map((item, index) => (
          <li key={item.no}>
            <MotionItem delayIndex={Math.min(index, 5)}>
              <article className={cardClassName({ padding: 'md' })} aria-labelledby={`intro-q-${item.no}`}>
                <div className="flex items-start gap-3">
                  <span
                    aria-hidden
                    className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-skill-speaking/10 text-sm font-bold tabular-nums text-skill-speaking"
                  >
                    {item.no}
                  </span>
                  <h2 id={`intro-q-${item.no}`} className="text-base font-bold text-navy sm:text-lg">
                    <span className="sr-only">{item.no}. </span>{item.question}
                  </h2>
                </div>
                <p className="mt-3 eyebrow text-primary">
                  Sample answer — personalise the bracketed details:
                </p>
                <p className="mt-1.5 max-w-prose text-sm leading-7 text-navy/85">
                  {renderWithPlaceholders(item.sampleAnswer)}
                </p>
                {item.note ? (
                  <p className="mt-2 border-t border-border pt-2 text-xs leading-5 text-muted">{item.note}</p>
                ) : null}
              </article>
            </MotionItem>
          </li>
        ))}
      </ol>

      <Card padding="md" className="border-dashed">
        <p className="text-sm leading-6 text-muted">
          Tip: practise each answer aloud in 20–30 seconds, keeping your own details. Revisit the{' '}
          <Link href="/speaking/assessment-criteria" className="font-semibold text-primary hover:underline">
            Speaking Assessment Criteria
          </Link>{' '}
          to see what the examiner listens for while you speak.
        </p>
      </Card>
    </>
  );
}
