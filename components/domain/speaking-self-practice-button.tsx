'use client';

// Wave 5 of docs/SPEAKING-MODULE-PLAN.md - shared "Practise this
// scenario with the AI patient" button. Used on both the speaking task
// page and the results page so a learner can deep-link straight into
// the Conversation module after attempting (or while preparing for) a
// speaking role-play. The handler delegates to the backend
// `/v1/speaking/tasks/{id}/self-practice` endpoint which itself routes
// through `ConversationService.CreateSessionAsync` (no new AI provider).
import { useCallback, useState } from 'react';
import { useRouter } from 'next/navigation';

import { Button } from '@/components/ui/button';
import { startSpeakingSelfPracticeSession } from '@/lib/api';

export interface SpeakingSelfPracticeButtonProps {
  taskId: string;
  label?: string;
  className?: string;
}

export function SpeakingSelfPracticeButton({
  taskId,
  label = 'Practise this scenario with the AI patient',
  className,
}: SpeakingSelfPracticeButtonProps) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onClick = useCallback(async () => {
    if (!taskId || busy) return;
    setBusy(true);
    setError(null);
    try {
      const result = await startSpeakingSelfPracticeSession(taskId);
      router.push(result.redirectPath);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not start AI patient session.';
      setError(message);
      setBusy(false);
    }
  }, [busy, router, taskId]);

  return (
    <div className={className}>
      <Button type="button" variant="primary" onClick={onClick} disabled={busy}>
        {busy ? 'Starting…' : label}
      </Button>
      {error ? (
        <p role="alert" className="mt-2 text-sm text-red-600">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export default SpeakingSelfPracticeButton;
