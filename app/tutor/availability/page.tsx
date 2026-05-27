'use client';

import { useEffect, useState } from 'react';
import { CalendarClock } from 'lucide-react';

import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { AvailabilityGrid } from '@/components/tutor/AvailabilityGrid';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchTutorAvailability,
  fetchTutorProfile,
  replaceTutorAvailability,
  type TutorAvailabilitySlot,
  type TutorAvailabilityUpsertPayload,
} from '@/lib/api';

const FALLBACK_TZ =
  typeof Intl !== 'undefined' ? Intl.DateTimeFormat().resolvedOptions().timeZone : 'UTC';

export default function TutorAvailabilityPage() {
  const [slots, setSlots] = useState<TutorAvailabilitySlot[]>([]);
  const [timeZone, setTimeZone] = useState(FALLBACK_TZ);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    Promise.all([fetchTutorAvailability(), fetchTutorProfile().catch(() => null)])
      .then(([list, profile]) => {
        if (cancelled) return;
        setSlots(list);
        if (profile?.timeZone) setTimeZone(profile.timeZone);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load availability.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleSave(payload: TutorAvailabilityUpsertPayload[]) {
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      const updated = await replaceTutorAvailability(payload);
      setSlots(updated);
      setSuccess('Availability saved.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save availability.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        title="Availability"
        description="Set the weekly hours you’re available to teach. Learners only see slots when matched against your published classes."
        icon={CalendarClock}
      />

      {error ? (
        <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
          <span>{error}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}
      {success ? (
        <InlineAlert variant="success" className="flex items-center justify-between gap-3">
          <span>{success}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setSuccess(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
        </div>
      ) : (
        <AvailabilityGrid slots={slots} timeZone={timeZone} saving={saving} onSave={handleSave} />
      )}
    </TutorRouteWorkspace>
  );
}
