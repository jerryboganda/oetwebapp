'use client';

import { useCallback, useEffect, useState } from 'react';
import type { JSX } from 'react';
import { AdminRouteSectionHeader, AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { aiAssistantClient } from '@/lib/ai-assistant/client';

interface SettingsState {
  globalEnabled: boolean;
  defaultProvider: string;
  defaultModel: string;
}

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AiAssistantTestConsolePage(): JSX.Element {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [settings, setSettings] = useState<SettingsState | null>(null);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      setSettings(await aiAssistantClient.getSettings());
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const onToggle = useCallback(async () => {
    if (!settings) return;
    setBusy(true);
    try {
      const next = await aiAssistantClient.toggleKillSwitch(!settings.globalEnabled);
      setSettings({
        globalEnabled: next.globalEnabled,
        defaultProvider: next.defaultProvider,
        defaultModel: next.defaultModel,
      });
      setToast({
        variant: 'success',
        message: next.globalEnabled ? 'Assistant enabled.' : 'Assistant disabled.',
      });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Toggle failed.' });
    } finally {
      setBusy(false);
    }
  }, [settings]);

  const onSmokeTest = useCallback(async () => {
    setBusy(true);
    try {
      const thread = await aiAssistantClient.createThread({ title: 'Admin smoke test' });
      setToast({ variant: 'success', message: `Smoke-test thread created: ${thread.id}. Open the chat surface to send a message.` });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Smoke test failed.' });
    } finally {
      setBusy(false);
    }
  }, []);

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant — Test Console"
        description="Manual kill-switch toggle and end-to-end smoke test (thread create). Use the main chat surface for streamed message tests."
      />

      {toast && <Toast variant={toast.variant === 'success' ? 'success' : 'error'} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={status} errorMessage="Could not load assistant status." onRetry={() => void load()}>
        {settings && (
          <>
            <AdminRoutePanel
              title="Kill switch"
              actions={
                <Button variant="primary" disabled={busy} onClick={() => void onToggle()}>
                  {settings.globalEnabled ? 'Disable assistant' : 'Enable assistant'}
                </Button>
              }
            >
              <Badge variant={settings.globalEnabled ? 'success' : 'danger'}>
                {settings.globalEnabled ? 'ENABLED' : 'DISABLED'}
              </Badge>
              <p className="mt-2 text-xs text-admin-text-muted">
                Default provider: <code>{settings.defaultProvider}</code> · Default model: <code>{settings.defaultModel}</code>
              </p>
            </AdminRoutePanel>
            <AdminRoutePanel
              title="Smoke test"
              description="Creates a new admin chat thread. To send a streamed test message, open the assistant from the admin sidebar."
              actions={
                <Button variant="ghost" disabled={busy} onClick={() => void onSmokeTest()}>
                  Create test thread
                </Button>
              }
            >
              <p className="text-xs text-admin-text-muted">
                Successful thread creation confirms: auth policy <code>AiAssistantUse</code>, rate-limiter,
                <code> AiChatThreads</code> insert, and audit-event write.
              </p>
            </AdminRoutePanel>
          </>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
