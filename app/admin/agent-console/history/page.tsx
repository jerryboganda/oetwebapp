'use client';

import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { SessionHistory } from '@/components/admin/agent-console/HistoryTable';
import { useOwnerAgentConsole } from '@/components/admin/agent-console/OwnerAgentConsoleShell';

/**
 * Agent Console session history: every session (archived included by default)
 * with search, engine/status filters and paging. Opening a row replays the
 * full transcript on the session page (read-only when archived).
 */
export default function AgentConsoleHistoryPage() {
  const consoleState = useOwnerAgentConsole();

  return (
    <AdminOperationsLayout
      title="Agent Console History"
      eyebrow="Owner only"
      breadcrumbs={[{ label: 'Agent Console', href: '/admin/agent-console' }, { label: 'History' }]}
      description="Previous Claude Code and Codex sessions. Open one to replay its whole conversation."
      footer={<p className="text-xs text-admin-fg-muted">Sessions are kept for 90 days.</p>}
    >
      <SessionHistory enabled={consoleState.unlocked} />
    </AdminOperationsLayout>
  );
}
