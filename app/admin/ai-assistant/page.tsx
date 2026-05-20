'use client';

import { useCallback, useEffect, useState } from 'react';
import type { JSX } from 'react';
import { AlertTriangle, Power, Zap } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { aiAssistantClient } from '@/lib/ai-assistant/client';

interface SettingsState {
  globalEnabled: boolean;
  defaultProvider: string;
  defaultModel: string;
  lastKillSwitchAt: string | null;
  lastKillSwitchActor: string | null;
}

interface UsageSummary {
  total: number;
  success: number;
  errors: number;
  promptTokens: number;
  completionTokens: number;
}

interface UsageRow {
  outcome?: string;
  promptTokens?: number;
  completionTokens?: number;
  occurredAt?: string;
}

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const EMPTY_USAGE: UsageSummary = { total: 0, success: 0, errors: 0, promptTokens: 0, completionTokens: 0 };

export default function AiAssistantAdminDashboardPage(): JSX.Element {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [settings, setSettings] = useState<SettingsState | null>(null);
  const [usage, setUsage] = useState<UsageSummary>(EMPTY_USAGE);
  const [toggling, setToggling] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const [s, rows] = await Promise.all([
        aiAssistantClient.getSettings(),
        aiAssistantClient.getUsage(200, 0),
      ]);
      setSettings(s);
      const summary = (rows as UsageRow[]).reduce<UsageSummary>((acc, r) => ({
        total: acc.total + 1,
        success: acc.success + (r.outcome === 'success' ? 1 : 0),
        errors: acc.errors + (r.outcome && r.outcome !== 'success' && r.outcome !== 'empty' ? 1 : 0),
        promptTokens: acc.promptTokens + (r.promptTokens ?? 0),
        completionTokens: acc.completionTokens + (r.completionTokens ?? 0),
      }), { ...EMPTY_USAGE });
      setUsage(summary);
      setStatus('success');
    } catch (err) {
      setStatus('error');
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load AI Assistant data.' });
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const onToggle = useCallback(async () => {
    if (!settings) return;
    setToggling(true);
    try {
      const next = await aiAssistantClient.toggleKillSwitch(!settings.globalEnabled);
      setSettings(next);
      setToast({
        variant: 'success',
        message: next.globalEnabled ? 'AI Assistant enabled.' : 'AI Assistant disabled (kill-switch engaged).',
      });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to toggle kill-switch.' });
    } finally {
      setToggling(false);
    }
  }, [settings]);

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant"
        description="Admin dashboard for the in-app agentic chatbot. Use the kill-switch to stop all calls immediately."
      />

      {toast && (
        <Toast variant={toast.variant === 'success' ? 'success' : 'error'} message={toast.message} onClose={() => setToast(null)} />
      )}

      <AsyncStateWrapper
        status={status}
        errorMessage="Could not load AI Assistant status."
        onRetry={() => void load()}
      >
        {settings && (
          <div className="grid gap-3 md:grid-cols-4">
            <AdminRouteSummaryCard
              label="Global status"
              value={settings.globalEnabled ? 'Enabled' : 'Disabled'}
              tone={settings.globalEnabled ? 'success' : 'danger'}
              icon={Power}
              statusLabel={settings.lastKillSwitchAt ? `Toggled ${new Date(settings.lastKillSwitchAt).toLocaleString()}` : undefined}
            />
            <AdminRouteSummaryCard
              label="Default provider"
              value={settings.defaultProvider || '—'}
              hint={settings.defaultModel || ''}
              icon={Zap}
            />
            <AdminRouteSummaryCard
              label="Turns (last 200)"
              value={usage.total}
              hint={`${usage.success} success · ${usage.errors} errors`}
              tone={usage.errors > 0 ? 'warning' : 'default'}
              icon={AlertTriangle}
            />
            <AdminRouteSummaryCard
              label="Tokens"
              value={usage.promptTokens + usage.completionTokens}
              hint={`${usage.promptTokens} prompt · ${usage.completionTokens} completion`}
            />
          </div>
        )}

        {settings && (
          <AdminRoutePanel
            title="Kill switch"
            description="Disabling the assistant stops all new chat turns immediately. Existing in-flight turns continue until they finish or are cancelled."
            actions={
              <Button
                variant="primary"
                onClick={() => void onToggle()}
                disabled={toggling}
              >
                {toggling ? 'Saving…' : settings.globalEnabled ? 'Disable AI Assistant' : 'Enable AI Assistant'}
              </Button>
            }
          >
            <div className="flex items-center gap-3">
              <Badge variant={settings.globalEnabled ? 'success' : 'danger'}>
                {settings.globalEnabled ? 'ENABLED' : 'DISABLED'}
              </Badge>
              {settings.lastKillSwitchActor && (
                <span className="text-xs text-admin-text-muted">
                  Last actor: {settings.lastKillSwitchActor}
                </span>
              )}
            </div>
          </AdminRoutePanel>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
