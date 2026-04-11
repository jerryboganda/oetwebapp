'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CalendarClock, Globe2, Sparkles, ShieldCheck } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Checkbox, Select } from '@/components/ui/form-controls';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { fetchExpertSchedule, isApiError, saveExpertSchedule } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ExpertSchedule, ExpertScheduleDay } from '@/lib/types/expert';

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

  const activeDays = useMemo(() => schedule ? DAY_ORDER.filter((day) => schedule.days[day].active).length : 0, [schedule]);
  const timeWindow = useMemo(() => {
    if (!schedule) return 'Loading...';
    const firstActive = DAY_ORDER.find((day) => schedule.days[day].active);
    const lastActive = [...DAY_ORDER].reverse().find((day) => schedule.days[day].active);
    if (!firstActive || !lastActive) return 'No active days';
    return `${schedule.days[firstActive].start} - ${schedule.days[lastActive].end}`;
  }, [schedule]);

  return (
    <ExpertRouteWorkspace role="main" aria-label="Schedule Management">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AsyncStateWrapper status={pageStatus} onRetry={() => setReloadToken((current) => current + 1)} errorMessage={errorMessage ?? undefined}>
        <div className="space-y-6">
          <ExpertRouteHero
            eyebrow="Availability"
            icon={Sparkles}
            accent="primary"
            title="Schedule / Availability"
            description="Manage your regular review working hours and the timezone used for SLA calculations from a learner-style workspace."
            highlights={[
              { icon: Globe2, label: 'Timezone', value: schedule?.timezone ?? 'UTC' },
              { icon: CalendarClock, label: 'Active days', value: String(activeDays) },
              { icon: ShieldCheck, label: 'Working window', value: timeWindow },
            ]}
            aside={schedule?.lastUpdatedAt ? <span className="text-xs text-slate-500">Last updated: {new Date(schedule.lastUpdatedAt).toLocaleString()}</span> : undefined}
          />

          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <ExpertRouteSummaryCard
              label="Timezone"
              value={schedule?.timezone ?? 'UTC'}
              hint="Used for review SLAs and scheduling."
              accent="primary"
              icon={Globe2}
            />
            <ExpertRouteSummaryCard
              label="Active Days"
              value={activeDays}
              hint="Days currently open for review work."
              accent="navy"
              icon={CalendarClock}
            />
            <ExpertRouteSummaryCard
              label="Working Window"
              value={timeWindow}
              hint="Derived from the active day range."
              accent="emerald"
              icon={ShieldCheck}
            />
          </div>

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Weekly Availability"
              title="Schedule controls"
              description="Manage your regular review working hours and the timezone used for SLA calculations."
            />
            <Card>
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border p-4">
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
              <CardContent className="space-y-4 p-6">
                {schedule ? DAY_ORDER.map((day) => {
                  const scheduleDay = schedule.days[day];
                  const error = validationErrors[day];
                  return (
                    <div key={day}>
                        <div className="flex flex-col gap-4 border-b border-border/60 py-2 last:border-0 md:flex-row md:items-center">
                        <div className="w-32">
                          <Checkbox
                            label={day.charAt(0).toUpperCase() + day.slice(1)}
                            checked={scheduleDay.active}
                            onChange={(event) => updateDay(day, { active: event.target.checked })}
                            aria-label={`${day} availability toggle`}
                          />
                        </div>
                        <div className="flex flex-1 items-center gap-2">
                          <Select
                            options={TIME_OPTIONS}
                            value={scheduleDay.start}
                            disabled={!scheduleDay.active}
                            onChange={(event) => updateDay(day, { start: event.target.value })}
                            aria-label={`${day} start time`}
                            error={error ? ' ' : undefined}
                          />
                          <span className="text-sm text-muted">to</span>
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
                      {error ? <p className="ml-36 mt-1 text-xs text-red-500">{error}</p> : null}
                    </div>
                  );
                }) : null}

                {Object.keys(validationErrors).length > 0 ? (
                  <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
                    <div className="flex items-start gap-3">
                      <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
                      <p>Fix the highlighted day ranges before saving your availability.</p>
                    </div>
                  </div>
                ) : null}

                <div className="flex justify-end pt-2">
                  <Button onClick={handleSave} disabled={isSaving} aria-label="Save schedule">
                    {isSaving ? 'Saving...' : 'Save Schedule'}
                  </Button>
                </div>
              </CardContent>
            </Card>
          </section>
        </div>
      </AsyncStateWrapper>
    </ExpertRouteWorkspace>
  );
}
