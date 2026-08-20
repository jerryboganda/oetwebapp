'use client';

import { useCallback, useEffect, useState } from 'react';
import { Save } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { Label } from '@/components/admin/ui/label';
import { Switch } from '@/components/admin/ui/switch';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  listAdminPaymentGateways,
  updateAdminPaymentGateway,
  type AdminPaymentGatewayDto,
} from '@/lib/api';

export default function AdminPaymentGatewaysPage() {
  const [rows, setRows] = useState<AdminPaymentGatewayDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<Record<string, { apiKey: string; hashApiKey: string; providerKey: string; webhookSecret: string; companyId: string }>>({});

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const next = await listAdminPaymentGateways();
      setRows(next);
      setDrafts(Object.fromEntries(next.map((row) => [row.name, {
        apiKey: '',
        hashApiKey: '',
        providerKey: row.providerKey ?? '',
        webhookSecret: '',
        companyId: row.companyId ?? '',
      }])));
      setError(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load payment gateways.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function toggleEnabled(row: AdminPaymentGatewayDto, isEnabled: boolean) {
    setSaving(row.name);
    try {
      const updated = await updateAdminPaymentGateway(row.name, { isEnabled });
      setRows((current) => current.map((item) => (item.name === row.name ? updated : item)));
      toast.success(`${row.label} ${isEnabled ? 'enabled' : 'disabled'}.`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not update gateway.');
    } finally {
      setSaving(null);
    }
  }

  async function saveSecrets(row: AdminPaymentGatewayDto) {
    const draft = drafts[row.name];
    if (!draft) return;
    setSaving(row.name);
    try {
      const updated = await updateAdminPaymentGateway(row.name, {
        apiKey: draft.apiKey.trim() || undefined,
        hashApiKey: draft.hashApiKey.trim() || undefined,
        providerKey: draft.providerKey,
        webhookSecret: draft.webhookSecret.trim() || undefined,
        companyId: draft.companyId,
      });
      setRows((current) => current.map((item) => (item.name === row.name ? updated : item)));
      setDrafts((current) => ({
        ...current,
        [row.name]: { ...draft, apiKey: '', hashApiKey: '', webhookSecret: '' },
      }));
      toast.success(`${row.label} credentials saved.`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save credentials.');
    } finally {
      setSaving(null);
    }
  }

  return (
    <AdminTableLayout
      title="Payment Gateways"
      description="Turn candidate-facing card gateways on or off without a deploy. Secrets stay server-side."
    >
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {loading ? <p className="text-sm text-muted">Loading gateways…</p> : null}
      <div className="space-y-4">
        {rows.map((row) => {
          const draft = drafts[row.name] ?? { apiKey: '', hashApiKey: '', providerKey: '', webhookSecret: '', companyId: '' };
          const busy = saving === row.name;
          return (
            <section key={row.name} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-lg font-semibold">{row.label}</h2>
                    {row.isPrimary ? <Badge>MAIN</Badge> : null}
                    <Badge variant={row.isEnabled ? 'success' : 'secondary'}>{row.isEnabled ? 'On' : 'Off'}</Badge>
                    <Badge variant="secondary">{row.region}</Badge>
                    <Badge variant="secondary">{row.mode}</Badge>
                    <Badge variant={row.isConfigured ? 'success' : 'secondary'}>
                      {row.isConfigured ? 'Configured' : 'Needs keys'}
                    </Badge>
                  </div>
                  <p className="mt-1 text-sm text-muted">{row.candidateLabel}</p>
                  {(row.name === 'whop' || row.name === 'fawaterak') ? (
                    <p className="mt-1 font-mono text-xs text-muted">/v1/payment/webhooks/{row.name}</p>
                  ) : null}
                </div>
                <div className="flex items-center gap-2">
                  <Label htmlFor={`gw-${row.name}`} className="text-sm">Enabled</Label>
                  <Switch
                    id={`gw-${row.name}`}
                    checked={row.isEnabled}
                    disabled={busy}
                    onCheckedChange={(checked) => void toggleEnabled(row, checked)}
                  />
                </div>
              </div>

              {(row.name === 'whop' || row.name === 'fawaterak') ? (
                <div className="mt-4 grid gap-3 sm:grid-cols-2">
                  {row.name === 'whop' ? (
                    <>
                      <div>
                        <Label htmlFor={`${row.name}-api`}>Company API key</Label>
                        <Input
                          id={`${row.name}-api`}
                          type="password"
                          autoComplete="off"
                          placeholder={row.apiKeyMasked || 'Paste live key'}
                          value={draft.apiKey}
                          onChange={(event) => setDrafts((current) => ({ ...current, [row.name]: { ...draft, apiKey: event.target.value } }))}
                        />
                      </div>
                      <div>
                        <Label htmlFor={`${row.name}-company`}>Company id</Label>
                        <Input
                          id={`${row.name}-company`}
                          value={draft.companyId}
                          onChange={(event) => setDrafts((current) => ({ ...current, [row.name]: { ...draft, companyId: event.target.value } }))}
                        />
                      </div>
                      <div className="sm:col-span-2">
                        <Label htmlFor={`${row.name}-webhook`}>Webhook secret (optional)</Label>
                        <Input
                          id={`${row.name}-webhook`}
                          type="password"
                          autoComplete="off"
                          placeholder={row.webhookSecretMasked || 'Optional signing secret'}
                          value={draft.webhookSecret}
                          onChange={(event) => setDrafts((current) => ({ ...current, [row.name]: { ...draft, webhookSecret: event.target.value } }))}
                        />
                      </div>
                    </>
                  ) : (
                    <>
                      <div>
                        <Label htmlFor={`${row.name}-hash`}>HASH API key</Label>
                        <Input
                          id={`${row.name}-hash`}
                          type="password"
                          autoComplete="off"
                          placeholder={row.hashKeyMasked || 'Paste HASH key'}
                          value={draft.hashApiKey}
                          onChange={(event) => setDrafts((current) => ({ ...current, [row.name]: { ...draft, hashApiKey: event.target.value } }))}
                        />
                      </div>
                      <div>
                        <Label htmlFor={`${row.name}-provider`}>Provider key</Label>
                        <Input
                          id={`${row.name}-provider`}
                          value={draft.providerKey}
                          onChange={(event) => setDrafts((current) => ({ ...current, [row.name]: { ...draft, providerKey: event.target.value } }))}
                        />
                      </div>
                    </>
                  )}
                  <div className="sm:col-span-2">
                    <Button type="button" onClick={() => void saveSecrets(row)} disabled={busy}>
                      <Save className="h-4 w-4" /> Save credentials
                    </Button>
                  </div>
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted">
                  Kept in code but disabled. Enable only after credentials are configured in Runtime Settings.
                </p>
              )}
            </section>
          );
        })}
      </div>
    </AdminTableLayout>
  );
}
