'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { LucideIcon } from 'lucide-react';
import { FreeSampleCard } from '@/components/domain/free-sample-card';
import { Button } from '@/components/ui/button';
import { RadioGroup } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { listFreeSamples, type FreeSampleOption, type FreeSampleSubtest } from '@/lib/api/free-samples';
import { WRITING_PROFESSION_LABELS, type WritingProfession } from '@/lib/writing/types';

// The Free Writing / Free Speaking entry (Free Mocks proposal, 2026-09). Shows
// the FREE SAMPLE card and, on click, asks the learner to choose a profession
// BEFORE the sample opens. The choice is per-open and never touches the
// learner's account profession. Renders nothing while loading, when the feature
// is off, or when no profession has live content yet.
//
// Import by direct path, not the '@/components/domain' barrel (hub tests mock it).

const normalize = (value?: string | null) => (value ?? '').trim().toLowerCase().replace(/_/g, '-');

function professionLabel(professionId: string): string {
  const known = WRITING_PROFESSION_LABELS[professionId as WritingProfession];
  if (known) return known;
  return professionId
    .split('-')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

export interface FreeSampleLauncherProps {
  subtest: FreeSampleSubtest;
  icon: LucideIcon;
  testId: string;
  title: string;
  description: string;
  badgeLabel?: string;
  modalTitle: string;
  modalDescription: string;
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
  modalTitle,
  modalDescription,
  startLabel,
  usedLabel,
  className = 'mb-4',
}: FreeSampleLauncherProps) {
  const router = useRouter();
  const { user } = useAuth();
  const [options, setOptions] = useState<FreeSampleOption[]>([]);
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState('');

  useEffect(() => {
    let cancelled = false;
    listFreeSamples(subtest)
      .then((rows) => {
        if (!cancelled) setOptions(Array.isArray(rows) ? rows : []);
      })
      .catch(() => {
        // The free sample is a bonus entry point: a failed lookup just hides it.
        if (!cancelled) setOptions([]);
      });
    return () => {
      cancelled = true;
    };
  }, [subtest]);

  if (options.length === 0) return null;

  const track = (row: FreeSampleOption) =>
    analytics.track('free_sample_click', { module: subtest, professionId: row.professionId });

  // Started already: continue that exact sample — no picker, no profession switch.
  const inProgress = options.find((option) => option.state === 'in_progress');
  if (inProgress) {
    return (
      <FreeSampleCard
        testId={testId}
        icon={icon}
        title={title}
        description={description}
        badgeLabel={badgeLabel}
        href={inProgress.route}
        onClick={() => track(inProgress)}
        className={className}
      />
    );
  }

  if (options.every((option) => option.state === 'used')) {
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

  const accountProfession = normalize(user?.activeProfessionId);
  const defaultProfession =
    options.find((option) => normalize(option.professionId) === accountProfession)?.professionId
    ?? options[0].professionId;
  const current = options.some((option) => option.professionId === selected) ? selected : defaultProfession;

  const start = () => {
    const row = options.find((option) => option.professionId === current);
    if (!row) return;
    track(row);
    setOpen(false);
    router.push(row.route);
  };

  return (
    <>
      <FreeSampleCard
        testId={testId}
        icon={icon}
        title={title}
        description={description}
        badgeLabel={badgeLabel}
        onClick={() => setOpen(true)}
        className={className}
      />
      <Modal open={open} onClose={() => setOpen(false)} title={modalTitle} size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">{modalDescription}</p>
          <RadioGroup
            name={`free-sample-${subtest}-profession`}
            options={options.map((option) => ({
              value: option.professionId,
              label: professionLabel(option.professionId),
            }))}
            value={current}
            onChange={setSelected}
          />
          <Button fullWidth size="lg" onClick={start} data-testid={`${testId}-start`}>
            {startLabel}
          </Button>
        </div>
      </Modal>
    </>
  );
}
