'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import ReadingPlayer, {
  type ReadingPassageDto,
  type ReadingQuestionDto,
} from '@/components/reading/ReadingPlayer';

interface StoredPracticeSession {
  questions: ReadingQuestionDto[];
  passages: ReadingPassageDto[];
  mode?: 'drill' | 'diagnostic' | 'mock' | 'review';
  focusSkill?: string;
  timeLimitSeconds?: number;
}

export default function PracticeSessionPage() {
  const params = useParams<{ sessionId: string }>();
  const router = useRouter();
  const sessionId = params?.sessionId ?? '';

  const [session, setSession] = useState<StoredPracticeSession | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [completing, setCompleting] = useState(false);

  useEffect(() => {
    if (!sessionId) {
      setError('No session ID provided.');
      return;
    }

    const key = `practice_session_${sessionId}`;
    const raw = sessionStorage.getItem(key);

    if (!raw) {
      setError('Session not found. Please start a new practice session.');
      return;
    }

    try {
      const parsed = JSON.parse(raw) as StoredPracticeSession;
      setSession(parsed);
    } catch {
      setError('Session data is corrupted. Please start a new practice session.');
    }
  }, [sessionId]);

  const handleComplete = async (answers: Record<string, string>) => {
    setCompleting(true);
    try {
      await fetch(`/v1/reading-pathway/sessions/${sessionId}/end`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ answers }),
      });
    } catch {
      // Best-effort: navigate regardless of API failure
    } finally {
      setCompleting(false);
      router.push('/reading');
    }
  };

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Practice">
        <div className="space-y-4">
          <InlineAlert variant="error">{error}</InlineAlert>
          <button
            type="button"
            onClick={() => router.push('/reading')}
            className="text-sm font-medium text-primary hover:underline"
          >
            ← Back to Reading
          </button>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!session) {
    return (
      <LearnerDashboardShell pageTitle="Practice">
        <div className="space-y-4">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-64 w-full" />
          <Skeleton className="h-96 w-full" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (completing) {
    return (
      <LearnerDashboardShell pageTitle="Practice">
        <div className="flex h-64 flex-col items-center justify-center gap-4">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary border-t-transparent" aria-hidden />
          <p className="text-sm text-muted-foreground">Saving your answers…</p>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Practice">
      <div className="flex h-[calc(100vh-8rem)] flex-col">
        <ReadingPlayer
          mode={session.mode ?? 'drill'}
          questions={session.questions}
          passages={session.passages}
          sessionId={sessionId}
          onComplete={(answers) => void handleComplete(answers)}
          focusSkill={session.focusSkill}
          timeLimitSeconds={session.timeLimitSeconds}
        />
      </div>
    </LearnerDashboardShell>
  );
}
