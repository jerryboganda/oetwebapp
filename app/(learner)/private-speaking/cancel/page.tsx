'use client';

import { useRouter } from 'next/navigation';
import { XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';

export default function PrivateSpeakingCancelPage() {
  const router = useRouter();

  return (
    <MotionSection>
      <Card padding="lg" className="flex flex-col items-center gap-6 py-10 text-center sm:py-12">
        <div className="flex h-16 w-16 items-center justify-center rounded-full bg-danger/10">
          <XCircle className="h-8 w-8 text-danger-strong" aria-hidden="true" />
        </div>
        <div>
          <h1 className="mb-2 text-2xl font-bold text-navy">Payment Cancelled</h1>
          <p className="max-w-prose text-muted">
            Your booking was not completed. The reserved time slot has been released.
            You can try booking again at any time.
          </p>
        </div>
        <div className="flex flex-wrap justify-center gap-3">
          <Button onClick={() => router.push('/private-speaking')}>
            Try Again
          </Button>
          <Button variant="outline" onClick={() => router.push('/dashboard')}>
            Back to Dashboard
          </Button>
        </div>
      </Card>
    </MotionSection>
  );
}
