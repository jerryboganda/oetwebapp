'use client';

import Link from 'next/link';
import { LockKeyhole, Mail, Users } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';

export default function ListeningTeacherClassesPage() {
  return (
    <LearnerDashboardShell pageTitle="Listening Class Analytics" subtitle="Teacher class tools are held for a later launch gate." backHref="/listening">
      <div className="space-y-6 pb-24">
        <LearnerPageHero
          eyebrow="Launch hold"
          title="Teacher class analytics are not available to public learners"
          description="The first public launch is learner-focused OET practice. Roster management and class reporting remain hidden until role, privacy, and support workflows are complete."
          icon={Users}
          accent="blue"
          highlights={[
            { icon: LockKeyhole, label: 'Status', value: 'Closed gate' },
            { icon: Users, label: 'Audience', value: 'Teacher beta only' },
          ]}
        />
        <InlineAlert variant="info">
          If you are part of a teacher beta or institutional pilot, contact support for the correct access path.
        </InlineAlert>
        <Link className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white hover:bg-primary/90" href="/support">
          <Mail className="h-4 w-4" /> Contact support
        </Link>
      </div>
    </LearnerDashboardShell>
  );
}
