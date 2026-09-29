'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Mic } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { createMockSpeakingExam } from '@/lib/api';

export default function SpeakingLiveRoomRedirectPage() {
  const params = useParams<{ bookingId: string }>();
  const router = useRouter();
  const bookingId = params?.bookingId ?? '';
  const startedRef = useRef(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!bookingId || startedRef.current) return;
    startedRef.current = true;
    void createMockSpeakingExam(bookingId)
      .then((exam) => router.replace(`/speaking/exam/${encodeURIComponent(exam.examId)}`))
      .catch((reason: unknown) => {
        setError(reason instanceof Error ? reason.message : 'Could not open the live tutor room.');
      });
  }, [bookingId, router]);

  return (
    <>
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/mocks/bookings')}>
          <ArrowLeft className="h-4 w-4" />
          Back to bookings
        </Button>
        <LearnerPageHero
          title="Opening live Speaking room"
          description="This booking now uses the canonical realtime LiveKit tutor room."
          icon={Mic}
        />
        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : (
          <InlineAlert variant="info">Preparing the two-card Speaking exam and consent gate...</InlineAlert>
        )}
      </div>
    </>
  );
}
