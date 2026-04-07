'use client';

import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
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
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { analytics } from '@/lib/analytics';
import { fetchFreezeStatus, fetchSettingsData, fetchUserProfile, updateSettingsSection } from '@/lib/api';
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
    ],
  },
  {
    title: 'Accessibility & Performance',
    items: [
      { id: 'accessibility', title: 'Accessibility', description: 'Text size, contrast, and screen reader options', icon: Accessibility, type: 'link' },
      { id: 'low-bandwidth', title: 'Low-Bandwidth Mode', description: 'Reduce data usage for slower connections', icon: Wifi, type: 'toggle', defaultToggleState: false },
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
  const [freezeState, setFreezeState] = useState<any | null>(null);
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
        setFreezeState(freeze as any);
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
          <motion.section
            key={group.title}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: groupIndex * 0.08 }}
          >
            <LearnerSurfaceSectionHeader
              eyebrow="Settings Group"
              title={group.title}
              description="Each setting is labeled by what it controls and what the learner should expect after opening or toggling it."
              className="mb-3"
            />

            <div className="bg-surface rounded-[24px] border border-gray-200 shadow-sm overflow-hidden">
              <div className="divide-y divide-gray-100">
                {group.items.map((item) => {
                  const Icon = item.icon;
                  return (
                    <div
                      key={item.id}
                      className={`flex items-center justify-between p-4 sm:p-5 transition-colors ${item.type === 'link' ? (isFrozen ? 'cursor-not-allowed opacity-60' : 'hover:bg-gray-50 cursor-pointer') : ''}`}
                      onClick={item.type === 'link' ? () => handleOpen(item.id) : undefined}
                    >
                      <div className="flex items-center gap-4 pr-4">
                        <div className="w-10 h-10 rounded-xl bg-gray-50 flex items-center justify-center shrink-0 border border-gray-100">
                          <Icon className="w-5 h-5 text-gray-600" />
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <h3 className="text-base font-bold text-navy">{item.title}</h3>
                          </div>
                          <p className="text-xs sm:text-sm text-muted mt-0.5">{item.description}</p>
                        </div>
                      </div>

                      <div className="shrink-0">
                        {item.type === 'link' ? (
                          <ChevronRight className="w-5 h-5 text-gray-400" />
                        ) : (
                          <button
                            onClick={() => handleToggle(item.id)}
                            disabled={savingId === item.id || isFrozen}
                            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-navy focus:ring-offset-2 ${toggles[item.id] ? 'bg-navy' : 'bg-gray-200'}`}
                            role="switch"
                            aria-checked={toggles[item.id]}
                            aria-label={`Toggle ${item.title}`}
                          >
                            <span className="sr-only">Toggle {item.title}</span>
                            <span
                              className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${toggles[item.id] ? 'translate-x-6' : 'translate-x-1'}`}
                            />
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          </motion.section>
        ))}
      </div>
    </LearnerDashboardShell>
  );
}
