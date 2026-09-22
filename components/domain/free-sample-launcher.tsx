'use client';

import { useEffect, useState } from 'react';
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
  startLabel: string;
  usedLabel: string;
  className?: string;
}

export function FreeSampleLauncher({
  subtest,
  icon,
  testId,
  title,
  description,
  badgeLabel,
  usedLabel,
  className = 'mb-4',
}: FreeSampleLauncherProps) {
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

  const track = () => analytics.track('free_sample_click', { module: subtest, professionId: option.professionId });

  if (option.state === 'used') {
    return (
      <FreeSampleCard
        testId={testId}
        icon={icon}
        title={title}
        description={description}
        badgeLabel={badgeLabel}
        disabled
        footer={<span className="mt-1 block text-xs font-semibold text-muted">{usedLabel}</span>}
        className={className}
      />
    );
  }

  // Available or in_progress: one offer, one destination — click starts it.
  return (
    <FreeSampleCard
      testId={testId}
      icon={icon}
      title={title}
      description={description}
      badgeLabel={badgeLabel}
      href={option.route}
      onClick={track}
      className={className}
    />
  );
}
