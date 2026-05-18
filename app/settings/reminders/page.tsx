'use client';

import { useEffect } from 'react';
import { Bell, Clock } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerDashboardShell } from '@/components/layout';
import { NotificationPreferencesPanel } from '@/components/layout/notification-preferences-panel';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';

export default function SmartRemindersPage() {
  useEffect(() => {
    analytics.track('content_view', { page: 'smart-reminders' });
  }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Smart Study Reminders"
        description="Manage reminders through the shared notification service, not device-only local settings."
        icon={<Bell className="w-7 h-7" />}
      />

      <MotionSection>
        <Card className="p-6">
          <LearnerSurfaceSectionHeader
            icon={<Clock className="w-5 h-5" />}
            title="Production reminder controls"
            description="Reminder delivery, quiet hours, browser push, and per-event overrides are saved to your authenticated notification preferences."
          />
          <p className="mt-3 text-sm text-muted">
            Preferred study-time slots are not stored separately here until the backend exposes scheduled study reminder times. Use quiet hours and per-event delivery overrides below for live notification behavior.
          </p>
        </Card>
      </MotionSection>

      <MotionSection className="mt-6">
        <NotificationPreferencesPanel
          title="Reminder notification preferences"
          description="These controls save to /v1/notifications/preferences and affect study reminders, progress notifications, review updates, and other reminder-style events across supported channels."
        />
      </MotionSection>
    </LearnerDashboardShell>
  );
}
