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
        'flex items-start justify-between gap-4 rounded-2xl border px-4 py-3 text-left transition-colors',
        checked
          ? 'border-primary/25 bg-primary/5 text-navy'
          : 'border-gray-200 bg-white text-navy hover:border-gray-300',
      )}
    >
      <div className="space-y-1">
        <p className="text-sm font-semibold">{label}</p>
        {hint ? <p className="text-xs text-muted">{hint}</p> : null}
      </div>
      <span
        className={cn(
          'inline-flex min-w-14 items-center justify-center rounded-full px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.12em]',
          checked ? 'bg-primary text-white' : 'bg-gray-100 text-gray-500',
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
    <div className="space-y-4">
      {preferencesError ? <InlineAlert variant="error">{preferencesError}</InlineAlert> : null}
      {localError ? <InlineAlert variant="error">{localError}</InlineAlert> : null}
      {saveMessage ? <InlineAlert variant="success">{saveMessage}</InlineAlert> : null}

      <div className="grid gap-3 sm:grid-cols-2">
        <PreferenceToggle
          checked={draft.globalInAppEnabled}
          label="In-app notifications"
          hint="Controls the shared inbox and realtime badge."
          onToggle={() => updateDraft((current) => ({ ...current, globalInAppEnabled: !current.globalInAppEnabled }))}
        />
        <PreferenceToggle
          checked={draft.globalEmailEnabled}
          label="Email delivery"
          hint="Keeps transactional notification email active for this account."
          onToggle={() => updateDraft((current) => ({ ...current, globalEmailEnabled: !current.globalEmailEnabled }))}
        />
        <PreferenceToggle
          checked={draft.globalPushEnabled}
          label="Push delivery policy"
          hint="Lets the notification service fan out browser push for supported events."
          onToggle={() => updateDraft((current) => ({ ...current, globalPushEnabled: !current.globalPushEnabled }))}
        />
        <PreferenceToggle
          checked={draft.quietHoursEnabled}
          label="Quiet hours"
          hint="Reminder-style push respects these local quiet hours."
          onToggle={() => updateDraft((current) => ({ ...current, quietHoursEnabled: !current.quietHoursEnabled }))}
        />
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
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

      <div className="rounded-2xl border border-gray-200 bg-white p-4">
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

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold text-navy">Per-event delivery overrides</h3>
            <p className="text-xs text-muted">
              Overrides are stored per authenticated account and work across learner, expert, and admin shells.
            </p>
          </div>
          {!compact && draft.legacyLearnerSettings && Object.keys(draft.legacyLearnerSettings).length > 0 ? (
            <Badge variant="info">Legacy learner settings mirrored</Badge>
          ) : null}
        </div>

        <div className={cn('space-y-3 rounded-2xl border border-gray-200 bg-gray-50/60 p-3', compact && 'max-h-80 overflow-y-auto')}>
          {visibleEventEntries.map(([eventKey, eventPreference]) => (
            <div key={eventKey} className="rounded-2xl border border-gray-200 bg-white p-3">
              <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div className="space-y-2">
                  <p className="text-sm font-semibold text-navy">{formatEventLabel(eventKey)}</p>
                  <div className="flex flex-wrap gap-2">
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
                  </div>
                </div>

                <div className="min-w-48">
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
              </div>
            </div>
          ))}
        </div>

        {compact && eventEntries.length > visibleEventEntries.length ? (
          <p className="text-xs text-muted">
            Showing the first {visibleEventEntries.length} event overrides here. Open Settings -&gt; Notifications for the full shared matrix.
          </p>
        ) : null}
      </div>

      <div className="flex justify-end">
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
  const content = (
    <div className="space-y-4">
      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="info" className="gap-1">
            <Bell className="h-3.5 w-3.5" />
            Shared Account Preferences
          </Badge>
          <Badge variant="muted" className="gap-1">
            <Mail className="h-3.5 w-3.5" />
            Email
          </Badge>
          <Badge variant="muted" className="gap-1">
            <Volume2 className="h-3.5 w-3.5" />
            In-app
          </Badge>
          <Badge variant="muted" className="gap-1">
            <Smartphone className="h-3.5 w-3.5" />
            Push
          </Badge>
          <Badge variant="muted" className="gap-1">
            <MoonStar className="h-3.5 w-3.5" />
            Quiet Hours
          </Badge>
          <Badge variant="muted" className="gap-1">
            <Wifi className="h-3.5 w-3.5" />
            Web Push
          </Badge>
        </div>
        <h2 className="text-lg font-semibold text-navy">{title}</h2>
        <p className="text-sm text-muted">{description}</p>
      </div>
      <NotificationPreferencesInner compact={compact} />
    </div>
  );

  if (!showCard) {
    return <div className={className}>{content}</div>;
  }

  return (
    <Card className={className}>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent>{content}</CardContent>
    </Card>
  );
}
