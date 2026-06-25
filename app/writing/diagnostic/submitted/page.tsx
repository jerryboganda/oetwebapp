'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { CheckCircle2, Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { LearnerPageHero } from '@/components/domain/learner-surface';

export default function WritingDiagnosticSubmittedPage() {
  const searchParams = useSearchParams();
  const evaluationId = searchParams?.get('id') ?? '';

  return (
    <LearnerDashboardShell pageTitle="Writing Diagnostic Submitted">
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow="Writing Diagnostic"
          icon={CheckCircle2}
          accent="amber"
          title="Diagnostic submitted"
          description="Your response is in the existing Writing evaluation queue. The pathway will use the completed result for readiness and next-step selection."
          highlights={[{ icon: Clock, label: 'State', value: 'Evaluating' }]}
        />
        <div className="flex flex-wrap gap-3 rounded-2xl border border-border bg-surface p-5 shadow-sm">
          {evaluationId ? <Button asChild><Link href={`/writing/result?id=${encodeURIComponent(evaluationId)}`}>Open result</Link></Button> : null}
          <Button asChild variant="outline"><Link href="/writing/diagnostic/results">Diagnostic results</Link></Button>
          <Button asChild variant="ghost"><Link href="/writing/today">Today plan</Link></Button>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}