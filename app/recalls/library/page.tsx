'use client';

import { useEffect, useState } from 'react';
import { Library } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MasteryDashboard } from '@/components/domain/recalls/mastery-dashboard';
import { fetchRecallsToday, type RecallsTodayResponse } from '@/lib/api';
import { analytics } from '@/lib/analytics';

/**
 * /recalls/library — mastery dashboard with bucket and topic filters.
 */
export default function RecallsLibraryPage() {
  const [today, setToday] = useState<RecallsTodayResponse | null>(null);

  useEffect(() => {
    analytics.track('recalls_library_viewed');
    fetchRecallsToday().then(setToday).catch(() => undefined);
  }, []);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls / Library"
          title="Mastery & weak areas"
          description="Where you stand, what's slipping, and which clinical topics to revise next."
          icon={Library}
          highlights={[
            { icon: Library, label: 'Total', value: `${today?.total ?? 0}` },
            { icon: Library, label: 'Mastered', value: `${today?.mastered ?? 0}` },
            { icon: Library, label: 'Readiness', value: `${today?.readinessScore ?? 0}` },
          ]}
        />

        {today?.weakTopics && today.weakTopics.length > 0 && (
          <div className="rounded-2xl border border-warning/30 bg-warning/10 p-4 text-sm">
            <div className="font-semibold text-navy">Weak topics</div>
            <ul className="mt-2 grid grid-cols-1 gap-1 sm:grid-cols-2 lg:grid-cols-3">
              {today.weakTopics.map((t) => (
                <li key={t.topic} className="text-muted">
                  <span className="font-mono text-navy">{t.topic}</span> — {t.weakCount}/{t.total} weak
                </li>
              ))}
            </ul>
          </div>
        )}

        <LearnerSurfaceSectionHeader eyebrow="Library" title="All cards by bucket" description="Filter by status." />

        <MasteryDashboard />
      </div>
    </LearnerDashboardShell>
  );
}
