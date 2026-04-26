'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';
import { Bell, Clock, Mail, Moon, Smartphone, Sun, Sunset, Volume2, VolumeX } from 'lucide-react';
import { useEffect, useState } from 'react';

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
      enabledReminders: prefs.filter((p: ReminderPreference) => p.enabled).map((p: ReminderPreference) => p.id).join(','),
    });
    setIsSaving(false);
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <LearnerPageHero
        title="Smart Study Reminders"
        description="Customise when and how you receive study notifications."
        icon={<Bell className="w-7 h-7" />}
      />

      {/* Preferred Study Time */}
      <MotionSection>
        <Card className="p-6">
          <LearnerSurfaceSectionHeader
            icon={<Clock className="w-5 h-5" />}
            title="Preferred Study Time"
            description="When should we remind you to study?"
          />
          <div className="mt-4 flex flex-wrap gap-3">
            {TIME_SLOTS.map((slot) => (
              <button
                key={slot.id}
                type="button"
                onClick={() => setPreferredSlot(slot.id)}
                className={`flex items-center gap-2 px-4 py-3 rounded-xl border transition-colors ${
                  preferredSlot === slot.id
                    ? 'bg-primary/10 border-primary/30 text-primary'
                    : 'bg-surface border-border text-navy hover:border-primary/30'
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
                  ? 'bg-success/10 border-success/30 text-success'
                  : 'bg-background-light border-border text-muted'
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
                  ? 'bg-success/10 border-success/30 text-success'
                  : 'bg-background-light border-border text-muted'
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
          description="Toggle the reminders you want to receive."
        />
        <div className="space-y-2 mt-3">
          {prefs.map((pref) => (
            <MotionItem key={pref.id}>
              <button
                type="button"
                onClick={() => togglePref(pref.id)}
                className={`w-full flex items-center gap-4 p-4 rounded-xl border transition-colors text-left ${
                  pref.enabled
                    ? 'bg-surface border-primary/30'
                    : 'bg-background-light border-border opacity-70'
                }`}
              >
                <div className={pref.enabled ? 'text-primary' : 'text-muted/60'}>
                  {pref.icon}
                </div>
                <div className="flex-1">
                  <p className="text-sm font-medium text-navy">{pref.label}</p>
                  <p className="text-xs text-muted">{pref.description}</p>
                </div>
                <div className={`w-10 h-6 rounded-full transition-colors ${pref.enabled ? 'bg-primary' : 'bg-border'}`}>
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
