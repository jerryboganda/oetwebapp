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
      <div className="relative min-h-[calc(100dvh-4rem)]">
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-70" />
        <div className="space-y-12 pb-16 relative z-10 px-4 sm:px-0">
          
          <div className="bg-white/60 backdrop-blur-2xl p-6 sm:p-10 rounded-[2rem] shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-primary/5 hover:shadow-[0_8px_40px_rgb(0,0,0,0.08)] transition-all overflow-hidden relative group">
            <div className="absolute inset-0 bg-[linear-gradient(45deg,transparent,rgba(255,255,255,0.4),transparent)] -translate-x-[150%] group-hover:translate-x-[150%] transition-transform duration-1000 ease-in-out pointer-events-none" />
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
          </div>

          {error ? <InlineAlert variant="error" className="shadow-sm">{error}</InlineAlert> : null}
          {isFrozen ? (
            <InlineAlert variant="warning" className="shadow-sm">
              Your account is currently frozen, so settings are view-only until the freeze ends. You can still review your information, but updates are paused.
            </InlineAlert>
          ) : null}

          <div className="space-y-10">
            {settingsGroups.map((group, groupIndex) => (
              <MotionSection
                key={group.title}
                delayIndex={groupIndex}
                className="space-y-6"
              >
                <div className="flex items-center gap-3 px-2">
                  <div className="w-1 h-6 bg-primary rounded-full"></div>
                  <div>
                    <h2 className="text-sm font-black uppercase tracking-widest text-primary">{group.title}</h2>
                    <p className="text-xs text-navy/60 font-medium mt-1">Each setting is labeled by what it controls</p>
                  </div>
                </div>

                <div className="grid gap-4">
                  {group.items.map((item) => {
                    const Icon = item.icon;
                    const isDanger = item.id === 'danger-zone';
                    
                    return (
                      <div
                        key={item.id}
                        className={`group relative flex items-center justify-between p-5 transition-all duration-300 
                          rounded-2xl bg-white/70 backdrop-blur-xl ring-1 shadow-sm 
                          hover:shadow-md hover:-translate-y-0.5
                          ${isDanger ? 'ring-danger/20 hover:ring-danger/40 hover:bg-danger/5 shadow-danger/5' : 'ring-black/5 hover:ring-primary/20 hover:bg-white/90 shadow-black/5'}
                          ${item.type === 'link' ? (isFrozen ? 'cursor-not-allowed opacity-60' : 'cursor-pointer') : ''}
                        `}
                        onClick={item.type === 'link' ? () => handleOpen(item.id) : undefined}
                      >
                        <div className="flex flex-col sm:flex-row sm:items-center gap-4 sm:gap-5 pr-4 relative z-10 w-full">
                          <div className={`w-12 h-12 rounded-2xl flex items-center justify-center shrink-0 border transition-colors duration-300
                            ${isDanger ? 'bg-danger/10 border-danger/20 group-hover:bg-danger/20' : 'bg-primary/5 border-primary/10 group-hover:bg-primary/10 group-hover:border-primary/20'}`}>
                            <Icon className={`w-6 h-6 transition-transform duration-300 group-hover:scale-110 ${isDanger ? 'text-danger' : 'text-primary'}`} />
                          </div>
                          <div>
                            <h3 className={`text-lg font-black tracking-tight ${isDanger ? 'text-danger' : 'text-navy group-hover:text-primary'}`}>{item.title}</h3>
                            <p className={`text-sm font-medium mt-0.5 ${isDanger ? 'text-danger/70' : 'text-navy/60'}`}>{item.description}</p>
                          </div>
                        </div>

                        <div className="shrink-0 relative z-10 pl-2">
                          {item.type === 'link' ? (
                            <div className={`p-2 rounded-full transition-colors duration-300 ${isDanger ? 'bg-danger/5 group-hover:bg-danger/10' : 'bg-navy/5 group-hover:bg-primary/10'}`}>
                              <ChevronRight className={`w-5 h-5 ${isDanger ? 'text-danger' : 'text-navy/40 group-hover:text-primary'}`} />
                            </div>
                          ) : (
                            <button
                              onClick={() => handleToggle(item.id)}
                              disabled={savingId === item.id || isFrozen}
                              className={`relative inline-flex h-7 w-14 items-center rounded-full transition-all duration-300 focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 overflow-hidden shadow-inner ${toggles[item.id] ? 'bg-primary' : 'bg-navy/10'}`}
                              role="switch"
                              aria-checked={toggles[item.id]}
                              aria-label={`Toggle ${item.title}`}
                            >
                              <span className="sr-only">Toggle {item.title}</span>
                              <span
                                className={`inline-block h-5 w-5 transform rounded-full bg-white shadow-sm transition-transform duration-300 ease-spring ${toggles[item.id] ? 'translate-x-8' : 'translate-x-1'}`}
                              />
                            </button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </MotionSection>
            ))}
          </div>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
