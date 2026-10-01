'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { CheckCircle2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';

export default function PrivateSpeakingSuccessPage() {
  const router = useRouter();
  const params = useSearchParams();
  const bookingId = params?.get('booking_id') ?? null;
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
    <MotionSection>
      <Card padding="lg" className="flex flex-col items-center gap-6 py-10 text-center sm:py-12">
        {/* One-shot pop: confirms the real payment, once. */}
        <div className="pop-in flex h-16 w-16 items-center justify-center rounded-full bg-success/10">
          <CheckCircle2 className="h-8 w-8 text-success-strong" aria-hidden="true" />
        </div>
        <div>
          <h1 className="mb-2 text-2xl font-bold text-navy">Payment Successful!</h1>
          <p className="max-w-prose text-muted">
            Your private speaking session has been booked. Open your dashboard to join the
            LiveKit tutor room when the session window opens.
          </p>
          {bookingId && (
            <p className="mt-2 break-all text-xs text-muted">Booking ID: {bookingId}</p>
          )}
        </div>
        <Button onClick={() => router.push('/private-speaking')}>
          Back to Private Speaking ({countdown}s)
        </Button>
      </Card>
    </MotionSection>
  );
}
