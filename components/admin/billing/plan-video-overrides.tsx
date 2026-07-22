'use client';

import { useEffect, useMemo, useState } from 'react';
import { fetchAllocatableVideos, type AllocatableVideo } from '@/lib/user-access';
import { VideoScopePicker } from '@/components/admin/user-access/video-scope-picker';

export interface PlanVideoOverridesValue {
  include: string[];
  exclude: string[];
}

export const EMPTY_PLAN_VIDEO_OVERRIDES: PlanVideoOverridesValue = { include: [], exclude: [] };

interface PlanVideoOverridesProps {
  value: PlanVideoOverridesValue;
  onChange: (next: PlanVideoOverridesValue) => void;
  disabled?: boolean;
}

/**
 * Per-plan video include/exclude picker — the friendly editor for the "videos" node of
 * BillingPlan.ContentOverridesJson (see VideoEntitlementService.Evaluate). An include wins over
 * the subtest/profession scope AND an exclude, but never over the Videos module toggle above.
 * Loads the same allocatable-video list and grouped Section → Language picker used by the
 * per-user Manage Access panel, so the two screens read as one system.
 */
export function PlanVideoOverrides({ value, onChange, disabled }: PlanVideoOverridesProps) {
  const [videos, setVideos] = useState<AllocatableVideo[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchAllocatableVideos()
      .then((result) => {
        if (cancelled) return;
        setVideos(result);
        setLoadError(null);
      })
      .catch((error) => {
        if (cancelled) return;
        console.error('Failed to load videos for plan overrides', error);
        setLoadError('Couldn’t load the video list. Any videos already selected below are kept, just not shown by title.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const overlapCount = useMemo(() => {
    const excludeSet = new Set(value.exclude);
    return value.include.filter((id) => excludeSet.has(id)).length;
  }, [value.include, value.exclude]);

  return (
    <div className="space-y-3">
      {loadError ? <p className="text-xs text-amber-600 dark:text-amber-400">{loadError}</p> : null}

      <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/5 p-3">
        <p className="mb-2 text-sm font-semibold text-navy">Always include</p>
        <p className="mb-2 text-xs text-muted">
          These videos unlock for every buyer of this plan, even if their profession or the subtests above wouldn’t
          normally show them.
        </p>
        <VideoScopePicker
          videos={videos}
          selectedIds={value.include}
          onChange={(include) => onChange({ ...value, include })}
          disabled={disabled || loading}
          emptyHint="No forced includes — this plan follows the subtest/profession scope as normal."
          selectedHint={(count) => `${count} video${count === 1 ? '' : 's'} force-included on this plan.`}
        />
      </div>

      <div className="rounded-xl border border-red-500/30 bg-red-500/5 p-3">
        <p className="mb-2 text-sm font-semibold text-navy">Always exclude</p>
        <p className="mb-2 text-xs text-muted">
          These videos stay locked for every buyer of this plan, even if the subtest/profession scope above would
          otherwise unlock them.
        </p>
        <VideoScopePicker
          videos={videos}
          selectedIds={value.exclude}
          onChange={(exclude) => onChange({ ...value, exclude })}
          disabled={disabled || loading}
          emptyHint="No forced excludes — this plan follows the subtest/profession scope as normal."
          selectedHint={(count) => `${count} video${count === 1 ? '' : 's'} force-excluded on this plan.`}
        />
      </div>

      {overlapCount > 0 ? (
        <p className="text-xs text-amber-600 dark:text-amber-400">
          {overlapCount} video{overlapCount === 1 ? '' : 's'} on both lists — include always wins, so it stays unlocked.
        </p>
      ) : null}
    </div>
  );
}
