'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  Bell,
  BellRing,
  CalendarDays,
  CheckCircle2,
  ChevronRight,
  Clock,
  Globe,
  Info,
  Mail,
  MessageSquare,
  Monitor,
  MoonStar,
  Rocket,
  RotateCcw,
  Smartphone,
  SlidersHorizontal,
  SquarePen,
  Sun,
  Ticket,
  Volume2,
  Wifi,
  XCircle,
} from 'lucide-react';
import { cloneNotificationPreferences, useNotificationCenter } from '@/contexts/notification-center-context';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { NotificationSwitch } from '@/components/settings/notification-switch';
import { cn } from '@/lib/utils';
import type {
  NotificationEmailMode,
  NotificationEventPreferencePayload,
  NotificationPreferencePayload,
} from '@/lib/types/notifications';

type ChannelFilter = 'all' | 'in_app' | 'email' | 'push' | 'quiet' | 'web_push';
type SaveState = 'idle' | 'pending' | 'saving' | 'saved' | 'error';

const CHANNEL_TABS: { key: ChannelFilter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'in_app', label: 'In-app' },
  { key: 'email', label: 'Email' },
  { key: 'push', label: 'Push' },
  { key: 'quiet', label: 'Quiet hours' },
  { key: 'web_push', label: 'Web push' },
];

const TIMEZONES: { value: string; label: string }[] = [
  { value: 'UTC', label: '(UTC) Coordinated Universal Time' },
  { value: 'Europe/London', label: '(UTC+00:00) London' },
  { value: 'Europe/Berlin', label: '(UTC+01:00) Berlin, Paris, Madrid' },
  { value: 'Africa/Cairo', label: '(UTC+02:00) Cairo' },
  { value: 'Europe/Istanbul', label: '(UTC+03:00) Istanbul' },
  { value: 'Asia/Riyadh', label: '(UTC+03:00) Riyadh' },
  { value: 'Asia/Dubai', label: '(UTC+04:00) Dubai' },
  { value: 'Asia/Karachi', label: '(UTC+05:00) Karachi' },
  { value: 'Asia/Kolkata', label: '(UTC+05:30) Kolkata' },
  { value: 'Asia/Manila', label: '(UTC+08:00) Manila' },
  { value: 'Australia/Sydney', label: '(UTC+10:00) Sydney' },
  { value: 'America/New_York', label: '(UTC-05:00) New York' },
  { value: 'America/Chicago', label: '(UTC-06:00) Chicago' },
  { value: 'America/Los_Angeles', label: '(UTC-08:00) Los Angeles' },
];

const EMAIL_MODES: { value: NotificationEmailMode; label: string }[] = [
  { value: 'off', label: 'Off' },
  { value: 'immediate', label: 'Immediate' },
  { value: 'daily_digest', label: 'Daily digest' },
];

/** Keyword → icon so each event row reads at a glance, with a sane fallback. */
function iconForEvent(eventKey: string) {
  const key = eventKey.toLowerCase();
  if (key.includes('cancel')) return XCircle;
  if (key.includes('confirm') || key.includes('enrol')) return CheckCircle2;
  if (key.includes('feedback') || key.includes('comment')) return MessageSquare;
  if (key.includes('waitlist')) return BellRing;
  if (key.includes('coupon') || key.includes('discount')) return Ticket;
  if (key.includes('launch')) return Rocket;
  if (key.includes('expir')) return Clock;
  if (key.includes('low') || key.includes('warn')) return AlertTriangle;
  if (key.includes('reminder') || key.includes('daily')) return CalendarDays;
  if (key.includes('mail') || key.includes('email')) return Mail;
  return Bell;
}

function formatEventLabel(eventKey: string): string {
  return eventKey
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .trim();
}

function ensureEmailMode(value: NotificationEmailMode | null | undefined): NotificationEmailMode {
  return value ?? 'immediate';
}

function buildSavePayload(preferences: NotificationPreferencePayload) {
  return {
    timezone: preferences.timezone,
    globalInAppEnabled: preferences.globalInAppEnabled,
    globalEmailEnabled: preferences.globalEmailEnabled,
    globalPushEnabled: preferences.globalPushEnabled,
    quietHoursEnabled: preferences.quietHoursEnabled,
    quietHoursStartLocalTime: preferences.quietHoursStartLocalTime,
    quietHoursEndLocalTime: preferences.quietHoursEndLocalTime,
    eventPreferences: preferences.eventPreferences,
  };
}

/** An event counts as "configured" once it deviates from all-on + immediate. */
function isEventConfigured(preference: NotificationEventPreferencePayload): boolean {
  return (
    preference.inAppEnabled === false ||
    preference.emailEnabled === false ||
    preference.pushEnabled === false ||
    (preference.emailMode !== null && preference.emailMode !== 'immediate')
  );
}

function SelectShell({
  icon: Icon,
  children,
  className,
}: {
  icon?: typeof Globe;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('relative', className)}>
      {Icon ? (
        <Icon className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
      ) : null}
      {children}
    </div>
  );
}

const SELECT_CLASS =
  'w-full appearance-none rounded-xl border border-border bg-surface py-2.5 pr-9 text-[13px] font-medium text-navy shadow-sm transition-colors hover:border-border-hover focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20';

export function NotificationsSettingsView() {
  const {
    preferences,
    isPreferencesLoading,
    preferencesError,
    pushSupported,
    pushPublicKeyConfigured,
    pushPermission,
    pushEnabled,
    isUpdatingPush,
    updatePreferences,
    subscribeToPush,
    unsubscribeFromPush,
  } = useNotificationCenter();

  const [draft, setDraft] = useState<NotificationPreferencePayload | null>(null);
  const [filter, setFilter] = useState<ChannelFilter>('all');
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [localError, setLocalError] = useState<string | null>(null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const preferencesRef = useRef<HTMLDivElement | null>(null);
  const overridesRef = useRef<HTMLDivElement | null>(null);

  // Seed the draft once. We deliberately do NOT re-sync on every context change:
  // auto-save echoes the server payload back, and re-seeding mid-edit would
  // clobber a toggle the learner flipped while a save was still in flight.
  useEffect(() => {
    if (!preferences) return;
    setDraft((current) => current ?? cloneNotificationPreferences(preferences));
  }, [preferences]);

  useEffect(() => () => {
    if (saveTimer.current) clearTimeout(saveTimer.current);
  }, []);

  const scheduleSave = useCallback(
    (next: NotificationPreferencePayload) => {
      if (saveTimer.current) clearTimeout(saveTimer.current);
      setSaveState('pending');
      setLocalError(null);
      saveTimer.current = setTimeout(async () => {
        setSaveState('saving');
        try {
          await updatePreferences(buildSavePayload(next));
          setSaveState('saved');
        } catch (error) {
          setLocalError(error instanceof Error ? error.message : 'Unable to save notification preferences.');
          setSaveState('error');
        }
      }, 700);
    },
    [updatePreferences],
  );

  const mutate = useCallback(
    (updater: (current: NotificationPreferencePayload) => NotificationPreferencePayload) => {
      setDraft((current) => {
        if (!current) return current;
        const next = updater(current);
        scheduleSave(next);
        return next;
      });
    },
    [scheduleSave],
  );

  const mutateEvent = useCallback(
    (eventKey: string, patch: Partial<NotificationEventPreferencePayload>) => {
      mutate((current) => ({
        ...current,
        eventPreferences: {
          ...current.eventPreferences,
          [eventKey]: { ...current.eventPreferences[eventKey], ...patch },
        },
      }));
    },
    [mutate],
  );

  const restoreDefaults = useCallback(() => {
    mutate((current) => ({
      ...current,
      globalInAppEnabled: true,
      globalEmailEnabled: true,
      globalPushEnabled: true,
      quietHoursEnabled: false,
      quietHoursStartLocalTime: null,
      quietHoursEndLocalTime: null,
      eventPreferences: Object.fromEntries(
        Object.entries(current.eventPreferences).map(([key, value]) => [
          key,
          { ...value, inAppEnabled: true, emailEnabled: true, pushEnabled: true, emailMode: 'immediate' as const },
        ]),
      ),
    }));
  }, [mutate]);

  const eventEntries = useMemo(
    () => Object.entries(draft?.eventPreferences ?? {}).sort(([left], [right]) => left.localeCompare(right)),
    [draft?.eventPreferences],
  );

  const configuredCount = useMemo(
    () => eventEntries.filter(([, preference]) => isEventConfigured(preference)).length,
    [eventEntries],
  );

  const handlePushToggle = async () => {
    try {
      if (pushEnabled) {
        await unsubscribeFromPush();
      } else {
        await subscribeToPush();
      }
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : 'Unable to update browser push.');
    }
  };

  const isAll = filter === 'all';
  const showInApp = isAll || filter === 'in_app';
  const showEmail = isAll || filter === 'email';
  const showPush = isAll || filter === 'push';
  const showQuiet = isAll || filter === 'quiet';
  const showChannelRows = filter !== 'web_push';
  const showEventMatrix = isAll || filter === 'in_app' || filter === 'email' || filter === 'push';

  const saveStateLabel =
    saveState === 'saving' || saveState === 'pending'
      ? 'Saving…'
      : saveState === 'saved'
        ? 'All changes saved'
        : saveState === 'error'
          ? 'Save failed'
          : 'Ready to edit';

  const channelRows = [
    {
      key: 'in_app' as const,
      show: showInApp,
      icon: Bell,
      label: 'In-app notifications',
      hint: 'Show notifications within the OET platform.',
      checked: draft?.globalInAppEnabled ?? false,
      onChange: () => mutate((current) => ({ ...current, globalInAppEnabled: !current.globalInAppEnabled })),
    },
    {
      key: 'email' as const,
      show: showEmail,
      icon: Mail,
      label: 'Email delivery',
      hint: 'Receive updates and alerts via email.',
      checked: draft?.globalEmailEnabled ?? false,
      onChange: () => mutate((current) => ({ ...current, globalEmailEnabled: !current.globalEmailEnabled })),
    },
    {
      key: 'push' as const,
      show: showPush,
      icon: Smartphone,
      label: 'Push notifications',
      hint: 'Get notified on your mobile devices.',
      checked: draft?.globalPushEnabled ?? false,
      onChange: () => mutate((current) => ({ ...current, globalPushEnabled: !current.globalPushEnabled })),
    },
    {
      key: 'quiet' as const,
      show: showQuiet,
      icon: MoonStar,
      label: 'Quiet hours',
      hint: 'Pause non-urgent notifications during quiet hours.',
      checked: draft?.quietHoursEnabled ?? false,
      onChange: () => mutate((current) => ({ ...current, quietHoursEnabled: !current.quietHoursEnabled })),
    },
  ];

  const stats = [
    {
      icon: SlidersHorizontal,
      tone: 'bg-primary/10 text-primary',
      label: 'Controls',
      value: `${channelRows.length} settings`,
      onClick: () => preferencesRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
    },
    {
      icon: CheckCircle2,
      tone: 'bg-emerald-50 text-emerald-600 dark:bg-emerald-950/50 dark:text-emerald-400',
      label: 'Configured',
      value: `${configuredCount} set`,
      onClick: () => overridesRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
    },
    {
      icon: SquarePen,
      tone: 'bg-amber-50 text-amber-600 dark:bg-amber-950/50 dark:text-amber-400',
      label: 'Save state',
      value: saveStateLabel,
      onClick: null,
    },
  ];

  if (isPreferencesLoading && !draft) {
    return (
      <div className="space-y-4">
        <div className="h-24 animate-pulse rounded-2xl bg-background-light" />
        <div className="grid gap-4 sm:grid-cols-3">
          {[0, 1, 2].map((item) => <div key={item} className="h-24 animate-pulse rounded-2xl bg-background-light" />)}
        </div>
        <div className="h-96 animate-pulse rounded-2xl bg-background-light" />
      </div>
    );
  }

  if (!draft) {
    return (
      <InlineAlert variant="warning">
        Notification preferences are not available for this account yet.
      </InlineAlert>
    );
  }

  return (
    <div className="space-y-5">
      {/* ── Page header ───────────────────────────────────────────────── */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-navy sm:text-4xl">Notifications</h1>
          <p className="mt-1.5 text-sm text-muted">
            Manage how and when you receive updates across the OET platform.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={restoreDefaults} className="shrink-0 gap-2 self-start bg-surface text-primary">
          <RotateCcw className="h-4 w-4" />
          Restore defaults
        </Button>
      </div>

      {preferencesError ? <InlineAlert variant="error">{preferencesError}</InlineAlert> : null}
      {localError ? <InlineAlert variant="error">{localError}</InlineAlert> : null}

      {/* ── Summary tiles ─────────────────────────────────────────────── */}
      <div className="grid gap-4 sm:grid-cols-3">
        {stats.map((stat) => {
          const StatIcon = stat.icon;
          const interactive = Boolean(stat.onClick);
          const Tag = interactive ? 'button' : 'div';
          return (
            <Tag
              key={stat.label}
              {...(interactive ? { type: 'button' as const, onClick: stat.onClick ?? undefined } : {})}
              className={cn(
                'flex items-center gap-3.5 rounded-2xl border border-border bg-surface p-4 text-left shadow-sm',
                interactive && 'transition-colors hover:border-border-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40',
              )}
            >
              <span className={cn('flex h-11 w-11 shrink-0 items-center justify-center rounded-xl', stat.tone)}>
                <StatIcon className="h-5 w-5" aria-hidden="true" />
              </span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-[15px] font-semibold text-navy">{stat.label}</span>
                <span className="block truncate text-[13px] text-muted">{stat.value}</span>
              </span>
              {interactive ? <ChevronRight className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" /> : null}
            </Tag>
          );
        })}
      </div>

      {/* ── Main three-column workspace ───────────────────────────────── */}
      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,0.92fr)_minmax(0,1.5fr)]">
        {/* Column 1 — delivery channels */}
        <section ref={preferencesRef} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <h2 className="text-[17px] font-semibold text-navy">Notification Preferences</h2>
          <p className="mt-1 text-[13px] text-muted">Choose your preferred delivery channels for platform updates.</p>

          <div role="tablist" aria-label="Filter by channel" className="-mx-1 mt-4 flex flex-wrap gap-1.5 px-1">
            {CHANNEL_TABS.map((tab) => {
              const active = filter === tab.key;
              return (
                <button
                  key={tab.key}
                  type="button"
                  role="tab"
                  aria-selected={active}
                  onClick={() => setFilter(tab.key)}
                  className={cn(
                    'rounded-full px-3 py-1.5 text-[12.5px] font-semibold transition-colors',
                    active
                      ? 'bg-primary text-white shadow-sm shadow-primary/20 dark:bg-violet-700'
                      : 'bg-background-light text-muted hover:text-navy',
                  )}
                >
                  {tab.label}
                </button>
              );
            })}
          </div>

          {showChannelRows ? (
            <div className="mt-4 space-y-3">
              {channelRows.filter((row) => row.show).map((row) => {
                const RowIcon = row.icon;
                return (
                  <div
                    key={row.key}
                    className="flex items-center gap-3 rounded-xl border border-border bg-surface p-3.5 transition-colors hover:border-border-hover"
                  >
                    <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
                      <RowIcon className="h-[18px] w-[18px]" aria-hidden="true" />
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-[14px] font-semibold leading-tight text-navy">{row.label}</p>
                      <p className="mt-0.5 text-[12px] leading-snug text-muted">{row.hint}</p>
                    </div>
                    <NotificationSwitch checked={row.checked} onChange={row.onChange} label={row.label} />
                  </div>
                );
              })}
            </div>
          ) : (
            <p className="mt-4 rounded-xl bg-background-light px-3.5 py-3 text-[12.5px] text-muted">
              Web push is managed from the browser push card.
            </p>
          )}
        </section>

        {/* Column 2 — timezone, quiet hours, browser push */}
        <div className="space-y-4">
          <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <div className="flex items-center gap-2">
              <Clock className="h-[18px] w-[18px] text-primary" aria-hidden="true" />
              <h2 className="text-[17px] font-semibold text-navy">Timezone &amp; Quiet Hours</h2>
            </div>

            <label className="mt-4 block text-[12.5px] font-semibold text-navy" htmlFor="notification-timezone">
              Timezone
            </label>
            <SelectShell icon={Globe} className="mt-1.5">
              <select
                id="notification-timezone"
                value={draft.timezone}
                onChange={(event) => mutate((current) => ({ ...current, timezone: event.target.value }))}
                className={cn(SELECT_CLASS, 'pl-9')}
              >
                {TIMEZONES.some((zone) => zone.value === draft.timezone) ? null : (
                  <option value={draft.timezone}>{draft.timezone}</option>
                )}
                {TIMEZONES.map((zone) => (
                  <option key={zone.value} value={zone.value}>{zone.label}</option>
                ))}
              </select>
              <ChevronRight className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 rotate-90 text-muted" aria-hidden="true" />
            </SelectShell>

            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <div>
                <label className="block text-[12.5px] font-semibold text-navy" htmlFor="quiet-start">
                  Quiet hours start
                </label>
                <SelectShell icon={MoonStar} className="mt-1.5">
                  <input
                    id="quiet-start"
                    type="time"
                    value={draft.quietHoursStartLocalTime ?? ''}
                    disabled={!draft.quietHoursEnabled}
                    onChange={(event) =>
                      mutate((current) => ({ ...current, quietHoursStartLocalTime: event.target.value || null }))
                    }
                    className={cn(SELECT_CLASS, 'pl-9 pr-3 disabled:cursor-not-allowed disabled:opacity-60')}
                  />
                </SelectShell>
              </div>
              <div>
                <label className="block text-[12.5px] font-semibold text-navy" htmlFor="quiet-end">
                  Quiet hours end
                </label>
                <SelectShell icon={Sun} className="mt-1.5">
                  <input
                    id="quiet-end"
                    type="time"
                    value={draft.quietHoursEndLocalTime ?? ''}
                    disabled={!draft.quietHoursEnabled}
                    onChange={(event) =>
                      mutate((current) => ({ ...current, quietHoursEndLocalTime: event.target.value || null }))
                    }
                    className={cn(SELECT_CLASS, 'pl-9 pr-3 disabled:cursor-not-allowed disabled:opacity-60')}
                  />
                </SelectShell>
              </div>
            </div>
            {!draft.quietHoursEnabled ? (
              <p className="mt-2.5 text-[12px] text-muted">Turn on “Quiet hours” to set a window.</p>
            ) : null}
          </section>

          <section className="rounded-2xl border border-border bg-surface p-5 text-center shadow-sm">
            <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
              <BellRing className="h-6 w-6" aria-hidden="true" />
            </span>
            <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
              <span
                className={cn(
                  'inline-flex items-center rounded-full px-2.5 py-1 text-[11.5px] font-bold',
                  pushEnabled
                    ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-400'
                    : 'bg-rose-50 text-rose-600 dark:bg-rose-950/50 dark:text-rose-400',
                )}
              >
                {pushEnabled ? 'Browser push enabled' : 'Browser push disabled'}
              </span>
            </div>
            <p className="mt-1.5 text-[12.5px] text-muted">Permission: {pushPermission}</p>
            <p className="mt-2.5 text-[12.5px] leading-relaxed text-muted">
              You can enable browser push notifications by allowing permissions when prompted from the preferences
              panel or notification center.
            </p>
            <Button
              type="button"
              variant={pushEnabled ? 'outline' : 'primary'}
              onClick={handlePushToggle}
              loading={isUpdatingPush}
              disabled={!pushSupported || !pushPublicKeyConfigured}
              fullWidth
              className="mt-4 gap-2"
            >
              <Bell className="h-4 w-4" />
              {pushEnabled ? 'Disable push' : 'Enable Push'}
            </Button>
            {!pushSupported ? (
              <p className="mt-2 text-[11.5px] text-muted">This browser does not support the Push API.</p>
            ) : null}
            {pushSupported && !pushPublicKeyConfigured ? (
              <p className="mt-2 text-[11.5px] text-muted">Push is unavailable: no VAPID key configured.</p>
            ) : null}
          </section>
        </div>

        {/* Column 3 — per-event overrides */}
        <section ref={overridesRef} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <div className="flex items-center gap-2">
            <Bell className="h-[18px] w-[18px] text-primary" aria-hidden="true" />
            <h2 className="text-[17px] font-semibold text-navy">Per-event Delivery Overrides</h2>
            <span
              className="text-muted"
              title="Stored per account and applied across learner, expert, and admin shells."
            >
              <Info className="h-4 w-4" aria-hidden="true" />
            </span>
          </div>

          {showEventMatrix ? (
            <div className="mt-4 overflow-x-auto">
              <div className="min-w-[36rem]">
                <div className="grid grid-cols-[minmax(0,1fr)_3rem_3rem_3rem_8.5rem] items-center gap-2 border-b border-border pb-2.5">
                  <span className="text-[12px] font-semibold text-muted">Event</span>
                  <span className="flex items-center justify-center" title="In-app">
                    <Monitor className={cn('h-4 w-4', showInApp ? 'text-muted' : 'text-muted/30')} aria-label="In-app" />
                  </span>
                  <span className="flex items-center justify-center" title="Email">
                    <Mail className={cn('h-4 w-4', showEmail ? 'text-muted' : 'text-muted/30')} aria-label="Email" />
                  </span>
                  <span className="flex items-center justify-center" title="Push">
                    <Volume2 className={cn('h-4 w-4', showPush ? 'text-muted' : 'text-muted/30')} aria-label="Push" />
                  </span>
                  <span className="text-[12px] font-semibold text-muted">Email mode</span>
                </div>

                <div className="divide-y divide-border">
                  {eventEntries.map(([eventKey, eventPreference]) => {
                    const EventIcon = iconForEvent(eventKey);
                    const label = formatEventLabel(eventKey);
                    return (
                      <div
                        key={eventKey}
                        className="grid grid-cols-[minmax(0,1fr)_3rem_3rem_3rem_8.5rem] items-center gap-2 py-2.5"
                      >
                        <div className="flex min-w-0 items-center gap-2.5">
                          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-primary/[0.07] text-primary">
                            <EventIcon className="h-4 w-4" aria-hidden="true" />
                          </span>
                          <span className="truncate text-[13px] font-medium text-navy" title={label}>{label}</span>
                        </div>

                        <span className="flex justify-center">
                          {showInApp ? (
                            <NotificationSwitch
                              size="sm"
                              checked={Boolean(eventPreference.inAppEnabled)}
                              onChange={() => mutateEvent(eventKey, { inAppEnabled: !eventPreference.inAppEnabled })}
                              label={`${label} in-app`}
                            />
                          ) : <span className="text-muted/30">—</span>}
                        </span>
                        <span className="flex justify-center">
                          {showEmail ? (
                            <NotificationSwitch
                              size="sm"
                              checked={Boolean(eventPreference.emailEnabled)}
                              onChange={() => mutateEvent(eventKey, { emailEnabled: !eventPreference.emailEnabled })}
                              label={`${label} email`}
                            />
                          ) : <span className="text-muted/30">—</span>}
                        </span>
                        <span className="flex justify-center">
                          {showPush ? (
                            <NotificationSwitch
                              size="sm"
                              checked={Boolean(eventPreference.pushEnabled)}
                              onChange={() => mutateEvent(eventKey, { pushEnabled: !eventPreference.pushEnabled })}
                              label={`${label} push`}
                            />
                          ) : <span className="text-muted/30">—</span>}
                        </span>

                        <SelectShell>
                          <select
                            aria-label={`${label} email mode`}
                            value={ensureEmailMode(eventPreference.emailMode)}
                            onChange={(event) =>
                              mutateEvent(eventKey, { emailMode: event.target.value as NotificationEmailMode })
                            }
                            className={cn(SELECT_CLASS, 'py-1.5 pl-3')}
                          >
                            {EMAIL_MODES.map((mode) => (
                              <option key={mode.value} value={mode.value}>{mode.label}</option>
                            ))}
                          </select>
                          <ChevronRight className="pointer-events-none absolute right-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 rotate-90 text-muted" aria-hidden="true" />
                        </SelectShell>
                      </div>
                    );
                  })}
                </div>

                {eventEntries.length === 0 ? (
                  <p className="py-8 text-center text-[13px] text-muted">No event overrides available yet.</p>
                ) : null}
              </div>
            </div>
          ) : (
            <p className="mt-4 rounded-xl bg-background-light px-3.5 py-3 text-[12.5px] text-muted">
              Per-event overrides apply to in-app, email, and push. Pick one of those tabs to edit them.
            </p>
          )}
        </section>
      </div>

      <p className="flex items-center justify-center gap-1.5 pt-1 text-[12px] text-muted">
        <Wifi className="h-3.5 w-3.5" aria-hidden="true" />
        Changes save automatically.
      </p>
    </div>
  );
}
