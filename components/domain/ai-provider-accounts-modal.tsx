'use client';

import { useCallback, useEffect, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import {
  fetchAiProviderAccounts,
  createAiProviderAccount,
  updateAiProviderAccount,
  deactivateAiProviderAccount,
  resetAiProviderAccount,
  testAiProviderAccount,
  type AiProviderAccountRow,
  type AiProviderTestStatus,
  type AiProviderAccountUpsertBody,
} from '@/lib/ai-management-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface AccountDraft {
  id: string;
  label: string;
  apiKey: string;
  monthlyRequestCap: number | null;
  priority: number;
  isActive: boolean;
}

const EMPTY_DRAFT: AccountDraft = {
  id: '',
  label: '',
  apiKey: '',
  monthlyRequestCap: null,
  priority: 0,
  isActive: true,
};

function accountTestStatusVariant(status: AiProviderTestStatus): 'success' | 'danger' | 'warning' | 'muted' {
  switch (status) {
    case 'ok':
      return 'success';
    case 'auth':
      return 'danger';
    case 'rate_limited':
      return 'warning';
    case 'network':
    case 'unknown':
    default:
      return 'muted';
  }
}

export interface AiProviderAccountsModalProps {
  open: boolean;
  providerId: string;
  providerLabel: string;
  onClose: () => void;
}

/// <summary>
/// Nested account-pool manager for a single provider. Lists existing
/// `AiProviderAccount` rows, lets the operator add a new PAT, edit
/// label/cap/priority/active, reset the monthly counter (clears
/// quarantine), and soft-deactivate.
/// </summary>
export function AiProviderAccountsModal({ open, providerId, providerLabel, onClose }: AiProviderAccountsModalProps) {
  const [rows, setRows] = useState<AiProviderAccountRow[]>([]);
  const [status, setStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [draft, setDraft] = useState<AccountDraft | null>(null);
  const [creating, setCreating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [testingAccountId, setTestingAccountId] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!providerId) return;
    setStatus('loading');
    try {
      const data = await fetchAiProviderAccounts(providerId);
      setRows(data);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `Load failed: ${(e as Error).message}` });
    }
  }, [providerId]);

  // Parent unmounts the component on close, so we don't need to reset
  // state here — re-mounting starts from the initial values.
  useEffect(() => {
    if (open) {
      queueMicrotask(() => { void load(); });
    }
  }, [open, load]);

  const save = async () => {
    if (!draft) return;
    if (!draft.label.trim()) {
      setToast({ variant: 'error', message: 'Label is required.' });
      return;
    }
    if (creating && (!draft.apiKey || draft.apiKey.length < 16)) {
      setToast({ variant: 'error', message: 'API key must be at least 16 characters.' });
      return;
    }
    if (!creating && draft.apiKey && draft.apiKey.length < 16) {
      setToast({ variant: 'error', message: 'API key must be at least 16 characters.' });
      return;
    }
    if (draft.monthlyRequestCap !== null && draft.monthlyRequestCap < 1) {
      setToast({ variant: 'error', message: 'Monthly cap must be empty or at least 1.' });
      return;
    }

    const body: AiProviderAccountUpsertBody = {
      label: draft.label.trim(),
      apiKey: draft.apiKey ? draft.apiKey : undefined,
      monthlyRequestCap: draft.monthlyRequestCap,
      priority: draft.priority,
      isActive: draft.isActive,
    };

    try {
      if (creating) {
        await createAiProviderAccount(providerId, body);
        setToast({ variant: 'success', message: 'Account added.' });
      } else {
        await updateAiProviderAccount(providerId, draft.id, body);
        setToast({ variant: 'success', message: 'Account updated.' });
      }
      setDraft(null);
      setCreating(false);
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` });
    }
  };

  const deactivate = async (accountId: string) => {
    try {
      await deactivateAiProviderAccount(providerId, accountId);
      setToast({ variant: 'success', message: 'Account deactivated.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Failed: ${(e as Error).message}` });
    }
  };

  const reset = async (accountId: string) => {
    try {
      await resetAiProviderAccount(providerId, accountId);
      setToast({ variant: 'success', message: 'Counter and quarantine cleared.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Failed: ${(e as Error).message}` });
    }
  };

  const runTest = async (accountId: string) => {
    setTestingAccountId(accountId);
    try {
      const result = await testAiProviderAccount(providerId, accountId);
      setToast({
        variant: result.status === 'ok' ? 'success' : 'error',
        message: `Test: ${result.status}${result.errorMessage ? ' — ' + result.errorMessage : ''} (${result.latencyMs} ms)`,
      });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Test failed: ${(e as Error).message}` });
    } finally {
      setTestingAccountId(null);
    }
  };

  const columns: Column<AiProviderAccountRow>[] = [
    { key: 'label', header: 'Label', render: (a) => <span className="font-medium">{a.label}</span> },
    { key: 'k', header: 'Key', render: (a) => <span className="font-mono text-xs">{a.apiKeyHint}</span> },
    {
      key: 'used',
      header: 'Used / cap',
      render: (a) => (
        <span className="font-mono text-xs">
          {a.requestsUsedThisMonth.toLocaleString()} / {a.monthlyRequestCap ? a.monthlyRequestCap.toLocaleString() : '∞'}
        </span>
      ),
    },
    { key: 'pri', header: 'Priority', render: (a) => a.priority },
    {
      key: 'q',
      header: 'Status',
      render: (a) => {
        if (!a.isActive) return <Badge variant="muted">Disabled</Badge>;
        if (a.exhaustedUntil && new Date(a.exhaustedUntil) > new Date()) {
          return <Badge variant="warning">Quarantined</Badge>;
        }
        if (a.monthlyRequestCap !== null && a.requestsUsedThisMonth >= a.monthlyRequestCap) {
          return <Badge variant="danger">At cap</Badge>;
        }
        return <Badge variant="success">Active</Badge>;
      },
    },
    {
      key: 'last',
      header: 'Last test',
      render: (a) => a.lastTestStatus
        ? (
          <div className="flex flex-col gap-1">
            <Badge variant={accountTestStatusVariant(a.lastTestStatus)}>{a.lastTestStatus}</Badge>
            {a.lastTestedAt && <span className="text-[10px] text-muted">{new Date(a.lastTestedAt).toLocaleString()}</span>}
          </div>
        )
        : <span className="text-xs text-muted">—</span>,
    },
    {
      key: 'acts',
      header: 'Actions',
      render: (a) => (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={() =>
              setDraft({
                id: a.id,
                label: a.label,
                apiKey: '',
                monthlyRequestCap: a.monthlyRequestCap,
                priority: a.priority,
                isActive: a.isActive,
              })
            }
          >
            Edit
          </Button>
          <Button variant="ghost" size="sm" onClick={() => void reset(a.id)}>
            Reset
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={testingAccountId === a.id}
            onClick={() => void runTest(a.id)}
          >
            {testingAccountId === a.id ? 'Testing…' : 'Test'}
          </Button>
          {a.isActive && (
            <Button variant="ghost" size="sm" onClick={() => void deactivate(a.id)}>
              Disable
            </Button>
          )}
        </div>
      ),
    },
  ];

  if (!open) return null;

  return (
    <Modal open={open} onClose={onClose} title={`Account pool — ${providerLabel}`} size="lg">
      <div className="space-y-4">
        {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

        <p className="text-sm text-muted">
          Multi-account pool for this provider. Requests are auto-distributed in priority order; on a 429 the
          account is quarantined for 15 minutes and the next account takes over. On a 401/403 the account is
          deactivated until you re-enable it.
        </p>

        <div className="flex justify-end">
          <Button
            variant="primary"
            size="sm"
            onClick={() => {
              setCreating(true);
              setDraft({ ...EMPTY_DRAFT });
            }}
          >
            + Add account
          </Button>
        </div>

        {status === 'loading' && <p className="text-sm text-muted">Loading…</p>}
        {status !== 'loading' && rows.length === 0 && (
          <p className="text-sm text-muted">
            No accounts yet — add one above. Until you do, this provider falls back to its single API key on the
            row above.
          </p>
        )}
        {rows.length > 0 && <DataTable data={rows} columns={columns} keyExtractor={(a) => a.id} />}

        {draft && (
          <Modal
            open={true}
            onClose={() => {
              setDraft(null);
              setCreating(false);
            }}
            title={creating ? 'Add account' : `Edit ${draft.label || 'account'}`}
          >
            <div className="space-y-3">
              <Input
                label="Label"
                value={draft.label}
                onChange={(e) => setDraft({ ...draft, label: e.target.value })}
                placeholder="primary-org"
              />
              <Input
                label={creating ? 'PAT / API key' : 'PAT / API key (leave blank to keep current)'}
                value={draft.apiKey}
                onChange={(e) => setDraft({ ...draft, apiKey: e.target.value })}
                type="password"
                placeholder={creating ? 'ghp_… or any 16+ char key' : '••••••••'}
              />
              <Input
                label="Monthly request cap (blank = unlimited)"
                value={draft.monthlyRequestCap === null ? '' : String(draft.monthlyRequestCap)}
                onChange={(e) => {
                  const v = e.target.value.trim();
                  setDraft({ ...draft, monthlyRequestCap: v === '' ? null : Number(v) });
                }}
                type="number"
                inputMode="numeric"
                min={1}
              />
              <Input
                label="Priority (lower = tried first)"
                value={String(draft.priority)}
                onChange={(e) => setDraft({ ...draft, priority: Number(e.target.value) || 0 })}
                type="number"
                inputMode="numeric"
              />
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={draft.isActive}
                  onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })}
                />
                Active
              </label>
              <div className="flex justify-end gap-2 pt-2">
                <Button
                  variant="outline"
                  onClick={() => {
                    setDraft(null);
                    setCreating(false);
                  }}
                >
                  Cancel
                </Button>
                <Button variant="primary" onClick={() => void save()}>
                  Save
                </Button>
              </div>
            </div>
          </Modal>
        )}
      </div>
    </Modal>
  );
}
