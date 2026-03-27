'use client';

import { useState, useEffect } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Checkbox, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import type { ExpertSchedule, ExpertScheduleDay } from '@/lib/types/expert';
import { fetchExpertSchedule, isApiError, saveExpertSchedule } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success';

const DAY_ORDER = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const;

const TIME_OPTIONS = Array.from({ length: 24 }).map((_, index) => {
  const hour = index.toString().padStart(2, '0');
  return { value: `${hour}:00`, label: `${hour}:00` };
});

const TIMEZONE_OPTIONS = [
  { value: 'UTC', label: 'UTC' },
  { value: 'Europe/London', label: 'London (GMT/BST)' },
  { value: 'Europe/Paris', label: 'Paris (CET/CEST)' },
  { value: 'America/New_York', label: 'New York (ET)' },
  { value: 'America/Chicago', label: 'Chicago (CT)' },
  { value: 'America/Denver', label: 'Denver (MT)' },
  { value: 'America/Los_Angeles', label: 'Los Angeles (PT)' },
  { value: 'Asia/Dubai', label: 'Dubai (GST)' },
  { value: 'Asia/Kolkata', label: 'India (IST)' },
  { value: 'Asia/Singapore', label: 'Singapore (SGT)' },
  { value: 'Asia/Tokyo', label: 'Tokyo (JST)' },
  { value: 'Australia/Sydney', label: 'Sydney (AEST)' },
  { value: 'Pacific/Auckland', label: 'Auckland (NZST)' },
];

export default function SchedulePage() {
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [schedule, setSchedule] = useState<ExpertSchedule | null>(null);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});
  const [isSaving, setIsSaving] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        setErrorMessage(null);
        const data = await fetchExpertSchedule();
        if (cancelled) return;
        setSchedule(data);
        setPageStatus('success');
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load your schedule right now.');
          setPageStatus('error');
        }
      }
    })();
    return () => { cancelled = true; };
  }, [reloadToken]);

  const updateDay = (day: string, patch: Partial<ExpertScheduleDay>) => {
    if (!schedule) return;
    setSchedule({ ...schedule, days: { ...schedule.days, [day]: { ...schedule.days[day], ...patch } } });
    if (validationErrors[day]) {
      setValidationErrors((current) => {
        const next = { ...current };
        delete next[day];
        return next;
      });
    }
  };

  const validate = (): boolean => {
    if (!schedule) return false;
    const errors: Record<string, string> = {};
    DAY_ORDER.forEach((day) => {
      const scheduleDay = schedule.days[day];
      if (scheduleDay.active && scheduleDay.end <= scheduleDay.start) {
        errors[day] = 'End time must be after start time';
      }
    });
    setValidationErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSave = async () => {
    if (!schedule || !validate()) return;
    setIsSaving(true);
    try {
      const saved = await saveExpertSchedule(schedule);
      setSchedule(saved);
      setToast({ variant: 'success', message: 'Schedule saved successfully.' });
      analytics.track('expert_schedule_saved', {});
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Failed to save schedule.' });
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="max-w-4xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Schedule Management">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-2xl font-bold text-navy">Schedule / Availability</h1>
          <p className="text-muted text-sm mt-1">Manage your regular review working hours and the timezone used for SLA calculations.</p>
        </div>
        {schedule?.lastUpdatedAt && <p className="text-xs text-muted">Last updated: {new Date(schedule.lastUpdatedAt).toLocaleString()}</p>}
      </div>

      <AsyncStateWrapper status={pageStatus} onRetry={() => setReloadToken((current) => current + 1)} errorMessage={errorMessage ?? undefined}>
        <Card>
          <div className="p-4 border-b border-gray-200 flex items-center justify-between flex-wrap gap-3">
            <div>
              <h3 className="font-semibold text-navy">Weekly Availability</h3>
            </div>
            <div className="w-64">
              <Select
                label="Timezone"
                options={TIMEZONE_OPTIONS}
                value={schedule?.timezone ?? 'UTC'}
                onChange={(event) => schedule && setSchedule({ ...schedule, timezone: event.target.value })}
                aria-label="Select timezone"
              />
            </div>
          </div>
          <CardContent className="p-6 space-y-4">
            {schedule && DAY_ORDER.map((day) => {
              const scheduleDay = schedule.days[day];
              const error = validationErrors[day];
              return (
                <div key={day}>
                  <div className="flex items-center gap-4 py-2 border-b border-gray-100 last:border-0">
                    <div className="w-32">
                      <Checkbox
                        label={day.charAt(0).toUpperCase() + day.slice(1)}
                        checked={scheduleDay.active}
                        onChange={(event) => updateDay(day, { active: event.target.checked })}
                        aria-label={`${day} availability toggle`}
                      />
                    </div>
                    <div className="flex-1 flex items-center gap-2">
                      <Select
                        options={TIME_OPTIONS}
                        value={scheduleDay.start}
                        disabled={!scheduleDay.active}
                        onChange={(event) => updateDay(day, { start: event.target.value })}
                        aria-label={`${day} start time`}
                        error={error ? ' ' : undefined}
                      />
                      <span className="text-muted text-sm">to</span>
                      <Select
                        options={TIME_OPTIONS}
                        value={scheduleDay.end}
                        disabled={!scheduleDay.active}
                        onChange={(event) => updateDay(day, { end: event.target.value })}
                        aria-label={`${day} end time`}
                        error={error ? ' ' : undefined}
                      />
                    </div>
                  </div>
                  {error && <p className="text-xs text-red-500 ml-36 mt-1">{error}</p>}
                </div>
              );
            })}

            <div className="pt-6 flex justify-end">
              <Button onClick={handleSave} disabled={isSaving} aria-label="Save schedule">
                {isSaving ? 'Saving...' : 'Save Schedule'}
              </Button>
            </div>
          </CardContent>
        </Card>
      </AsyncStateWrapper>
    </div>
  );
}
