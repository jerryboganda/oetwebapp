'use client';

// Shared entry point for the native realtime AI Speaking role-play. It is
// intentionally a route-only affordance so it cannot revive the old text
// ConversationHub self-practice path.
import { useCallback, useState } from 'react';
import { useRouter } from 'next/navigation';

import { Button } from '@/components/ui/button';

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
      router.push(`/speaking/roleplay/${encodeURIComponent(taskId)}`);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not open the live voice role-play.';
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
        <p role="alert" className="mt-2 text-sm text-danger">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export default SpeakingSelfPracticeButton;
