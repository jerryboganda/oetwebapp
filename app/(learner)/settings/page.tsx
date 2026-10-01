'use client';

import { useContext, useState, useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  ChevronRight,
  User,
  Target,
  Bell,
  Shield,
  Accessibility,
  Wifi,
  Volume2,
  Calendar,
  Settings as SettingsIcon,
  Cpu,
  Trash2,
  MonitorSmartphone,
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { AuthContext } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { fetchFreezeStatus, fetchSettingsData, fetchUserProfile, updateSettingsSection } from '@/lib/api';
import { queryKeys } from '@/lib/query/hooks';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Switch } from '@/components/ui/switch';
import { cn } from '@/lib/utils';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';

type SettingType = 'link' | 'toggle';

interface SettingItem {
  id: string;
  title: string;
  description: string;
  icon: React.ElementType;
  type: SettingType;
  defaultToggleState?: boolean;
}

interface SettingGroup {
  title: string;
  items: SettingItem[];
}

const settingsGroups: SettingGroup[] = [
  {
    title: 'Account & Privacy',
    items: [
      { id: 'profile', title: 'Profile', description: 'Manage your personal information and credentials', icon: User, type: 'link' },
      { id: 'privacy', title: 'Privacy', description: 'Control your data, visibility, and security', icon: Shield, type: 'link' },
      { id: 'sessions', title: 'Active Sessions', description: 'View and manage devices signed into your account', icon: MonitorSmartphone, type: 'link' },
    ],
  },
  {
    title: 'Study Journey',
    items: [
      { id: 'goals', title: 'Goals', description: 'Set your target scores and milestones', icon: Target, type: 'link' },
      { id: 'study', title: 'Exam Date & Study Preferences', description: 'Update your test date, study schedule, and reminder cadence', icon: Calendar, type: 'link' },
    ],
  },
  {
    title: 'App Preferences',
    items: [
      { id: 'notifications', title: 'Notifications', description: 'Choose what alerts and emails you receive', icon: Bell, type: 'link' },
      { id: 'audio', title: 'Audio Preferences', description: 'Manage playback speed, volume, and transcripts', icon: Volume2, type: 'link' },
      { id: 'ai', title: 'AI', description: 'Connect your own OpenAI / Anthropic / OpenRouter key, or use platform credits', icon: Cpu, type: 'link' },
    ],
  },
  {
    title: 'Accessibility & Performance',
    items: [
      { id: 'accessibility', title: 'Accessibility', description: 'Text size, contrast, and screen reader options', icon: Accessibility, type: 'link' },
      { id: 'low-bandwidth', title: 'Low-Bandwidth Mode', description: 'Reduce data usage for slower connections', icon: Wifi, type: 'toggle', defaultToggleState: false },
    ],
  },
  {
    title: 'Danger Zone',
    items: [
      { id: 'danger-zone', title: 'Delete Account', description: 'Permanently delete your account and all associated data', icon: Trash2, type: 'link' },
    ],
  },
];

export default function Settings() {
  const router = useRouter();
  const authContext = useContext(AuthContext);
  const queryClient = useQueryClient();
  const queryUserId = authContext?.user?.userId ?? 'current';
  const queriesEnabled = authContext ? !authContext.loading && authContext.isAuthenticated : true;
  const settingsQuery = useQuery({
    queryKey: queryKeys.settings.home(queryUserId),
    queryFn: fetchSettingsData,
    staleTime: 60_000,
    enabled: queriesEnabled,
  });
  const profileQuery = useQuery({
    queryKey: queryKeys.profile.self(queryUserId),
    queryFn: fetchUserProfile,
    staleTime: 60_000,
    enabled: queriesEnabled,
  });
  const freezeQuery = useQuery({
    queryKey: queryKeys.settings.freeze(queryUserId),
    queryFn: fetchFreezeStatus,
    staleTime: 30_000,
    retry: false,
    enabled: queriesEnabled,
  });
  const [toggleOverride, setToggleOverride] = useState<{ userId: string; value: boolean } | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const settingsMutation = useMutation({
    mutationFn: ({ lowBandwidthMode }: { userId: string; lowBandwidthMode: boolean }) =>
      updateSettingsSection('audio', { lowBandwidthMode }),
    onSuccess: async (_, { userId, lowBandwidthMode }) => {
      queryClient.setQueryData(
        queryKeys.settings.home(userId),
        (current: Awaited<ReturnType<typeof fetchSettingsData>> | undefined) => current
          ? { ...current, audio: { ...current.audio, lowBandwidthMode } }
          : current,
      );
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.settings.home(userId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.settings.section(userId, 'audio') }),
      ]);
      setToggleOverride(null);
    },
  });
  const serverLowBandwidth = Boolean(settingsQuery.data?.audio?.lowBandwidthMode ?? false);
  const lowBandwidthEnabled = toggleOverride?.userId === queryUserId
    ? toggleOverride.value
    : serverLowBandwidth;
  const toggles: Record<string, boolean> = { 'low-bandwidth': lowBandwidthEnabled };
  const profileName = profileQuery.data?.displayName ?? '';
  const profileEmail = profileQuery.data?.email ?? '';
  const freezeState = (freezeQuery.data ?? null) as LearnerFreezeStatus | null;
  const loading = queriesEnabled && (settingsQuery.isPending || profileQuery.isPending || freezeQuery.isPending);
  const queryError = settingsQuery.error ?? profileQuery.error;
  const error = actionError ?? (queryError instanceof Error ? queryError.message : queryError ? 'Failed to load settings.' : null);

  useEffect(() => {
    analytics.track('content_view', { page: 'settings' });
  }, []);

  const handleToggle = async (id: string) => {
    if (freezeState?.currentFreeze) {
      setActionError('Settings are read-only while your account is frozen.');
      return;
    }
    const nextValue = !toggles[id];
    setToggleOverride({ userId: queryUserId, value: nextValue });
    setSavingId(id);
    setActionError(null);
    try {
      if (id === 'low-bandwidth') {
        await settingsMutation.mutateAsync({ userId: queryUserId, lowBandwidthMode: nextValue });
      }
      analytics.track('content_view', { page: 'settings', setting: id, value: String(nextValue) });
    } catch (err) {
      setToggleOverride({ userId: queryUserId, value: !nextValue });
      setActionError(err instanceof Error ? err.message : 'Failed to save setting.');
    } finally {
      setSavingId(null);
    }
  };

  const handleOpen = (id: string) => {
    if (freezeState?.currentFreeze) {
      setActionError('Settings are read-only while your account is frozen.');
      return;
    }
    const target = id === 'goals' ? '/settings/goals' : `/settings/${id}`;
    router.push(target);
    analytics.track('content_view', { page: 'settings', setting: id, action: 'open' });
  };

  const isFrozen = Boolean(freezeState?.currentFreeze);

  return (
    <>
      <LearnerPageHero
        eyebrow="Control Center"
        icon={SettingsIcon}
        accent="primary"
        title="Adjust account and study settings without hunting for them"
        description="Use this page to move quickly between identity, goals, app preferences, and accessibility controls with the impact of each change kept obvious."
        highlights={[
          { icon: User, label: 'Profile', value: loading ? 'Loading...' : profileName || 'Learner' },
          { icon: Bell, label: 'Account email', value: loading ? 'Loading...' : profileEmail || 'No email available' },
          { icon: Wifi, label: 'Low-bandwidth mode', value: toggles['low-bandwidth'] ? 'On' : 'Off' },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {isFrozen ? (
        <InlineAlert variant="warning">
          Your account is currently frozen, so settings are view-only until the freeze ends. You can still review your information, but updates are paused.
        </InlineAlert>
      ) : null}

      {settingsGroups.map((group, groupIndex) => (
        <MotionSection key={group.title} delayIndex={groupIndex} className="space-y-3">
          <LearnerSurfaceSectionHeader title={group.title} />
          <Card padding="none" className="divide-y divide-border overflow-hidden">
            {group.items.map((item) => {
              const Icon = item.icon;
              const isDanger = item.id === 'danger-zone';
              const isLink = item.type === 'link';

              return (
                <div
                  key={item.id}
                  role={isLink ? 'button' : 'group'}
                  tabIndex={isLink ? 0 : undefined}
                  onClick={isLink ? () => handleOpen(item.id) : undefined}
                  onKeyDown={isLink ? (event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      handleOpen(item.id);
                    }
                  } : undefined}
                  className={cn(
                    'group flex items-center gap-4 px-4 py-4 transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary sm:px-5',
                    isLink && (isDanger ? 'cursor-pointer hover:bg-danger/5' : 'cursor-pointer hover:bg-background-light'),
                    isLink && isFrozen && 'cursor-not-allowed opacity-60',
                  )}
                >
                  <div className={cn('flex h-11 w-11 shrink-0 items-center justify-center rounded-xl', isDanger ? 'bg-danger/10 text-danger' : 'bg-lavender text-primary')}>
                    <Icon className="h-5 w-5" aria-hidden="true" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <h3 className={cn('text-base font-semibold', isDanger ? 'text-danger' : 'text-navy')}>{item.title}</h3>
                    <p className={cn('mt-0.5 text-sm', isDanger ? 'text-danger/80' : 'text-muted')}>{item.description}</p>
                  </div>
                  {isLink ? (
                    <ChevronRight
                      className={cn(
                        'h-5 w-5 shrink-0 transition-transform duration-200 group-hover:translate-x-0.5 rtl:rotate-180 rtl:group-hover:-translate-x-0.5',
                        isDanger ? 'text-danger' : 'text-muted group-hover:text-primary',
                      )}
                      aria-hidden="true"
                    />
                  ) : (
                    <Switch
                      checked={Boolean(toggles[item.id])}
                      onChange={() => handleToggle(item.id)}
                      disabled={savingId === item.id || isFrozen}
                      label={`Toggle ${item.title}`}
                    />
                  )}
                </div>
              );
            })}
          </Card>
        </MotionSection>
      ))}
    </>
  );
}
