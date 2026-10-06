'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import type { LucideIcon } from 'lucide-react';
import { FreeSampleCard } from '@/components/domain/free-sample-card';
import { analytics } from '@/lib/analytics';
import { listFreeSamples, type FreeSampleOption, type FreeSampleSubtest } from '@/lib/api/free-samples';

// The Free Writing / Free Speaking entry (Free Mocks proposal, 2026-09).
// Shows the FREE SAMPLE card for the learner's OWN profession — the server
// (FreeSampleService) only ever offers one, so there is no cross-profession
// picker here (22 Sep 2026 handoff, item 2: "no cross-profession free-sample
// picker" — CRITICAL SECURITY). Renders nothing while loading, when the
// feature is off, or when the learner's profession has no live sample yet.
//
// Import by direct path, not the '@/components/domain' barrel (hub tests mock it).

export interface FreeSampleLauncherProps {
  subtest: FreeSampleSubtest;
  icon: LucideIcon;
  testId: string;
  title: string;
  description: string;
  badgeLabel?: string;
  className?: string;
}

// Exact owner copy lives in messages/{en,ar}/free-samples.json. Writing keeps its second free result, but it
// is a fresh attempt on the same task (the server routes retry_available to the normal start route); a
// completed Speaking attempt has NO free retry (owner spec 4 Oct 2026): repeating the card is a new paid
// attempt, and only a failed grade can be re-run for free (the grading_failed state).
const ALLOWANCE_KEY: Record<FreeSampleSubtest, string> = {
  speaking: 'freeSample.speaking.allowance',
  writing: 'freeSample.writing.allowance',
};
const WRITING_ANOTHER_ATTEMPT_KEY = 'freeSample.writing.anotherAttempt';
const GRADING_FAILED_KEY: Record<FreeSampleSubtest, string> = {
  speaking: 'freeSample.speaking.gradingFailed',
  writing: 'freeSample.writing.gradingFailed',
};

export function FreeSampleLauncher({
  subtest,
  icon,
  testId,
  title,
  description,
  badgeLabel,
  className = 'mb-4',
}: FreeSampleLauncherProps) {
  const t = useTranslations();
  const [option, setOption] = useState<FreeSampleOption | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let cancelled = false;
    listFreeSamples(subtest)
      .then((rows) => {
        if (cancelled) return;
        // The server offers at most one row: the caller's own profession.
        setOption(Array.isArray(rows) && rows.length > 0 ? rows[0] : null);
      })
      .catch(() => {
        // The free sample is a bonus entry point: a failed lookup just hides it.
        if (!cancelled) setOption(null);
      })
      .finally(() => {
        if (!cancelled) setLoaded(true);
      });
    return () => {
      cancelled = true;
    };
  }, [subtest]);

  if (!loaded || !option) return null;

  const track = () => analytics.track('free_sample_click', { module: subtest, professionId: option.professionId, state: option.state });
  const note = (text: string, tone: 'muted' | 'cta' = 'muted') => (
    <span
      className={`mt-1 block text-xs font-semibold ${tone === 'cta' ? 'text-violet-700 dark:text-violet-200' : 'text-muted'}`}
      data-testid={`${testId}-status`}
    >
      {text}
    </span>
  );

  let href: string | null = null;
  let footer: ReturnType<typeof note>;
  switch (option.state) {
    case 'available':
      href = option.route;
      footer = note(t(ALLOWANCE_KEY[subtest]));
      break;
    case 'retry_available':
      // Speaking has no free retry: show the sample as spent rather than advertise one that does not exist.
      if (subtest === 'speaking') {
        footer = note(t('freeSample.completed'));
        break;
      }
      // The second free Writing result is a fresh attempt: the server route is the normal start route.
      href = option.route;
      footer = note(t(WRITING_ANOTHER_ATTEMPT_KEY), 'cta');
      break;
    case 'in_progress':
      // Writing: the server routes the letter being graded to its grading page,
      // while lastResultRoute is the PREVIOUS result. Speaking keeps its result page.
      href = subtest === 'writing'
        ? option.route ?? option.lastResultRoute
        : option.lastResultRoute ?? option.route;
      footer = note(t('freeSample.inProgress'));
      break;
    case 'grading_failed':
      // Retry grading of the SAME saved attempt (server route). Never the start
      // route: a fresh start could spend a second free use.
      href = option.route;
      footer = note(t(GRADING_FAILED_KEY[subtest]), 'cta');
      break;
    case 'unavailable':
      footer = note(t('freeSample.unavailable'));
      break;
    default:
      // completed (and any unknown state): spent, visible but inert.
      footer = note(t('freeSample.completed'));
  }

  return (
    <FreeSampleCard
      testId={testId}
      icon={icon}
      title={title}
      description={description}
      badgeLabel={badgeLabel}
      href={href ?? undefined}
      onClick={href ? track : undefined}
      disabled={!href}
      footer={footer}
      className={className}
    />
  );
}
