'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { AlertTriangle, Ban, CalendarClock, Clock, Globe2, Plus, Sparkles, ShieldCheck, Trash2 } from 'lucide-react';
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
import {
  createScheduleException,
  deleteScheduleException,
  fetchExpertSchedule,
  fetchScheduleExceptions,
  isApiError,
  saveExpertSchedule,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ExpertSchedule, ExpertScheduleDay, ScheduleException } from '@/lib/types/expert';

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

  // Schedule exceptions state
  const [exceptions, setExceptions] = useState<ScheduleException[]>([]);
  const [showAddForm, setShowAddForm] = useState(false);
  const [newException, setNewException] = useState({ date: '', isBlocked: true, startTime: '09:00', endTime: '17:00', reason: '' });
  const [isAddingException, setIsAddingException] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        setErrorMessage(null);
        const [data, exceptionsData] = await Promise.all([fetchExpertSchedule(), fetchScheduleExceptions()]);
        if (cancelled) return;
        setSchedule(data);
        setExceptions(exceptionsData.exceptions);
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

  const handleAddException = useCallback(async () => {
    if (!newException.date) return;
    setIsAddingException(true);
    try {
      const created = await createScheduleException({
        date: newException.date,
        isBlocked: newException.isBlocked,
        startTime: newException.isBlocked ? undefined : newException.startTime,
        endTime: newException.isBlocked ? undefined : newException.endTime,
        reason: newException.reason || undefined,
      });
      setExceptions((prev) => [...prev, created].sort((a, b) => a.date.localeCompare(b.date)));
      setShowAddForm(false);
      setNewException({ date: '', isBlocked: true, startTime: '09:00', endTime: '17:00', reason: '' });
      setToast({ variant: 'success', message: 'Schedule exception added.' });
      analytics.track('expert_schedule_exception_created', { isBlocked: created.isBlocked });
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Failed to add exception.' });
    } finally {
      setIsAddingException(false);
    }
  }, [newException]);

  const handleDeleteException = useCallback(async (id: string) => {
    setDeletingId(id);
    try {
      await deleteScheduleException(id);
      setExceptions((prev) => prev.filter((e) => e.id !== id));
      setToast({ variant: 'success', message: 'Exception removed.' });
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Failed to remove exception.' });
    } finally {
      setDeletingId(null);
    }
  }, []);

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

          {/* ── Schedule Exceptions ─────────────────────── */}
          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Date Overrides"
              title="Schedule Exceptions"
              description="Block specific dates or set custom hours that override your weekly schedule."
            />
            <Card>
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border p-4">
                <h3 className="font-semibold text-navy">Upcoming Exceptions</h3>
                <Button variant="outline" size="sm" onClick={() => setShowAddForm(true)} aria-label="Add schedule exception">
                  <Plus className="mr-1 h-4 w-4" /> Add Exception
                </Button>
              </div>
              <CardContent className="space-y-4 p-6">
                {showAddForm ? (
                  <div className="rounded-xl border border-border bg-slate-50 p-4 space-y-3">
                    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                      <div>
                        <label className="mb-1 block text-sm font-medium text-navy">Date</label>
                        <input
                          type="date"
                          className="w-full rounded-lg border border-border px-3 py-2 text-sm"
                          value={newException.date}
                          onChange={(e) => setNewException({ ...newException, date: e.target.value })}
                          aria-label="Exception date"
                        />
                      </div>
                      <div>
                        <label className="mb-1 block text-sm font-medium text-navy">Type</label>
                        <Select
                          options={[
                            { value: 'blocked', label: 'Day Off (Blocked)' },
                            { value: 'custom', label: 'Custom Hours' },
                          ]}
                          value={newException.isBlocked ? 'blocked' : 'custom'}
                          onChange={(e) => setNewException({ ...newException, isBlocked: e.target.value === 'blocked' })}
                          aria-label="Exception type"
                        />
                      </div>
                    </div>
                    {!newException.isBlocked ? (
                      <div className="flex items-center gap-2">
                        <Select
                          options={TIME_OPTIONS}
                          value={newException.startTime}
                          onChange={(e) => setNewException({ ...newException, startTime: e.target.value })}
                          aria-label="Exception start time"
                        />
                        <span className="text-sm text-muted">to</span>
                        <Select
                          options={TIME_OPTIONS}
                          value={newException.endTime}
                          onChange={(e) => setNewException({ ...newException, endTime: e.target.value })}
                          aria-label="Exception end time"
                        />
                      </div>
                    ) : null}
                    <div>
                      <label className="mb-1 block text-sm font-medium text-navy">Reason (optional)</label>
                      <input
                        type="text"
                        className="w-full rounded-lg border border-border px-3 py-2 text-sm"
                        placeholder="e.g. Public holiday, Personal leave"
                        maxLength={500}
                        value={newException.reason}
                        onChange={(e) => setNewException({ ...newException, reason: e.target.value })}
                        aria-label="Exception reason"
                      />
                    </div>
                    <div className="flex justify-end gap-2">
                      <Button variant="ghost" size="sm" onClick={() => setShowAddForm(false)}>Cancel</Button>
                      <Button size="sm" onClick={handleAddException} disabled={isAddingException || !newException.date}>
                        {isAddingException ? 'Adding...' : 'Add Exception'}
                      </Button>
                    </div>
                  </div>
                ) : null}

                {exceptions.length === 0 && !showAddForm ? (
                  <p className="text-center text-sm text-muted py-6">No schedule exceptions. Your weekly schedule applies every week.</p>
                ) : null}

                {exceptions.length > 0 ? (
                  <div className="divide-y divide-border/60">
                    {exceptions.map((exception) => (
                      <div key={exception.id} className="flex items-center justify-between py-3">
                        <div className="flex items-center gap-3">
                          {exception.isBlocked ? (
                            <Ban className="h-5 w-5 text-red-500 shrink-0" />
                          ) : (
                            <Clock className="h-5 w-5 text-amber-500 shrink-0" />
                          )}
                          <div>
                            <p className="text-sm font-medium text-navy">
                              {new Date(exception.date + 'T00:00:00').toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}
                            </p>
                            <p className="text-xs text-muted">
                              {exception.isBlocked ? 'Day off' : `Custom: ${exception.startTime} – ${exception.endTime}`}
                              {exception.reason ? ` · ${exception.reason}` : ''}
                            </p>
                          </div>
                        </div>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleDeleteException(exception.id)}
                          disabled={deletingId === exception.id}
                          aria-label={`Remove exception for ${exception.date}`}
                        >
                          <Trash2 className="h-4 w-4 text-red-500" />
                        </Button>
                      </div>
                    ))}
                  </div>
                ) : null}
              </CardContent>
            </Card>
          </section>
        </div>
      </AsyncStateWrapper>
    </ExpertRouteWorkspace>
  );
}
