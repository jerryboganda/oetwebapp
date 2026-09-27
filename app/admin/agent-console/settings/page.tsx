'use client';

import { useState } from 'react';
import { LogOut, Plug } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { AuditTail } from '@/components/admin/agent-console/AuditTail';
import { ConnectEngineDialog } from '@/components/admin/agent-console/ConnectEngineDialog';
import { ConsoleStatusStrip } from '@/components/admin/agent-console/ConsoleStatusStrip';
import { ENGINE_LABEL } from '@/components/admin/agent-console/EngineModelEffortPicker';
import { GithubTokensCard } from '@/components/admin/agent-console/GithubTokensCard';
import { useOwnerAgentConsole } from '@/components/admin/agent-console/OwnerAgentConsoleShell';
import { RateLimitBadge } from '@/components/admin/agent-console/RateLimitBadge';
import {
  connectEngine,
  describeOwnerAgentError,
  logoutEngine,
  putGithubTokens,
} from '@/lib/owner-agent/api';
import { OWNER_AGENT_ENGINES, type ConnectFlow, type Engine } from '@/lib/owner-agent/types';

const SIGN_IN_HELP: Record<Engine, string> = {
  claude:
    'Signs the bundled Claude Code CLI in with your Claude subscription. You sign in on claude.ai and paste the code back here; the CLI keeps its own credentials inside the console container.',
  codex:
    'Signs Codex in with ChatGPT (device code). Open the OpenAI page, enter the code before it expires; Codex stores its own credentials inside the console container.',
};

export default function AgentConsoleSettingsPage() {
  const consoleState = useOwnerAgentConsole();
  const [flow, setFlow] = useState<ConnectFlow | null>(null);
  const [busyEngine, setBusyEngine] = useState<Engine | null>(null);
  const [confirmLogout, setConfirmLogout] = useState<Engine | null>(null);
  const [error, setError] = useState<string | null>(null);
  const status = consoleState.status;

  const connect = async (engine: Engine) => {
    setBusyEngine(engine);
    setError(null);
    try {
      setFlow(await connectEngine(engine));
    } catch (err) {
      setError(describeOwnerAgentError(err, 'Could not start the sign-in.'));
    } finally {
      setBusyEngine(null);
    }
  };

  const logout = async (engine: Engine) => {
    setConfirmLogout(null);
    setBusyEngine(engine);
    setError(null);
    try {
      await logoutEngine(engine);
      await consoleState.refreshStatus();
    } catch (err) {
      setError(describeOwnerAgentError(err, 'Sign-out failed.'));
    } finally {
      setBusyEngine(null);
    }
  };

  const saveTokens = async (tokens: { agentToken?: string; shipToken?: string }) => {
    try {
      await putGithubTokens(tokens);
    } catch (err) {
      throw new Error(describeOwnerAgentError(err, 'Tokens were not saved.'));
    }
    await consoleState.refreshStatus();
  };

  return (
    <AdminOperationsLayout
      title="Agent Console Settings"
      eyebrow="Owner only"
      breadcrumbs={[{ label: 'Agent Console', href: '/admin/agent-console' }, { label: 'Settings' }]}
      description="Engine sign-in, GitHub tokens, kill switch and the audit trail."
      kpis={
        <ConsoleStatusStrip
          status={status}
          unlockExpiresAt={consoleState.unlock.expiresAt}
          onLockNow={consoleState.lockConsole}
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
        {error ? (
          <p className="text-xs text-red-700 dark:text-red-300" role="alert">
            {error}
          </p>
        ) : null}

        <div className="grid gap-4 lg:grid-cols-2">
          {OWNER_AGENT_ENGINES.map((engine) => {
            const engineStatus = status?.engines?.[engine];
            const auth = engineStatus?.auth;
            const signedIn = auth?.state === 'signed_in';
            return (
              <Card key={engine} data-testid={`engine-card-${engine}`}>
                <CardHeader className="flex-col items-start gap-1">
                  <CardTitle className="flex items-center gap-2 text-base">
                    {ENGINE_LABEL[engine]}
                    <Badge variant={signedIn ? 'success' : auth?.state === 'error' ? 'danger' : 'muted'}>
                      {auth?.state?.replace('_', ' ') ?? 'unknown'}
                    </Badge>
                  </CardTitle>
                  <CardDescription>{SIGN_IN_HELP[engine]}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-3 text-xs">
                  <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                    <dt className="text-admin-fg-muted">Account</dt>
                    <dd className="truncate">{auth?.account?.email ?? '—'}</dd>
                    <dt className="text-admin-fg-muted">Plan</dt>
                    <dd>{auth?.account?.plan ?? '—'}</dd>
                    <dt className="text-admin-fg-muted">Workspace</dt>
                    <dd className="truncate">{auth?.account?.workspace ?? '—'}</dd>
                    <dt className="text-admin-fg-muted">Version</dt>
                    <dd className="font-mono">{engineStatus?.version ?? '—'}</dd>
                    <dt className="text-admin-fg-muted">Models</dt>
                    <dd>{engineStatus?.models?.length ?? 0}</dd>
                  </dl>
                  {auth?.detail ? <p className="text-admin-fg-muted">{auth.detail}</p> : null}
                  <RateLimitBadge limits={engineStatus?.rateLimits} />
                  <div className="flex flex-wrap gap-2">
                    <Button variant={signedIn ? 'outline' : 'primary'} size="sm" loading={busyEngine === engine} onClick={() => void connect(engine)}>
                      <Plug className="h-4 w-4" aria-hidden="true" /> {signedIn ? 'Reconnect' : 'Connect'}
                    </Button>
                    {signedIn ? (
                      <Button variant="ghost" size="sm" disabled={busyEngine !== null} onClick={() => setConfirmLogout(engine)}>
                        <LogOut className="h-4 w-4" aria-hidden="true" /> Sign out
                      </Button>
                    ) : null}
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>

        <GithubTokensCard github={status?.github} onSave={saveTokens} />

        <AuditTail />
      </div>

      <ConnectEngineDialog
        flow={flow}
        onClose={() => setFlow(null)}
        onFinished={() => {
          void consoleState.refreshStatus();
        }}
      />

      <Modal
        open={confirmLogout !== null}
        onClose={() => setConfirmLogout(null)}
        title={confirmLogout ? `Sign ${ENGINE_LABEL[confirmLogout]} out?` : 'Sign out?'}
        size="sm"
      >
        <div className="space-y-4 text-sm">
          <p>New turns on this engine will fail until you connect it again. Running turns may stop.</p>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" size="sm" onClick={() => setConfirmLogout(null)}>
              Cancel
            </Button>
            <Button variant="destructive" size="sm" onClick={() => (confirmLogout ? void logout(confirmLogout) : undefined)}>
              Sign out
            </Button>
          </div>
        </div>
      </Modal>
    </AdminOperationsLayout>
  );
}
