'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BarChart3, ClipboardCheck, Route } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { getWritingProfile, type LearnerWritingProfileDto } from '@/lib/writing-pathway-api';

export default function WritingDiagnosticResultsPage() {
  const [profile, setProfile] = useState<LearnerWritingProfileDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getWritingProfile().then(setProfile).catch(() => setError('Could not load diagnostic results.'));
  }, []);

  return (
    <LearnerDashboardShell pageTitle="Writing Diagnostic Results">
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow="Writing Diagnostic"
          icon={BarChart3}
          accent="amber"
          title="Your Writing baseline"
          description="The diagnostic result is the first Writing evaluation linked to your pathway readiness and daily practice sequence."
          highlights={[
            { icon: ClipboardCheck, label: 'Diagnostic', value: profile?.diagnosticCompleted ? 'Complete' : 'Pending' },
            { icon: Route, label: 'Stage', value: profile?.currentStage ?? 'Loading' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow="Result"
            title={profile?.lastDiagnosticEvaluationId ? 'Baseline evaluation ready' : 'No completed diagnostic yet'}
            description="Completed diagnostic evaluations open in the standard Writing result view."
            className="mb-5"
          />
          <div className="flex flex-wrap gap-3">
            {profile?.lastDiagnosticEvaluationId ? (
              <Button asChild><Link href={`/writing/result?id=${encodeURIComponent(profile.lastDiagnosticEvaluationId)}`}>Open Writing result</Link></Button>
            ) : (
              <Button asChild><Link href="/writing/diagnostic">Start diagnostic</Link></Button>
            )}
            <Button asChild variant="outline"><Link href="/writing/pathway">View pathway</Link></Button>
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}