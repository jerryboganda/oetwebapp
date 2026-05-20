'use client';

import { lazy, Suspense, useMemo, useState } from 'react';
import { useAuth } from '@/contexts/auth-context';
import { hasAiAssistantAccess, type AiAssistantCurrentUser } from '@/lib/ai-assistant/permissions';
import { AiAssistantLauncher } from './AiAssistantLauncher';

const AiAssistantPanel = lazy(async () => {
  const mod = await import('./AiAssistantPanel');
  return { default: mod.AiAssistantPanel };
});

export function AiAssistantWidget(): React.JSX.Element | null {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);

  const assistantUser = useMemo<AiAssistantCurrentUser | null>(() => {
    if (!user) return null;
    return {
      userId: user.userId,
      role: user.role,
      permissions: user.adminPermissions ?? [],
    };
  }, [user]);

  if (!hasAiAssistantAccess(assistantUser)) return null;

  return (
    <>
      <AiAssistantLauncher onOpen={() => setOpen(true)} />
      {open ? (
        <Suspense fallback={null}>
          <AiAssistantPanel onClose={() => setOpen(false)} />
        </Suspense>
      ) : null}
    </>
  );
}
