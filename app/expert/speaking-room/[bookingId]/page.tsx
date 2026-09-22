'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Mic } from 'lucide-react';
import { ExpertRouteHero, ExpertRouteWorkspace } from '@/components/domain/expert-route-surface';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { createExpertMockSpeakingExam } from '@/lib/api';

export default function ExpertSpeakingLiveRoomRedirectPage() {
  const params = useParams<{ bookingId: string }>();
  const router = useRouter();
  const bookingId = params?.bookingId ?? '';
  const startedRef = useRef(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!bookingId || startedRef.current) return;
    startedRef.current = true;
    void createExpertMockSpeakingExam(bookingId)
      .then((exam) => router.replace(`/expert/speaking/exam/${encodeURIComponent(exam.examId)}`))
      .catch((reason: unknown) => {
        setError(reason instanceof Error ? reason.message : 'Could not open the live tutor room.');
      });
  }, [bookingId, router]);

  return (
    <ExpertRouteWorkspace>
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/expert/mocks/bookings')}>
          <ArrowLeft className="h-4 w-4" />
          Back to bookings
        </Button>
        <ExpertRouteHero
          eyebrow="Tutor · LiveKit"
          title="Opening live Speaking room"
          description="This booking now uses the canonical realtime LiveKit tutor room and tutor-only role-player view."
          icon={Mic}
          accent="navy"
        />
        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : (
          <InlineAlert variant="info">Preparing the tutor exam view and consent gate...</InlineAlert>
        )}
      </div>
    </ExpertRouteWorkspace>
  );
}
