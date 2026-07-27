'use client';

/**
 * Video wizard — step 4: access & scheduling.
 * Free vs Premium tier, target professions (all or a subset), featured flag,
 * manual sort order and an optional scheduled publish time (must be in the
 * future).
 */

import { useCallback, useState } from 'react';
import { Checkbox, CheckboxGroup, Input, RadioGroup } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import { COURSE_PROFESSIONS } from '@/lib/course-content-matrix';
import {
  adminPatchVideo,
  type AdminVideoDetail,
  type VideoAccessTier,
  type VideoLanguage,
} from '@/lib/api/video-library';

type LanguageChoice = VideoLanguage | 'unset';

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
  const [language, setLanguage] = useState<LanguageChoice>(video.language ?? 'unset');
  const [arabicTargets, setArabicTargets] = useState<string[]>(
    video.targetProfessionIds.filter((id) => COURSE_PROFESSIONS.some((p) => p.id === id)),
  );
  const [isFeatured, setIsFeatured] = useState(Boolean(video.isFeatured));
  const [sortOrder, setSortOrder] = useState(String(video.sortOrder ?? 0));
  const [publishAt, setPublishAt] = useState(toDatetimeLocal(video.publishAt));
  const [error, setError] = useState<string | null>(null);

  const isArabicProfessionSpecific = language === 'ar' && (video.subtestCode === 'writing' || video.subtestCode === 'speaking');
  const canAdvance = (accessTier === 'free' || accessTier === 'premium') && language !== 'unset' && Boolean(video.subtestCode)
    && (!isArabicProfessionSpecific || arabicTargets.length > 0);

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
    if (language === 'unset' || !video.subtestCode) {
      setError('Choose a language and a subtest before saving access.');
      throw new Error('invalid');
    }
    if (isArabicProfessionSpecific && arabicTargets.length === 0) {
      setError('Select at least one profession for this Arabic Writing/Speaking video.');
      throw new Error('invalid');
    }
    const targetProfessionIds = isArabicProfessionSpecific ? arabicTargets : [];
    await adminPatchVideo(video.videoId, {
      accessTier,
      language,
      targetProfessionIds,
      isFeatured,
      sortOrder: Number(sortOrder) || 0,
      publishAt: publishAtIso,
    });
    await wizard.refresh();
  }, [video.videoId, video.subtestCode, accessTier, language, isArabicProfessionSpecific, arabicTargets, isFeatured, sortOrder, publishAt, wizard]);

  useStepRegistration('access', { canAdvance, submit });

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

      <RadioGroup
        name="video-language"
        label="Instruction language"
        value={language}
        onChange={(value) => setLanguage(value as LanguageChoice)}
        options={[
          { value: 'en', label: 'English', description: 'Shown under the English filter (shared across professions).' },
          { value: 'ar', label: 'Arabic', description: 'Shown under the Arabic filter.' },
          { value: 'unset', label: 'Unspecified', description: 'No language tag — appears regardless of the learner filter.' },
        ]}
      />

      {isArabicProfessionSpecific ? (
        <CheckboxGroup
          label="Professions"
          values={arabicTargets}
          onChange={setArabicTargets}
          error={arabicTargets.length === 0 ? 'Select at least one profession.' : undefined}
          options={COURSE_PROFESSIONS.map((p) => ({ value: p.id, label: p.label }))}
        />
      ) : (
        <InlineAlert variant="info">This language and subtest are shared automatically. Profession targets cannot drift.</InlineAlert>
      )}

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
