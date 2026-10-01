'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslations } from 'next-intl';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import {
  fetchCompanionPreferences,
  saveCompanionPreferences,
  type CompanionExplanationDepth,
  type CompanionPreferences,
  type CompanionTeachingStyle,
} from '@/lib/api/companion';

const PREFERENCES_KEY = ['companion', 'preferences'] as const;

const STYLES: ReadonlyArray<{ value: CompanionTeachingStyle; labelKey: string; hintKey: string }> = [
  { value: 'Direct', labelKey: 'companion.style.direct', hintKey: 'companion.style.direct.hint' },
  { value: 'Socratic', labelKey: 'companion.style.socratic', hintKey: 'companion.style.socratic.hint' },
  { value: 'Coaching', labelKey: 'companion.style.coaching', hintKey: 'companion.style.coaching.hint' },
];

const DEPTHS: ReadonlyArray<{ value: CompanionExplanationDepth; labelKey: string }> = [
  { value: 'Brief', labelKey: 'companion.depth.brief' },
  { value: 'Standard', labelKey: 'companion.depth.standard' },
  { value: 'Deep', labelKey: 'companion.depth.deep' },
];

/**
 * How the learner wants to be taught: F-011 teaching style, F-050 Socratic
 * mode, F-052 coach mode, F-055 English-only immersion.
 *
 * The specification lists those as four features; from the learner's side they
 * are one question — "how should this thing talk to me?" — so they are one card.
 * Every control saves immediately: a settings panel with a Save button people
 * forget to press is a settings panel that does nothing.
 */
export function CompanionPreferencesPanel() {
  const t = useTranslations();
  const queryClient = useQueryClient();

  const preferences = useQuery({
    queryKey: PREFERENCES_KEY,
    queryFn: fetchCompanionPreferences,
    staleTime: 60_000,
  });

  const save = useMutation({
    mutationFn: saveCompanionPreferences,
    onSuccess: (saved) => queryClient.setQueryData(PREFERENCES_KEY, saved),
  });

  const current = preferences.data;

  const update = (patch: Partial<Omit<CompanionPreferences, 'updatedAt'>>) => {
    if (!current) return;
    save.mutate({
      teachingStyle: current.teachingStyle,
      depth: current.depth,
      englishOnly: current.englishOnly,
      preferWorkedExamples: current.preferWorkedExamples,
      ...patch,
    });
  };

  if (preferences.isLoading) {
    return (
      <Card padding="md">
        <Skeleton className="h-24 w-full" />
      </Card>
    );
  }

  if (!current) return null;

  return (
    <Card padding="md">
      <h2 className="text-sm font-semibold text-navy">{t('companion.style.title')}</h2>

      <fieldset className="mt-3" disabled={save.isPending}>
        <legend className="sr-only">{t('companion.style.title')}</legend>
        <ul className="space-y-1.5">
          {STYLES.map((style) => (
            <li key={style.value}>
              <label className="flex cursor-pointer items-start gap-2">
                <input
                  type="radio"
                  name="companion-teaching-style"
                  value={style.value}
                  checked={current.teachingStyle === style.value}
                  onChange={() => update({ teachingStyle: style.value })}
                  className="mt-0.5 h-4 w-4 shrink-0 accent-primary"
                />
                <span>
                  <span className="block text-xs font-semibold text-navy">{t(style.labelKey)}</span>
                  <span className="block text-xs text-muted">{t(style.hintKey)}</span>
                </span>
              </label>
            </li>
          ))}
        </ul>
      </fieldset>

      <fieldset className="mt-4" disabled={save.isPending}>
        <legend className="text-xs font-semibold text-navy">{t('companion.depth.title')}</legend>
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {DEPTHS.map((depth) => (
            <label key={depth.value} className="cursor-pointer">
              <input
                type="radio"
                name="companion-depth"
                value={depth.value}
                checked={current.depth === depth.value}
                onChange={() => update({ depth: depth.value })}
                className="peer sr-only"
              />
              <span className="inline-flex min-h-11 items-center rounded-full border border-border px-3 text-xs text-navy transition-colors peer-checked:border-primary peer-checked:bg-primary/10 peer-checked:font-semibold peer-checked:text-primary peer-focus-visible:ring-2 peer-focus-visible:ring-primary lg:min-h-7 lg:px-2.5">
                {t(depth.labelKey)}
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="mt-4 space-y-3">
        <label className="flex cursor-pointer items-start justify-between gap-3">
          <span>
            <span className="block text-xs font-semibold text-navy">{t('companion.style.englishOnly')}</span>
            <span className="block text-xs text-muted">{t('companion.style.englishOnly.hint')}</span>
          </span>
          <Switch
            size="sm"
            checked={current.englishOnly}
            disabled={save.isPending}
            onChange={() => update({ englishOnly: !current.englishOnly })}
            label={t('companion.style.englishOnly')}
          />
        </label>

        <label className="flex cursor-pointer items-start justify-between gap-3">
          <span className="text-xs font-semibold text-navy">{t('companion.style.workedExamples')}</span>
          <Switch
            size="sm"
            checked={current.preferWorkedExamples}
            disabled={save.isPending}
            onChange={() => update({ preferWorkedExamples: !current.preferWorkedExamples })}
            label={t('companion.style.workedExamples')}
          />
        </label>
      </div>
    </Card>
  );
}
