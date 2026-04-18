'use client';

import { useState, useEffect } from 'react';
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
import { LearnerDashboardShell } from '@/components/layout';
import { analytics } from '@/lib/analytics';
import { fetchFreezeStatus, fetchSettingsData, fetchUserProfile, updateSettingsSection } from '@/lib/api';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import { InlineAlert } from '@/components/ui/alert';
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
  const [toggles, setToggles] = useState<Record<string, boolean>>({
    'low-bandwidth': false,
  });
  const [profileName, setProfileName] = useState('');
  const [profileEmail, setProfileEmail] = useState('');
  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'settings' });
    let cancelled = false;

    (async () => {
      try {
        const [settings, profile, freeze] = await Promise.all([
          fetchSettingsData(),
          fetchUserProfile(),
          fetchFreezeStatus().catch(() => null),
        ]);
        if (cancelled) return;
        setToggles({
          'low-bandwidth': Boolean(settings.audio?.lowBandwidthMode ?? false),
        });
        setProfileName(profile.displayName);
        setProfileEmail(profile.email);
        setFreezeState(freeze as LearnerFreezeStatus | null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load settings.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const handleToggle = async (id: string) => {
    if (freezeState?.currentFreeze) {
      setError('Settings are read-only while your account is frozen.');
      return;
    }
    const nextValue = !toggles[id];
    setToggles((prev) => ({ ...prev, [id]: nextValue }));
    setSavingId(id);
    try {
      if (id === 'low-bandwidth') {
        await updateSettingsSection('audio', { lowBandwidthMode: nextValue });
      }
      analytics.track('content_view', { page: 'settings', setting: id, value: String(nextValue) });
    } catch (err) {
      setToggles((prev) => ({ ...prev, [id]: !nextValue }));
      setError(err instanceof Error ? err.message : 'Failed to save setting.');
    } finally {
      setSavingId(null);
    }
  };

  const handleOpen = (id: string) => {
    if (freezeState?.currentFreeze) {
      setError('Settings are read-only while your account is frozen.');
      return;
    }
    const target = id === 'goals' ? '/settings/goals' : `/settings/${id}`;
    router.push(target);
    analytics.track('content_view', { page: 'settings', setting: id, action: 'open' });
  };

  const isFrozen = Boolean(freezeState?.currentFreeze);

  return (
    <LearnerDashboardShell
      pageTitle="Settings"
      subtitle="Manage your account and app preferences"
      backHref="/"
    >
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Control Center"
          icon={SettingsIcon}
          accent="slate"
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
          <MotionSection
            key={group.title}
            delayIndex={groupIndex}
          >
            <LearnerSurfaceSectionHeader
              eyebrow="Settings Group"
              title={group.title}
              description="Each setting is labeled by what it controls and what the learner should expect after opening or toggling it."
              className="mb-3"
            />

            <div className={`bg-surface rounded-[24px] border shadow-sm overflow-hidden ${group.title === 'Danger Zone' ? 'border-red-200' : 'border-gray-200'}`}>
              <div className="divide-y divide-gray-100">
                {group.items.map((item) => {
                  const Icon = item.icon;
                  const isDanger = item.id === 'danger-zone';
                  return (
                    <div
                      key={item.id}
                      className={`flex items-center justify-between p-4 sm:p-5 transition-colors ${item.type === 'link' ? (isFrozen ? 'cursor-not-allowed opacity-60' : isDanger ? 'hover:bg-red-50 cursor-pointer' : 'hover:bg-gray-50 cursor-pointer') : ''}`}
                      onClick={item.type === 'link' ? () => handleOpen(item.id) : undefined}
                    >
                      <div className="flex items-center gap-4 pr-4">
                        <div className={`w-10 h-10 rounded-xl flex items-center justify-center shrink-0 border ${isDanger ? 'bg-red-50 border-red-200' : 'bg-gray-50 border-gray-100'}`}>
                          <Icon className={`w-5 h-5 ${isDanger ? 'text-red-600' : 'text-gray-600'}`} />
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <h3 className={`text-base font-bold ${isDanger ? 'text-red-700' : 'text-navy'}`}>{item.title}</h3>
                          </div>
                          <p className={`text-xs sm:text-sm mt-0.5 ${isDanger ? 'text-red-600/80' : 'text-muted'}`}>{item.description}</p>
                        </div>
                      </div>

                      <div className="shrink-0">
                        {item.type === 'link' ? (
                          <ChevronRight className="w-5 h-5 text-gray-400" />
                        ) : (
                          <button
                            onClick={() => handleToggle(item.id)}
                            disabled={savingId === item.id || isFrozen}
                            className={`relative inline-flex h-8 w-12 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-navy focus:ring-offset-2 ${toggles[item.id] ? 'bg-navy' : 'bg-gray-200'}`}
                            role="switch"
                            aria-checked={toggles[item.id]}
                            aria-label={`Toggle ${item.title}`}
                          >
                            <span className="sr-only">Toggle {item.title}</span>
                            <span
                              className={`inline-block h-5 w-5 transform rounded-full bg-white transition-transform ${toggles[item.id] ? 'translate-x-6' : 'translate-x-1'}`}
                            />
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          </MotionSection>
        ))}
      </div>
    </LearnerDashboardShell>
  );
}
