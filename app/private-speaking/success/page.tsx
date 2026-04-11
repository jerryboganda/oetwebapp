'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { CheckCircle2 } from 'lucide-react';
import { Button } from '@/components/ui/button';

export default function PrivateSpeakingSuccessPage() {
  const router = useRouter();
  const params = useSearchParams();
  const bookingId = params.get('booking_id');
  const [countdown, setCountdown] = useState(5);

  useEffect(() => {
    const timer = setInterval(() => {
      setCountdown(prev => {
        if (prev <= 1) {
          clearInterval(timer);
          router.push('/private-speaking');
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => clearInterval(timer);
  }, [router]);

  return (
    <LearnerDashboardShell>
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center space-y-6 px-4">
      <div className="w-16 h-16 bg-green-100 dark:bg-green-900/30 rounded-full flex items-center justify-center">
        <CheckCircle2 className="w-8 h-8 text-green-600 dark:text-green-400" />
      </div>
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">Payment Successful!</h1>
        <p className="text-gray-500 dark:text-gray-400">
          Your private speaking session has been booked. You&rsquo;ll receive a confirmation
          with Zoom details shortly.
        </p>
        {bookingId && (
          <p className="text-xs text-gray-400 mt-2">Booking ID: {bookingId}</p>
        )}
      </div>
      <Button onClick={() => router.push('/private-speaking')}>
        Back to Private Speaking ({countdown}s)
      </Button>
      </div>
    </LearnerDashboardShell>
  );
}
