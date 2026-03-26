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
  Calendar
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout/app-shell';
import { analytics } from '@/lib/analytics';
import { fetchSettingsData, fetchUserProfile, updateSettingsSection } from '@/lib/api';
import { InlineAlert } from '@/components/ui/alert';

// --- Types & Data ---
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
    ]
  },
  {
    title: 'Study Journey',
    items: [
      { id: 'goals', title: 'Goals', description: 'Set your target scores and milestones', icon: Target, type: 'link' },
      { id: 'exam-date', title: 'Exam Date & Study Preferences', description: 'Update your test date and study schedule', icon: Calendar, type: 'link' },
    ]
  },
  {
    title: 'App Preferences',
    items: [
      { id: 'notifications', title: 'Notifications', description: 'Choose what alerts and emails you receive', icon: Bell, type: 'link' },
      { id: 'audio', title: 'Audio Preferences', description: 'Manage playback speed, volume, and transcripts', icon: Volume2, type: 'link' },
    ]
  },
  {
    title: 'Accessibility & Performance',
    items: [
      { id: 'accessibility', title: 'Accessibility', description: 'Text size, contrast, and screen reader options', icon: Accessibility, type: 'link' },
      { id: 'low-bandwidth', title: 'Low-Bandwidth Mode', description: 'Reduce data usage for slower connections', icon: Wifi, type: 'toggle', defaultToggleState: false },
    ]
  }
];

export default function Settings() {
  const router = useRouter();
  // State for toggleable settings (e.g., Low-bandwidth mode)
  const [toggles, setToggles] = useState<Record<string, boolean>>({
    'low-bandwidth': false
  });
  const [profileName, setProfileName] = useState('');
  const [profileEmail, setProfileEmail] = useState('');
  const [loading, setLoading] = useState(true);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Track page view
  useEffect(() => {
    analytics.track('content_view', { page: 'settings' });
    let cancelled = false;

    (async () => {
      try {
        const [settings, profile] = await Promise.all([fetchSettingsData(), fetchUserProfile()]);
        if (cancelled) return;
        setToggles({
          'low-bandwidth': Boolean(settings.audio?.lowBandwidthMode ?? false),
        });
        setProfileName(profile.displayName);
        setProfileEmail(profile.email);
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

    return () => { cancelled = true; };
  }, []);

  const handleToggle = async (id: string) => {
    const nextValue = !toggles[id];
    setToggles(prev => ({ ...prev, [id]: nextValue }));
    setSavingId(id);
    try {
      if (id === 'low-bandwidth') {
        await updateSettingsSection('audio', { lowBandwidthMode: nextValue });
      }
      analytics.track('content_view', { page: 'settings', setting: id, value: String(nextValue) });
    } catch (err) {
      setToggles(prev => ({ ...prev, [id]: !nextValue }));
      setError(err instanceof Error ? err.message : 'Failed to save setting.');
    } finally {
      setSavingId(null);
    }
  };

  const handleOpen = (id: string) => {
    if (id === 'goals' || id === 'exam-date') {
      router.push('/goals');
      return;
    }

    if (id === 'profile') {
      router.push('/goals');
      return;
    }

    analytics.track('content_view', { page: 'settings', setting: id, action: 'open' });
  };

  return (
    <AppShell
      pageTitle="Settings"
      subtitle="Manage your account and app preferences"
      backHref="/"
    >
      <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">
        <div className="bg-surface rounded-[24px] border border-gray-200 shadow-sm p-5 sm:p-6">
          <p className="text-xs font-black uppercase tracking-widest text-muted mb-2">Learner Profile</p>
          <h2 className="text-xl font-bold text-navy">{loading ? 'Loading profile...' : profileName || 'Learner'}</h2>
          <p className="text-sm text-muted mt-1">{loading ? 'Loading email...' : profileEmail || 'No email available'}</p>
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {settingsGroups.map((group, groupIndex) => (
          <motion.section 
            key={group.title}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: groupIndex * 0.1 }}
          >
            <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-3 px-2">
              {group.title}
            </h2>
            
            <div className="bg-surface rounded-[24px] border border-gray-200 shadow-sm overflow-hidden">
              <div className="divide-y divide-gray-100">
                {group.items.map((item) => {
                  const Icon = item.icon;
                  return (
                    <div 
                      key={item.id} 
                      className={`flex items-center justify-between p-4 sm:p-5 transition-colors ${item.type === 'link' ? 'hover:bg-gray-50 cursor-pointer' : ''}`}
                      onClick={item.type === 'link' ? () => handleOpen(item.id) : undefined}
                    >
                      <div className="flex items-center gap-4 pr-4">
                        <div className="w-10 h-10 rounded-xl bg-gray-50 flex items-center justify-center shrink-0 border border-gray-100">
                          <Icon className="w-5 h-5 text-gray-600" />
                        </div>
                        <div>
                          <h3 className="text-base font-bold text-navy">{item.title}</h3>
                          <p className="text-xs sm:text-sm text-muted mt-0.5">{item.description}</p>
                        </div>
                      </div>
                      
                      <div className="shrink-0">
                        {item.type === 'link' ? (
                          <ChevronRight className="w-5 h-5 text-gray-400" />
                        ) : (
                          <button 
                            onClick={() => handleToggle(item.id)}
                            disabled={savingId === item.id}
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
    </AppShell>
  );
}
