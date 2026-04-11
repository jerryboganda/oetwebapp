'use client';

import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';

export default function PrivateSpeakingCancelPage() {
  const router = useRouter();

  return (
    <LearnerDashboardShell>
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center space-y-6 px-4">
      <div className="w-16 h-16 bg-red-100 dark:bg-red-900/30 rounded-full flex items-center justify-center">
        <XCircle className="w-8 h-8 text-red-600 dark:text-red-400" />
      </div>
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">Payment Cancelled</h1>
        <p className="text-gray-500 dark:text-gray-400">
          Your booking was not completed. The reserved time slot has been released.
          You can try booking again at any time.
        </p>
      </div>
      <div className="flex gap-3">
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
