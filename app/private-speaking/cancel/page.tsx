'use client';

import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';

export default function PrivateSpeakingCancelPage() {
  const router = useRouter();

  return (
    <LearnerDashboardShell>
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center space-y-6">
      <div className="w-16 h-16 bg-danger/10 rounded-full flex items-center justify-center">
        <XCircle className="w-8 h-8 text-danger" />
      </div>
      <div>
        <h1 className="text-2xl font-bold text-navy mb-2">Payment Cancelled</h1>
        <p className="text-muted">
          Your booking was not completed. The reserved time slot has been released.
          You can try booking again at any time.
        </p>
      </div>
      <div className="flex flex-wrap gap-3">
        <Button onClick={() => router.push('/private-speaking')}>
          Try Again
        </Button>
        <Button variant="outline" onClick={() => router.push('/dashboard')}>
          Back to Dashboard
        </Button>
      </div>
      </div>
    </LearnerDashboardShell>
  );
}
