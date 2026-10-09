'use client';

import { useCallback, useEffect, useState } from 'react';
import { SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import {
  fetchAiProviders,
  probeAiProviderCapabilities,
  fetchAiProviderAutoSelection,
  updateAiProviderAutoSelection,
  type AiProviderAutoSelectionSnapshot,
  type AiProviderModelCapabilityRow,
  type AiProviderRow,
} from '@/lib/ai-management-api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

/**
 * Which provider currently wins the implicit "first active credentialed row" pick.
 *
 * This exists because the auto-selection flag is only useful if its EFFECT is visible. Without
 * the readout, flipping the toggle is a leap of faith, and "why is my route not being used" has
 * no answer anywhere on the screen.
 */
export function AiProviderAutoSelectionPanel() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [snapshot, setSnapshot] = useState<AiProviderAutoSelectionSnapshot | null>(null);
  const [saving, setSaving] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    try {
      setSnapshot(await fetchAiProviderAutoSelection());
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    queueMicrotask(() => { void load(); });
  }, [isAuthenticated, role, load]);

  const toggle = async (code: string, enabled: boolean) => {
    setSaving(code);
    try {
      await updateAiProviderAutoSelection(code, enabled);
      await load();
      setToast({
        variant: 'success',
        message: enabled
          ? `${code} may now be picked automatically for features nobody routed.`
          : `${code} is now reachable only where a route points at it.`,
      });
    } catch (e) {
      setToast({ variant: 'error', message: `Could not change auto-selection: ${(e as Error).message}` });
    } finally {
      setSaving(null);
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <SettingsSection
      title="Automatic provider selection"
      description="Only providers marked here are candidates for a feature you never routed. Everyone else is reachable solely where a route points at it, so adding a vendor can never silently change who answers Reading explanations or vocabulary cards."
    >
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AsyncStateWrapper status={status}>
        <div className="space-y-4">
          {snapshot?.winner ? (
            <p className="text-sm">
              <span className="font-medium">{snapshot.winner.name}</span>{' '}
              <span className="text-admin-fg-muted">
                ({snapshot.winner.code}) — priority {snapshot.winner.failoverPriority}, model{' '}
                {snapshot.winner.defaultModel}
              </span>
            </p>
          ) : (
            <p className="text-sm text-admin-fg-muted">
              No provider is eligible. A feature with no route and no eligible provider falls
              through to the local mock provider.
            </p>
          )}

          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-admin-fg-muted">
                  <th className="py-2 pr-3">Provider</th>
                  <th className="py-2 pr-3">Priority</th>
                  <th className="py-2 pr-3">Automatic</th>
                  <th className="py-2">Why not</th>
                </tr>
              </thead>
              <tbody>
                {snapshot?.all.map((row) => (
                  <tr key={row.id} className="border-t border-admin-border">
                    <td className="py-2 pr-3">
                      {row.name}{' '}
                      <span className="text-xs text-admin-fg-muted">({row.code})</span>
                    </td>
                    <td className="py-2 pr-3 tabular-nums">{row.failoverPriority}</td>
                    <td className="py-2 pr-3">
                      <Button
                        variant={row.participatesInAutoSelection ? 'primary' : 'outline'}
                        size="sm"
                        disabled={saving === row.code}
                        onClick={() => toggle(row.code, !row.participatesInAutoSelection)}
                      >
                        {row.participatesInAutoSelection ? 'On' : 'Off'}
                      </Button>
                    </td>
                    <td className="py-2 text-xs text-admin-fg-muted">{row.reason ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="text-xs text-admin-fg-muted">
            Lower priority is tried first. Providers added after this feature landed start switched
            OFF, so registering a vendor never changes live routing on its own.
          </p>
        </div>
      </AsyncStateWrapper>
    </SettingsSection>
  );
}

/**
 * Live capability probe.
 *
 * Every flag shown here is what the endpoint was OBSERVED to do, never what a vendor's docs claim.
 * That distinction is not pedantry: Z.AI's published OpenAPI schema binds its multimodal GLM models
 * to a request shape containing no `response_format` at all, while the model page advertises
 * Structured Output. Both cannot be true, and only one observed call settles it — which is why
 * routing is gated on this table and fails closed when it is empty.
 */
export function AiProviderCapabilityPanel() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [providers, setProviders] = useState<AiProviderRow[]>([]);
  const [selected, setSelected] = useState('');
  const [results, setResults] = useState<AiProviderModelCapabilityRow[]>([]);
  const [probing, setProbing] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    try {
      const all = await fetchAiProviders();
      setProviders(all.filter((p) => p.category === 'TextChat' && p.apiKeyHint));
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    queueMicrotask(() => { void load(); });
  }, [isAuthenticated, role, load]);

  const probe = async () => {
    if (!selected) return;
    setProbing(true);
    try {
      const res = await probeAiProviderCapabilities(selected);
      setResults(res.results ?? []);
      const failed = (res.results ?? []).filter((r) => r.probeStatus !== 'ok' && r.probeStatus !== 'partial');
      setToast({
        variant: failed.length === 0 ? 'success' : 'error',
        message: failed.length === 0
          ? `Probed ${res.results.length} model(s); results saved.`
          : `Probed ${res.results.length} model(s), but ${failed.length} did not respond normally: ${failed.map((f) => f.model).join(', ')}.`,
      });
    } catch (e) {
      setToast({ variant: 'error', message: `Probe failed: ${(e as Error).message}` });
    } finally {
      setProbing(false);
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <SettingsSection
      title="Model capability probe"
      description="Calls the provider's live endpoint with its real key and records what each model actually does — tools, images, documents, JSON mode, streaming, embeddings, thinking. Feature routing is gated on these results and refuses anything unproven."
    >
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AsyncStateWrapper status={status}>
        <div className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <Select
              label="Provider"
              value={selected}
              onChange={(e) => { setSelected(e.target.value); setResults([]); }}
              options={[
                { value: '', label: 'Select a provider…' },
                ...providers.map((p) => ({ value: p.code, label: `${p.name} (${p.code})` })),
              ]}
            />
            <Button variant="primary" size="sm" onClick={probe} disabled={!selected || probing}>
              {probing ? 'Probing…' : 'Probe capabilities'}
            </Button>
          </div>

          {results.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-admin-fg-muted">
                    <th className="py-2 pr-3">Model</th>
                    <th className="py-2 pr-3">Tools</th>
                    <th className="py-2 pr-3">Images</th>
                    <th className="py-2 pr-3">JSON</th>
                    <th className="py-2 pr-3">Stream</th>
                    <th className="py-2 pr-3">Embed</th>
                    <th className="py-2">Thinking</th>
                  </tr>
                </thead>
                <tbody>
                  {results.map((r) => (
                    <tr key={r.model} className="border-t border-admin-border align-top">
                      <td className="py-2 pr-3">
                        <div className="font-medium">{r.model}</div>
                        <Badge variant={r.probeStatus === 'ok' ? 'success' : 'warning'}>{r.probeStatus}</Badge>
                        {r.probeDetail && (
                          <div className="mt-1 max-w-md text-xs text-admin-fg-muted">{r.probeDetail}</div>
                        )}
                      </td>
                      <td className="py-2 pr-3">{r.supportsTools ? 'yes' : 'no'}</td>
                      <td className="py-2 pr-3">{r.supportsVision ? 'yes' : 'no'}</td>
                      <td className="py-2 pr-3">{r.supportsJsonMode ? 'yes' : 'no'}</td>
                      <td className="py-2 pr-3">{r.supportsStreaming ? 'yes' : 'no'}</td>
                      <td className="py-2 pr-3">{r.supportsEmbeddings ? 'yes' : 'no'}</td>
                      <td className="py-2 text-xs">
                        {!r.supportsThinking
                          ? 'n/a'
                          : r.thinkingCanBeDisabled
                            ? r.allowedReasoningEffortsCsv || 'selectable'
                            : 'forced on (cannot disable)'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <p className="text-xs text-admin-fg-muted">
            Probing spends a handful of very small requests against your own key. It runs only when
            you press the button — never on a schedule and never in CI. Each probe re-reads the live
            endpoint, so re-run it after a vendor changes its model lineup.
          </p>
        </div>
      </AsyncStateWrapper>
    </SettingsSection>
  );
}