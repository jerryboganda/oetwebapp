'use client';

import { EmptyState } from '@/components/ui/empty-error';
import { Users } from 'lucide-react';

export default function LearnersIndexPage() {
  return (
    <div className="max-w-4xl mx-auto p-4 md:p-8" role="main" aria-label="Learners">
      <h1 className="text-2xl font-bold text-navy mb-2">Learners</h1>
      <p className="text-muted text-sm mb-8">
        Access learner profiles from the review queue by clicking on a learner&rsquo;s name.
      </p>
      <EmptyState
        icon={<Users className="w-12 h-12 text-muted" />}
        title="Select a Learner"
        description="Navigate to a learner profile from the Review Queue or use a direct link. Learner profiles are shown in the context of active reviews."
        action={{ label: 'Go to Review Queue', onClick: () => window.location.assign('/expert/queue') }}
      />
    </div>
  );
}
