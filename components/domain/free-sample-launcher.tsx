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
  /** @deprecated Ignored: state copy comes from messages/{en,ar}/free-samples.json. Kept so older call sites still compile. */
  usedLabel?: string;
  className?: string;
}

// Exact owner copy (retry addendum, 23 Sep 2026) lives in messages/{en,ar}/free-samples.json.
const ALLOWANCE_KEY: Record<FreeSampleSubtest, string> = {
  speaking: 'freeSample.speaking.allowance',
  writing: 'freeSample.writing.allowance',
};
const RETRY_KEY: Record<FreeSampleSubtest, string> = {
  speaking: 'freeSample.speaking.retryCta',
  writing: 'freeSample.writing.retryCta',
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
      // Server route; Writing falls back to the revise page of the last graded letter.
      href = option.route ?? (option.lastSubmissionId
        ? `/writing/submissions/${encodeURIComponent(option.lastSubmissionId)}/revise`
        : null);
      footer = note(t(RETRY_KEY[subtest]), 'cta');
      break;
    case 'in_progress':
      href = option.lastResultRoute ?? option.route;
      footer = note(t('freeSample.inProgress'));
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
