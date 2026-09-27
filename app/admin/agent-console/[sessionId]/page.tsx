'use client';

import { useParams } from 'next/navigation';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { SessionWorkspace } from '@/components/admin/agent-console/SessionWorkspace';
import { isOwnerAgentUserSessionId } from '@/lib/owner-agent/api';

export default function AgentConsoleSessionPage() {
  // useParams() can be null outside a dynamic segment; guard before use.
  const params = useParams<{ sessionId?: string | string[] }>();
  const raw = params?.sessionId;
  // Session ids are ULIDs (no characters that need decoding); anything else —
  // including the system approval queue's pseudo-session id — is rejected.
  const sessionId = typeof raw === 'string' ? raw : null;

  if (!sessionId || !isOwnerAgentUserSessionId(sessionId)) {
    return (
      <EmptyState
        title="Session not found"
        description="That is not a valid agent session id."
        primaryAction={{ label: 'Back to sessions', href: '/admin/agent-console' }}
      />
    );
  }

  // Keyed so switching sessions (e.g. after a hand-off) starts from a clean view.
  return <SessionWorkspace key={sessionId} sessionId={sessionId} />;
}
