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
                        role={item.type === 'link' ? 'button' : 'group'}
                        tabIndex={item.type === 'link' ? 0 : undefined}
                        onClick={item.type === 'link' ? () => handleOpen(item.id) : undefined}
                        className={`group relative flex items-center justify-between overflow-hidden rounded-[2rem] p-5 sm:p-8 transition-all duration-500 ${isDanger ? 'bg-danger/5 hover:bg-danger/10 ring-1 ring-danger/10 cursor-pointer' : 'bg-white/60 backdrop-blur-2xl hover:bg-white/90 shadow-[0_8px_30px_rgb(0,0,0,0.04)] hover:shadow-[0_8px_40px_rgb(0,0,0,0.12)] hover:-translate-y-1 ring-1 ring-black/5 cursor-pointer'} ${item.type === 'toggle' ? 'cursor-default hover:-translate-y-0 hover:shadow-sm' : ''} ${item.type === 'link' && isFrozen ? 'cursor-not-allowed opacity-60' : ''}`}
                      >
                        <div className="absolute inset-0 bg-gradient-to-br from-white/40 to-transparent pointer-events-none rounded-[2rem]" />
                        <div className="flex items-start gap-6 relative z-10 w-full pr-4">
                          <div className={`w-14 h-14 p-4 shrink-0 rounded-2xl flex items-center justify-center transition-all duration-500 relative ring-1 shadow-sm ${isDanger ? 'bg-danger/10 text-danger ring-danger/20' : 'bg-primary/5 text-primary group-hover:bg-primary/10 group-hover:scale-110 ring-primary/20'}`}>
                            <span className={`absolute inset-0 rounded-2xl transition-all duration-500 scale-100 opacity-0 ${isDanger ? 'bg-danger/20' : 'bg-primary/20'} group-hover:scale-[1.8] group-hover:opacity-100 blur-xl pointer-events-none`} />
                            <Icon className="w-7 h-7 drop-shadow-sm relative z-10" />
                          </div>
                          <div className="min-w-0">
                            <h3 className={`text-xl font-black tracking-tight ${isDanger ? 'text-danger' : 'text-navy group-hover:text-primary transition-colors'}`}>{item.title}</h3>
                            <p className={`text-sm font-medium mt-1.5 leading-relaxed ${isDanger ? 'text-danger/70' : 'text-navy/60'}`}>{item.description}</p>
                          </div>
                        </div>

                        <div className="shrink-0 relative z-10 flex">
                          {item.type === 'link' ? (
                            <div className={`w-12 h-12 flex items-center justify-center rounded-2xl transition-colors duration-500 ${isDanger ? 'bg-danger/10 text-danger group-hover:bg-danger/20' : 'bg-white/80 backdrop-blur-sm text-primary/40 group-hover:bg-primary/10 group-hover:text-primary shadow-sm ring-1 ring-black/5'}`}>
                              <ChevronRight className="w-5 h-5 transition-transform duration-300 group-hover:translate-x-1" />
                            </div>
                          ) : (
                            <button
                              onClick={() => handleToggle(item.id)}
                              disabled={savingId === item.id || isFrozen}
                              className={`relative inline-flex h-10 w-20 shrink-0 items-center rounded-full transition-all duration-500 focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-primary/20 focus-visible:ring-offset-2 overflow-hidden shadow-inner ring-1 ring-black/5 ${toggles[item.id] ? 'bg-primary' : 'bg-navy/10 hover:bg-navy/15'}`}
                              role="switch"
                              aria-checked={toggles[item.id]}
                              aria-label={`Toggle ${item.title}`}
                            >
                              <span className="sr-only">Toggle {item.title}</span>
                              <span
                                className={`inline-block h-8 w-8 transform rounded-full bg-white shadow-[0_2px_10px_rgba(0,0,0,0.1)] transition-transform duration-500 ease-spring ${toggles[item.id] ? 'translate-x-10' : 'translate-x-1'}`}
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
