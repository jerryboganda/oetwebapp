'use client';

import { useEffect, useState } from 'react';
import { Bell, Clock, Sun, Moon, Sunset, Volume2, VolumeX, Smartphone, Mail } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Toast } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

interface ReminderPreference {
  id: string;
  label: string;
  description: string;
  icon: React.ReactNode;
  enabled: boolean;
}

const TIME_SLOTS = [
  { id: 'morning', label: 'Morning', time: '08:00', icon: <Sun className="w-5 h-5" /> },
  { id: 'afternoon', label: 'Afternoon', time: '13:00', icon: <Sunset className="w-5 h-5" /> },
  { id: 'evening', label: 'Evening', time: '19:00', icon: <Moon className="w-5 h-5" /> },
];

const INITIAL_PREFS: ReminderPreference[] = [
  { id: 'daily_study', label: 'Daily Study Reminder', description: 'Get reminded to study at your preferred time.', icon: <Clock className="w-5 h-5" />, enabled: true },
  { id: 'streak_risk', label: 'Streak at Risk', description: 'Notification when your streak is about to break.', icon: <Bell className="w-5 h-5" />, enabled: true },
  { id: 'weekly_progress', label: 'Weekly Progress Report', description: 'Summary of your study activity and improvements.', icon: <Mail className="w-5 h-5" />, enabled: false },
  { id: 'goal_milestone', label: 'Goal Milestones', description: 'Celebrate when you reach study commitment goals.', icon: <Smartphone className="w-5 h-5" />, enabled: true },
  { id: 'exam_countdown', label: 'Exam Countdown', description: 'Reminders as your exam date approaches.', icon: <Clock className="w-5 h-5" />, enabled: false },
  { id: 'new_content', label: 'New Content Available', description: 'When new practice tasks are added for your profession.', icon: <Bell className="w-5 h-5" />, enabled: false },
];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function SmartRemindersPage() {
  const [prefs, setPrefs] = useState<ReminderPreference[]>(INITIAL_PREFS);
  const [preferredSlot, setPreferredSlot] = useState('morning');
  const [pushEnabled, setPushEnabled] = useState(true);
  const [emailEnabled, setEmailEnabled] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    analytics.track('content_view', { page: 'smart-reminders' });
  }, []);

  function togglePref(id: string) {
    setPrefs((prev) => prev.map((p) => (p.id === id ? { ...p, enabled: !p.enabled } : p)));
  }

  async function handleSave() {
    setIsSaving(true);
    // In production, this would call a backend API
    await new Promise((resolve) => setTimeout(resolve, 500));
    setToast({ variant: 'success', message: 'Reminder preferences saved!' });
    analytics.track('reminders_preferences_saved', {
      preferredSlot,
      pushEnabled,
      emailEnabled,
      enabledReminders: prefs.filter((p) => p.enabled).map((p) => p.id),
    });
    setIsSaving(false);
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} onDismiss={() => setToast(null)}>{toast.message}</Toast>}

      <LearnerPageHero
        title="Smart Study Reminders"
        subtitle="Customise when and how you receive study notifications."
        icon={<Bell className="w-7 h-7" />}
      />

      {/* Preferred Study Time */}
      <MotionSection>
        <Card className="p-6">
          <LearnerSurfaceSectionHeader
            icon={<Clock className="w-5 h-5" />}
            title="Preferred Study Time"
            subtitle="When should we remind you to study?"
          />
          <div className="mt-4 flex flex-wrap gap-3">
            {TIME_SLOTS.map((slot) => (
              <button
                key={slot.id}
                type="button"
                onClick={() => setPreferredSlot(slot.id)}
                className={`flex items-center gap-2 px-4 py-3 rounded-xl border transition-colors ${
                  preferredSlot === slot.id
                    ? 'bg-indigo-50 dark:bg-indigo-900/20 border-indigo-300 dark:border-indigo-700 text-indigo-700 dark:text-indigo-300'
                    : 'bg-white dark:bg-gray-900 border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:border-indigo-300'
                }`}
              >
                {slot.icon}
                <div className="text-left">
                  <p className="text-sm font-medium">{slot.label}</p>
                  <p className="text-xs opacity-70">{slot.time}</p>
                </div>
              </button>
            ))}
          </div>
        </Card>
      </MotionSection>

      {/* Notification Channels */}
      <MotionSection className="mt-6">
        <Card className="p-6">
          <LearnerSurfaceSectionHeader
            icon={<Volume2 className="w-5 h-5" />}
            title="Notification Channels"
          />
          <div className="mt-4 flex flex-wrap gap-4">
            <button
              type="button"
              onClick={() => setPushEnabled(!pushEnabled)}
              className={`flex items-center gap-2 px-4 py-2 rounded-lg border transition-colors ${
                pushEnabled
                  ? 'bg-green-50 dark:bg-green-900/20 border-green-300 dark:border-green-700 text-green-700 dark:text-green-300'
                  : 'bg-gray-50 dark:bg-gray-800 border-gray-200 dark:border-gray-700 text-gray-500'
              }`}
            >
              <Smartphone className="w-4 h-4" />
              <span className="text-sm font-medium">Push Notifications</span>
              {pushEnabled ? <Volume2 className="w-4 h-4" /> : <VolumeX className="w-4 h-4" />}
            </button>
            <button
              type="button"
              onClick={() => setEmailEnabled(!emailEnabled)}
              className={`flex items-center gap-2 px-4 py-2 rounded-lg border transition-colors ${
                emailEnabled
                  ? 'bg-green-50 dark:bg-green-900/20 border-green-300 dark:border-green-700 text-green-700 dark:text-green-300'
                  : 'bg-gray-50 dark:bg-gray-800 border-gray-200 dark:border-gray-700 text-gray-500'
              }`}
            >
              <Mail className="w-4 h-4" />
              <span className="text-sm font-medium">Email</span>
              {emailEnabled ? <Volume2 className="w-4 h-4" /> : <VolumeX className="w-4 h-4" />}
            </button>
          </div>
        </Card>
      </MotionSection>

      {/* Reminder Types */}
      <MotionSection className="mt-6">
        <LearnerSurfaceSectionHeader
          icon={<Bell className="w-5 h-5" />}
          title="Reminder Types"
          subtitle="Toggle the reminders you want to receive."
        />
        <div className="space-y-2 mt-3">
          {prefs.map((pref) => (
            <MotionItem key={pref.id}>
              <button
                type="button"
                onClick={() => togglePref(pref.id)}
                className={`w-full flex items-center gap-4 p-4 rounded-xl border transition-colors text-left ${
                  pref.enabled
                    ? 'bg-white dark:bg-gray-900 border-indigo-200 dark:border-indigo-800'
                    : 'bg-gray-50 dark:bg-gray-800/50 border-gray-200 dark:border-gray-700 opacity-70'
                }`}
              >
                <div className={pref.enabled ? 'text-indigo-600 dark:text-indigo-400' : 'text-gray-400'}>
                  {pref.icon}
                </div>
                <div className="flex-1">
                  <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{pref.label}</p>
                  <p className="text-xs text-gray-500 dark:text-gray-400">{pref.description}</p>
                </div>
                <div className={`w-10 h-6 rounded-full transition-colors ${pref.enabled ? 'bg-indigo-600' : 'bg-gray-300 dark:bg-gray-600'}`}>
                  <div className={`w-4 h-4 bg-white rounded-full mt-1 transition-transform ${pref.enabled ? 'translate-x-5' : 'translate-x-1'}`} />
                </div>
              </button>
            </MotionItem>
          ))}
        </div>
      </MotionSection>

      {/* Save Button */}
      <div className="mt-6 flex justify-end">
        <Button onClick={handleSave} disabled={isSaving} size="lg">
          {isSaving ? 'Saving…' : 'Save Preferences'}
        </Button>
      </div>
    </LearnerDashboardShell>
  );
}
