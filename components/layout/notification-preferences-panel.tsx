'use client';

import { useEffect, useMemo, useState } from 'react';
import { Bell, Mail, MoonStar, Save, Smartphone, Volume2, Wifi } from 'lucide-react';
import { cloneNotificationPreferences, useNotificationCenter } from '@/contexts/notification-center-context';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input, Select } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import type { NotificationEmailMode, NotificationPreferencePayload } from '@/lib/types/notifications';

interface NotificationPreferencesPanelProps {
  compact?: boolean;
  className?: string;
  title?: string;
  description?: string;
  showCard?: boolean;
}

type ChannelFilter = 'all' | 'in_app' | 'email' | 'push' | 'quiet' | 'web_push';

const CHANNEL_TABS: { key: ChannelFilter; label: string; icon: typeof Bell }[] = [
  { key: 'all', label: 'All', icon: Bell },
  { key: 'in_app', label: 'In-app', icon: Volume2 },
  { key: 'email', label: 'Email', icon: Mail },
  { key: 'push', label: 'Push', icon: Smartphone },
  { key: 'quiet', label: 'Quiet hours', icon: MoonStar },
  { key: 'web_push', label: 'Web push', icon: Wifi },
];

function formatEventLabel(eventKey: string): string {
  return eventKey
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .trim();
}

function ensureEmailMode(value: NotificationEmailMode | null | undefined): NotificationEmailMode {
  return value ?? 'immediate';
}

function PreferenceToggle({
  checked,
  label,
  hint,
  onToggle,
}: {
  checked: boolean;
  label: string;
  hint?: string;
  onToggle: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      className={cn(
        'flex items-center justify-between gap-3 rounded-xl border px-3.5 py-2.5 text-left transition-colors',
        checked
          ? 'border-primary/25 bg-primary/5 text-navy'
          : 'border-border bg-surface text-navy hover:border-border-hover',
      )}
    >
      <div className="min-w-0 space-y-0.5">
        <p className="text-[13px] font-semibold leading-tight">{label}</p>
        {hint ? <p className="text-[11px] leading-snug text-muted">{hint}</p> : null}
      </div>
      <span
        className={cn(
          'inline-flex min-w-10 shrink-0 items-center justify-center rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.12em]',
          checked ? 'bg-primary text-white dark:bg-violet-700' : 'bg-background-light text-muted',
        )}
      >
        {checked ? 'On' : 'Off'}
      </span>
    </button>
  );
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

function NotificationPreferencesInner({ compact = false }: Pick<NotificationPreferencesPanelProps, 'compact'>) {
  const {
    preferences,
    isPreferencesLoading,
    preferencesError,
    isUpdatingPreferences,
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
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [filter, setFilter] = useState<ChannelFilter>('all');

  const isAll = filter === 'all';
  const showInApp = isAll || filter === 'in_app';
  const showEmail = isAll || filter === 'email';
  const showPush = isAll || filter === 'push';
  const showQuiet = isAll || filter === 'quiet';
  const showChannelsSection = filter !== 'web_push';
  const showQuietInputs = isAll || filter === 'quiet';
  const showBrowserPush = isAll || filter === 'push' || filter === 'web_push';
  const showEventMatrix = isAll || filter === 'in_app' || filter === 'email' || filter === 'push';

  useEffect(() => {
    setDraft(cloneNotificationPreferences(preferences));
  }, [preferences]);

  const eventEntries = useMemo(
    () => Object.entries(draft?.eventPreferences ?? {}).sort(([leftKey], [rightKey]) => leftKey.localeCompare(rightKey)),
    [draft?.eventPreferences],
  );
  const visibleEventEntries = compact ? eventEntries.slice(0, 8) : eventEntries;

  const updateDraft = (updater: (current: NotificationPreferencePayload) => NotificationPreferencePayload) => {
    setDraft((current) => (current ? updater(current) : current));
    setSaveMessage(null);
    setLocalError(null);
  };

  const handleSave = async () => {
    if (!draft) {
      return;
    }

    try {
      const response = await updatePreferences(buildSavePayload(draft));
      setDraft(cloneNotificationPreferences(response));
      setSaveMessage('Notification preferences saved.');
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : 'Unable to save notification preferences.');
    }
  };

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

  if (isPreferencesLoading) {
    return <p className="text-sm text-muted">Loading notification preferences...</p>;
  }

  if (!draft) {
    return <InlineAlert variant="warning">Notification preferences are not available for this account yet.</InlineAlert>;
  }

  return (
    <div className="space-y-3.5">
      {preferencesError ? <InlineAlert variant="error">{preferencesError}</InlineAlert> : null}
      {localError ? <InlineAlert variant="error">{localError}</InlineAlert> : null}
      {saveMessage ? <InlineAlert variant="success">{saveMessage}</InlineAlert> : null}

      <div
        role="tablist"
        aria-label="Filter notification controls by channel"
        className="-mx-1 flex flex-nowrap gap-1.5 overflow-x-auto px-1 pb-1"
      >
        {CHANNEL_TABS.map((tab) => {
          const TabIcon = tab.icon;
          const active = filter === tab.key;
          return (
            <button
              key={tab.key}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setFilter(tab.key)}
              className={cn(
                'inline-flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1.5 text-[12px] font-semibold transition-colors',
                active
                  ? 'bg-primary text-white shadow-sm shadow-primary/20 dark:bg-violet-700'
                  : 'bg-background-light text-muted hover:text-navy',
              )}
            >
              <TabIcon className="h-3.5 w-3.5" />
              {tab.label}
            </button>
          );
        })}
      </div>

      {showChannelsSection ? (
        <section className="space-y-2">
          <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">Delivery channels</h3>
          <div className="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
            {showInApp ? (
              <PreferenceToggle
                checked={draft.globalInAppEnabled}
                label="In-app notifications"
                hint="Shared inbox and realtime badge."
                onToggle={() => updateDraft((current) => ({ ...current, globalInAppEnabled: !current.globalInAppEnabled }))}
              />
            ) : null}
            {showEmail ? (
              <PreferenceToggle
                checked={draft.globalEmailEnabled}
                label="Email delivery"
                hint="Transactional notification email."
                onToggle={() => updateDraft((current) => ({ ...current, globalEmailEnabled: !current.globalEmailEnabled }))}
              />
            ) : null}
            {showPush ? (
              <PreferenceToggle
                checked={draft.globalPushEnabled}
                label="Push delivery policy"
                hint="Fan out browser push for supported events."
                onToggle={() => updateDraft((current) => ({ ...current, globalPushEnabled: !current.globalPushEnabled }))}
              />
            ) : null}
            {showQuiet ? (
              <PreferenceToggle
                checked={draft.quietHoursEnabled}
                label="Quiet hours"
                hint="Reminder push respects local quiet hours."
                onToggle={() => updateDraft((current) => ({ ...current, quietHoursEnabled: !current.quietHoursEnabled }))}
              />
            ) : null}
          </div>
        </section>
      ) : null}

      {showQuietInputs ? (
      <section className="space-y-2">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">Timezone &amp; quiet hours</h3>
        <div className="grid gap-2.5 sm:grid-cols-3">
          <Input
            label="Timezone"
            value={draft.timezone}
            onChange={(event) => updateDraft((current) => ({ ...current, timezone: event.target.value }))}
            placeholder="Asia/Karachi"
          />
          <Input
            label="Quiet hours start"
            type="time"
            value={draft.quietHoursStartLocalTime ?? ''}
            onChange={(event) => updateDraft((current) => ({ ...current, quietHoursStartLocalTime: event.target.value || null }))}
            disabled={!draft.quietHoursEnabled}
          />
          <Input
            label="Quiet hours end"
            type="time"
            value={draft.quietHoursEndLocalTime ?? ''}
            onChange={(event) => updateDraft((current) => ({ ...current, quietHoursEndLocalTime: event.target.value || null }))}
            disabled={!draft.quietHoursEnabled}
          />
        </div>
      </section>
      ) : null}

      {showBrowserPush ? (
      <div className="rounded-xl border border-border bg-surface p-3.5">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <Badge variant={pushEnabled ? 'success' : 'muted'}>
                {pushEnabled ? 'Browser Push Enabled' : 'Browser Push Disabled'}
              </Badge>
              <Badge variant={pushPermission === 'denied' ? 'danger' : pushPermission === 'granted' ? 'info' : 'muted'}>
                Permission: {pushPermission}
              </Badge>
            </div>
            <p className="text-sm text-muted">
              Prompting for browser push only happens from this preferences panel or the notification center.
            </p>
          </div>
          <Button
            type="button"
            variant={pushEnabled ? 'outline' : 'primary'}
            onClick={handlePushToggle}
            loading={isUpdatingPush}
            disabled={!pushSupported || !pushPublicKeyConfigured}
            className="gap-2"
          >
            <Smartphone className="h-4 w-4" />
            {pushEnabled ? 'Disable Push' : 'Enable Push'}
          </Button>
        </div>
        {!pushSupported ? (
          <p className="mt-3 text-xs text-muted">This browser does not support the Push API or service workers.</p>
        ) : null}
        {pushSupported && !pushPublicKeyConfigured ? (
          <p className="mt-3 text-xs text-muted">
            Push is available, but the public VAPID key is not configured in this environment yet.
          </p>
        ) : null}
      </div>
      ) : null}

      {showEventMatrix ? (
      <section className="space-y-2">
        <div className="flex items-center justify-between gap-2">
          <div>
            <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">Per-event delivery overrides</h3>
            <p className="text-[11px] leading-snug text-muted">
              Stored per account; applies across learner, expert, and admin shells.
            </p>
          </div>
          {!compact && draft.legacyLearnerSettings && Object.keys(draft.legacyLearnerSettings).length > 0 ? (
            <Badge variant="info">Legacy mirrored</Badge>
          ) : null}
        </div>

        <div className={cn('space-y-2 rounded-xl border border-border bg-background-light/60 p-2.5', compact && 'max-h-80 overflow-y-auto')}>
          {visibleEventEntries.map(([eventKey, eventPreference]) => (
            <div key={eventKey} className="rounded-lg border border-border bg-surface p-2.5">
              <div className="flex flex-col gap-2.5 lg:flex-row lg:items-center lg:justify-between">
                <div className="min-w-0 space-y-1.5">
                  <p className="text-[13px] font-semibold text-navy">{formatEventLabel(eventKey)}</p>
                  <div className="flex flex-wrap gap-1.5">
                    {showInApp ? (
                    <PreferenceToggle
                      checked={Boolean(eventPreference.inAppEnabled)}
                      label="In-app"
                      onToggle={() =>
                        updateDraft((current) => ({
                          ...current,
                          eventPreferences: {
                            ...current.eventPreferences,
                            [eventKey]: {
                              ...current.eventPreferences[eventKey],
                              inAppEnabled: !current.eventPreferences[eventKey].inAppEnabled,
                            },
                          },
                        }))
                      }
                    />
                    ) : null}
                    {showEmail ? (
                    <PreferenceToggle
                      checked={Boolean(eventPreference.emailEnabled)}
                      label="Email"
                      onToggle={() =>
                        updateDraft((current) => ({
                          ...current,
                          eventPreferences: {
                            ...current.eventPreferences,
                            [eventKey]: {
                              ...current.eventPreferences[eventKey],
                              emailEnabled: !current.eventPreferences[eventKey].emailEnabled,
                            },
                          },
                        }))
                      }
                    />
                    ) : null}
                    {showPush ? (
                    <PreferenceToggle
                      checked={Boolean(eventPreference.pushEnabled)}
                      label="Push"
                      onToggle={() =>
                        updateDraft((current) => ({
                          ...current,
                          eventPreferences: {
                            ...current.eventPreferences,
                            [eventKey]: {
                              ...current.eventPreferences[eventKey],
                              pushEnabled: !current.eventPreferences[eventKey].pushEnabled,
                            },
                          },
                        }))
                      }
                    />
                    ) : null}
                  </div>
                </div>

                {showEmail ? (
                <div className="shrink-0 lg:w-44">
                  <Select
                    label="Email mode"
                    value={ensureEmailMode(eventPreference.emailMode)}
                    onChange={(event) =>
                      updateDraft((current) => ({
                        ...current,
                        eventPreferences: {
                          ...current.eventPreferences,
                          [eventKey]: {
                            ...current.eventPreferences[eventKey],
                            emailMode: event.target.value as NotificationEmailMode,
                          },
                        },
                      }))
                    }
                    options={[
                      { value: 'off', label: 'Off' },
                      { value: 'immediate', label: 'Immediate' },
                      { value: 'daily_digest', label: 'Daily Digest' },
                    ]}
                  />
                </div>
                ) : null}
              </div>
            </div>
          ))}
        </div>

        {compact && eventEntries.length > visibleEventEntries.length ? (
          <p className="text-xs text-muted">
            Showing the first {visibleEventEntries.length} event overrides here. Open Settings -&gt; Notifications for the full shared matrix.
          </p>
        ) : null}
      </section>
      ) : null}

      <div className="flex justify-end border-t border-border pt-3.5">
        <Button type="button" onClick={handleSave} loading={isUpdatingPreferences} className="gap-2">
          <Save className="h-4 w-4" />
          Save Preferences
        </Button>
      </div>
    </div>
  );
}

export function NotificationPreferencesPanel({
  compact = false,
  className,
  title = 'Notification Preferences',
  description = 'Manage delivery channels, quiet hours, and browser push from one shared account-level panel.',
  showCard = true,
}: NotificationPreferencesPanelProps) {
  if (!showCard) {
    return (
      <div className={cn('space-y-3.5', className)}>
        <div className="space-y-1">
          <h2 className="text-lg font-semibold text-navy">{title}</h2>
          <p className="text-sm text-muted">{description}</p>
        </div>
        <NotificationPreferencesInner compact={compact} />
      </div>
    );
  }

  return (
    <Card className={className}>
      <CardHeader className="mb-4 space-y-1">
        <CardTitle>{title}</CardTitle>
        <p className="text-sm text-muted">{description}</p>
      </CardHeader>
      <CardContent>
        <NotificationPreferencesInner compact={compact} />
      </CardContent>
    </Card>
  );
}
