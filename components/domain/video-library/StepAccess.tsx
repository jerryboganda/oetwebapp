'use client';

/**
 * Video wizard — step 4: access & scheduling.
 * Free vs Premium tier, target professions (all or a subset), featured flag,
 * manual sort order and an optional scheduled publish time (must be in the
 * future).
 */

import { useCallback, useState } from 'react';
import { Checkbox, Input, RadioGroup } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import { PROFESSION_OPTIONS } from '@/lib/api/speaking-role-play-cards';
import {
  adminPatchVideo,
  type AdminVideoDetail,
  type VideoAccessTier,
} from '@/lib/api/video-library';

/** ISO string → value for a `datetime-local` input (local time, minute precision). */
function toDatetimeLocal(iso: string | null): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function StepAccess() {
  const wizard = useAdminWizard<AdminVideoDetail>();
  const video = wizard.entity;

  const [accessTier, setAccessTier] = useState<VideoAccessTier>(video.accessTier ?? 'free');
  const [allProfessions, setAllProfessions] = useState((video.targetProfessionIds ?? []).length === 0);
  const [professionIds, setProfessionIds] = useState<string[]>(video.targetProfessionIds ?? []);
  const [isFeatured, setIsFeatured] = useState(Boolean(video.isFeatured));
  const [sortOrder, setSortOrder] = useState(String(video.sortOrder ?? 0));
  const [publishAt, setPublishAt] = useState(toDatetimeLocal(video.publishAt));
  const [error, setError] = useState<string | null>(null);

  const canAdvance = accessTier === 'free' || accessTier === 'premium';

  const submit = useCallback(async () => {
    let publishAtIso: string | null = null;
    if (publishAt) {
      const scheduled = new Date(publishAt);
      if (Number.isNaN(scheduled.getTime())) {
        setError('The scheduled publish time is not a valid date.');
        throw new Error('invalid');
      }
      if (scheduled.getTime() <= Date.now()) {
        setError('The scheduled publish time must be in the future.');
        throw new Error('invalid');
      }
      publishAtIso = scheduled.toISOString();
    }
    setError(null);
    await adminPatchVideo(video.videoId, {
      accessTier,
      targetProfessionIds: allProfessions ? [] : professionIds,
      isFeatured,
      sortOrder: Number(sortOrder) || 0,
      publishAt: publishAtIso,
    });
    await wizard.refresh();
  }, [video.videoId, accessTier, allProfessions, professionIds, isFeatured, sortOrder, publishAt, wizard]);

  useStepRegistration('access', { canAdvance, submit });

  function toggleProfession(id: string) {
    setProfessionIds((prev) => (prev.includes(id) ? prev.filter((p) => p !== id) : [...prev, id]));
  }

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Access</h2>
        <p className="text-sm text-muted">Who can watch this video and where it surfaces.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <RadioGroup
        name="access-tier"
        label="Access tier"
        value={accessTier}
        onChange={(value) => setAccessTier(value as VideoAccessTier)}
        options={[
          { value: 'free', label: 'Free', description: 'Every signed-in learner can watch.' },
          { value: 'premium', label: 'Premium', description: 'Requires an active subscription/entitlement.' },
        ]}
      />

      <div className="space-y-2">
        <Checkbox
          label="Available to all professions"
          checked={allProfessions}
          onChange={(e) => setAllProfessions(e.target.checked)}
        />
        {!allProfessions ? (
          <div className="flex flex-wrap gap-2">
            {PROFESSION_OPTIONS.map((profession) => {
              const selected = professionIds.includes(profession.value);
              return (
                <Button
                  key={profession.value}
                  type="button"
                  size="sm"
                  variant={selected ? 'primary' : 'outline'}
                  onClick={() => toggleProfession(profession.value)}
                  aria-pressed={selected}
                >
                  {profession.label}
                </Button>
              );
            })}
          </div>
        ) : null}
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Checkbox
          label="Featured (pinned to the top of the learner library)"
          checked={isFeatured}
          onChange={(e) => setIsFeatured(e.target.checked)}
        />
        <Input
          label="Sort order"
          type="number"
          value={sortOrder}
          onChange={(e) => setSortOrder(e.target.value)}
          hint="Lower numbers surface first within a category."
        />
      </div>

      <Input
        label="Scheduled publish time (optional)"
        type="datetime-local"
        value={publishAt}
        onChange={(e) => setPublishAt(e.target.value)}
        hint="Leave empty to publish manually from the Review step. Must be in the future."
      />
    </div>
  );
}
