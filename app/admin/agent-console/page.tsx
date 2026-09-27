'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Lock, Plus, Settings } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Button, buttonClassName } from '@/components/ui/button';
import { ConsoleStatusStrip } from '@/components/admin/agent-console/ConsoleStatusStrip';
import { NewSessionDialog } from '@/components/admin/agent-console/NewSessionDialog';
import { useOwnerAgentConsole } from '@/components/admin/agent-console/OwnerAgentConsoleShell';
import { SessionsList } from '@/components/admin/agent-console/SessionsList';
import { SystemApprovalQueue } from '@/components/admin/agent-console/SystemApprovalQueue';

export default function AgentConsolePage() {
  const router = useRouter();
  const consoleState = useOwnerAgentConsole();
  const [creating, setCreating] = useState(false);
  const engines = consoleState.status?.engines ?? null;
  const blocked = Boolean(consoleState.status?.killed || consoleState.status?.draining);

  return (
    <AdminOperationsLayout
      title="Agent Console"
      eyebrow="Owner only"
      description="Claude Code and Codex working on the production project under your subscriptions. Every action is audited."
      actions={
        <div className="flex flex-wrap gap-2">
          <Button variant="primary" size="sm" onClick={() => setCreating(true)} disabled={!engines || blocked}>
            <Plus className="h-4 w-4" aria-hidden="true" /> New session
          </Button>
          <Link href="/admin/agent-console/settings" className={buttonClassName({ variant: 'outline', size: 'sm' })}>
            <Settings className="h-4 w-4" aria-hidden="true" /> Settings
          </Link>
          <Button variant="ghost" size="sm" onClick={() => void consoleState.lockConsole()}>
            <Lock className="h-4 w-4" aria-hidden="true" /> Lock
          </Button>
        </div>
      }
      kpis={
        <ConsoleStatusStrip
          status={consoleState.status}
          statusError={consoleState.statusError}
          leaseExpiresAt={consoleState.leaseExpiresAt}
          leaseError={consoleState.leaseError}
          onKillSwitch={consoleState.killSwitch}
          onApplyUpdate={consoleState.applyUpdate}
          onResume={consoleState.resume}
        />
      }
    >
      <div className="space-y-6">
        <SystemApprovalQueue enabled={consoleState.unlocked} statusApprovals={consoleState.status?.systemApprovals ?? null} />

        <section aria-labelledby="owner-agent-sessions" className="space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="owner-agent-sessions" className="text-sm font-semibold text-admin-fg-strong">
              Sessions
            </h2>
            <label className="inline-flex items-center gap-2 text-xs text-admin-fg-muted">
              <input
                type="checkbox"
                checked={consoleState.includeArchived}
                onChange={(event) => consoleState.setIncludeArchived(event.target.checked)}
              />
              Show archived
            </label>
          </div>
          <SessionsList
            sessions={consoleState.sessions}
            loading={consoleState.sessionsState === 'loading'}
            error={consoleState.sessionsError}
            onCreate={engines && !blocked ? () => setCreating(true) : undefined}
          />
        </section>
      </div>

      <NewSessionDialog
        open={creating}
        engines={engines}
        onClose={() => setCreating(false)}
        onCreate={consoleState.createSession}
        onCreated={(session) => {
          setCreating(false);
          router.push(`/admin/agent-console/${encodeURIComponent(session.id)}`);
        }}
      />
    </AdminOperationsLayout>
  );
}
