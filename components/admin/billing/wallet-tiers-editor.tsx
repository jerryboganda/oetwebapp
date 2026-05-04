'use client';

import { useCallback, useMemo, useState } from 'react';
import { Plus, Save, Trash2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import type { AdminWalletTierInput, AdminWalletTierRow } from '@/lib/api';
import { BillingConfirmDialog } from './confirm-dialog';

export interface WalletTiersEditorProps {
  initialTiers: AdminWalletTierRow[];
  defaultCurrency: string;
  source: 'database' | 'appsettings';
  onSave: (tiers: AdminWalletTierInput[]) => Promise<void>;
  canWrite?: boolean;
}

interface DraftRow {
  key: string;
  id: string | null;
  amount: string;
  credits: string;
  bonus: string;
  label: string;
  isPopular: boolean;
  displayOrder: string;
  isActive: boolean;
  currency: string;
}

interface RowErrors {
  amount?: string;
  credits?: string;
  bonus?: string;
  currency?: string;
  displayOrder?: string;
}

let keySeq = 0;
const nextKey = () => `tier-${++keySeq}`;
const fieldId = (row: DraftRow, field: string) => `${row.key}-${field}`;

function toDraft(row: AdminWalletTierRow, defaultCurrency: string): DraftRow {
  return {
    key: nextKey(),
    id: row.id,
    amount: String(row.amount),
    credits: String(row.credits),
    bonus: String(row.bonus),
    label: row.label ?? '',
    isPopular: row.isPopular,
    displayOrder: String(row.displayOrder),
    isActive: row.isActive,
    currency: row.currency || defaultCurrency,
  };
}

function emptyDraft(defaultCurrency: string, displayOrder: number): DraftRow {
  return {
    key: nextKey(),
    id: null,
    amount: '',
    credits: '',
    bonus: '0',
    label: '',
    isPopular: false,
    displayOrder: String(displayOrder),
    isActive: true,
    currency: defaultCurrency,
  };
}

// Server stores amount/credits/bonus as integer cents/credits — reject decimals
// client-side too so we don't silently round on save.
function isPositiveInteger(value: number): boolean {
  return Number.isInteger(value) && value > 0;
}
function isNonNegativeInteger(value: number): boolean {
  return Number.isInteger(value) && value >= 0;
}

function validateRow(row: DraftRow, defaultCurrency: string): RowErrors {
  const errs: RowErrors = {};
  const amount = Number(row.amount);
  const credits = Number(row.credits);
  const bonus = Number(row.bonus);
  const order = Number(row.displayOrder);

  if (!row.amount || Number.isNaN(amount) || !isPositiveInteger(amount)) {
    errs.amount = 'Must be a positive integer (cents)';
  }
  if (row.credits === '' || Number.isNaN(credits) || !isNonNegativeInteger(credits)) {
    errs.credits = 'Must be a non-negative integer';
  }
  if (row.bonus === '' || Number.isNaN(bonus) || !isNonNegativeInteger(bonus)) {
    errs.bonus = 'Must be a non-negative integer';
  }
  if (row.displayOrder === '' || Number.isNaN(order) || !isNonNegativeInteger(order)) {
    errs.displayOrder = 'Must be a non-negative integer';
  }
  if (!/^[A-Za-z]{3}$/.test(row.currency.trim())) {
    errs.currency = '3-letter ISO';
  } else if (row.currency.trim().toUpperCase() !== defaultCurrency.toUpperCase()) {
    errs.currency = `Must be ${defaultCurrency.toUpperCase()}`;
  }
  return errs;
}

export function WalletTiersEditor({ initialTiers, defaultCurrency, source, onSave, canWrite = true }: WalletTiersEditorProps) {
  const [drafts, setDrafts] = useState<DraftRow[]>(() =>
    initialTiers.length > 0
      ? initialTiers.map((t) => toDraft(t, defaultCurrency))
      : [emptyDraft(defaultCurrency, 0)],
  );
  const [saving, setSaving] = useState(false);
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [pendingRemoveKey, setPendingRemoveKey] = useState<string | null>(null);

  const errorsByKey = useMemo(() => {
    const map = new Map<string, RowErrors>();
    drafts.forEach((d) => map.set(d.key, validateRow(d, defaultCurrency)));
    return map;
  }, [defaultCurrency, drafts]);

  const duplicateAmounts = useMemo(() => {
    const counts = new Map<number, number>();
    drafts.forEach((d) => {
      const n = Number(d.amount);
      if (!Number.isNaN(n) && n > 0) {
        counts.set(n, (counts.get(n) ?? 0) + 1);
      }
    });
    return new Set([...counts.entries()].filter(([, c]) => c > 1).map(([n]) => n));
  }, [drafts]);

  const hasErrors = useMemo(() => {
    if (drafts.length === 0) return true;
    if (!drafts.some((d) => d.isActive)) return true;
    if (duplicateAmounts.size > 0) return true;
    return drafts.some((d) => Object.keys(errorsByKey.get(d.key) ?? {}).length > 0);
  }, [drafts, duplicateAmounts, errorsByKey]);

  const setLevelError = useMemo(() => {
    if (drafts.length === 0) return 'At least one active tier is required.';
    if (!drafts.some((d) => d.isActive)) return 'At least one tier must remain active.';
    return null;
  }, [drafts]);

  // Tiers should be displayed in ascending amount order so they overlap visually with display
  // order. Server enforces uniqueness; client previews ordering for usability.
  const ascendingWarning = useMemo(() => {
    const amounts = drafts
      .filter((d) => d.isActive)
      .map((d) => Number(d.amount))
      .filter((n) => Number.isFinite(n) && n > 0);
    for (let i = 1; i < amounts.length; i += 1) {
      if (amounts[i] <= amounts[i - 1]) {
        return 'Active tier amounts should be strictly ascending in row order.';
      }
    }
    return null;
  }, [drafts]);

  const updateRow = useCallback((key: string, patch: Partial<DraftRow>) => {
    setDrafts((prev) => prev.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  }, []);

  const addRow = useCallback(() => {
    setDrafts((prev) => [...prev, emptyDraft(defaultCurrency, prev.length)]);
  }, [defaultCurrency]);

  const requestRemoveRow = useCallback((key: string) => {
    setPendingRemoveKey(key);
  }, []);

  const confirmRemoveRow = useCallback(() => {
    if (!pendingRemoveKey) return;
    setDrafts((prev) => prev.filter((row) => row.key !== pendingRemoveKey));
    setPendingRemoveKey(null);
  }, [pendingRemoveKey]);

  const cancelRemoveRow = useCallback(() => {
    setPendingRemoveKey(null);
  }, []);

  const handleSave = useCallback(async () => {
    if (!canWrite) {
      setFeedback({ tone: 'error', message: 'You have read-only billing access.' });
      return;
    }
    if (hasErrors) {
      setFeedback({ tone: 'error', message: 'Fix the validation errors before saving.' });
      return;
    }
    setSaving(true);
    setFeedback(null);
    try {
      const payload: AdminWalletTierInput[] = drafts.map((d) => ({
        id: d.id,
        amount: Number(d.amount),
        credits: Number(d.credits),
        bonus: Number(d.bonus),
        label: d.label.trim() || null,
        isPopular: d.isPopular,
        displayOrder: Number(d.displayOrder),
        isActive: d.isActive,
        currency: d.currency.trim().toUpperCase(),
      }));
      await onSave(payload);
      setFeedback({ tone: 'success', message: 'Wallet top-up tiers saved.' });
    } catch (err) {
      setFeedback({
        tone: 'error',
        message: err instanceof Error ? err.message : 'Failed to save wallet top-up tiers.',
      });
    } finally {
      setSaving(false);
    }
  }, [canWrite, drafts, hasErrors, onSave]);

  return (
    <div className="space-y-4" data-testid="wallet-tiers-editor">
      {source === 'appsettings' ? (
        <InlineAlert variant="info" title="Showing appsettings fallback">
          No DB-backed tiers exist yet — these rows reflect the current{' '}
          <code className="rounded bg-white/60 px-1 py-0.5 font-mono text-xs">Billing__Wallet__TopUpTiers</code>{' '}
          values. Save to persist them in the database (the UI then becomes the source of truth).
        </InlineAlert>
      ) : null}

      {!canWrite ? (
        <InlineAlert variant="info" title="Read-only access">
          You can review wallet top-up tiers, but saving changes requires Billing write permission.
        </InlineAlert>
      ) : null}

      {feedback ? (
        <InlineAlert
          variant={feedback.tone === 'success' ? 'success' : 'error'}
          title={feedback.tone === 'success' ? 'Saved' : 'Save failed'}
        >
          {feedback.message}
        </InlineAlert>
      ) : null}

      {duplicateAmounts.size > 0 ? (
        <InlineAlert variant="error" title="Duplicate amounts">
          The amount(s) {[...duplicateAmounts].join(', ')} appear more than once. Each tier must have a unique amount.
        </InlineAlert>
      ) : null}

      {setLevelError ? (
        <InlineAlert variant="error" title="Tier set incomplete">
          {setLevelError}
        </InlineAlert>
      ) : null}

      {ascendingWarning ? (
        <div data-testid="wallet-tier-ascending-warning">
          <InlineAlert variant="warning" title="Tiers not ascending">
            {ascendingWarning}
          </InlineAlert>
        </div>
      ) : null}

      <Card className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-background-light">
              <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-muted">
                <th scope="col" className="px-3 py-3">Amount</th>
                <th scope="col" className="px-3 py-3">Currency</th>
                <th scope="col" className="px-3 py-3">Credits</th>
                <th scope="col" className="px-3 py-3">Bonus</th>
                <th scope="col" className="px-3 py-3">Label</th>
                <th scope="col" className="px-3 py-3">Order</th>
                <th scope="col" className="px-3 py-3">Popular</th>
                <th scope="col" className="px-3 py-3">Active</th>
                <th scope="col" className="px-3 py-3 sr-only">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border bg-white">
              {drafts.length === 0 ? (
                <tr>
                  <td colSpan={9} className="px-4 py-8 text-center text-muted">
                    No tiers configured. Add at least one active tier before saving.
                  </td>
                </tr>
              ) : (
                drafts.map((row, index) => {
                  const rowErrors = errorsByKey.get(row.key) ?? {};
                  const amountIsDup = duplicateAmounts.has(Number(row.amount));
                  return (
                    <tr key={row.key} data-testid="wallet-tier-row" className="align-top">
                      <td className="px-3 py-3">
                        <Input
                          id={fieldId(row, 'amount')}
                          aria-label={`Tier ${index + 1} amount`}
                          inputMode="decimal"
                          value={row.amount}
                          error={rowErrors.amount ?? (amountIsDup ? 'Duplicate' : undefined)}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { amount: e.target.value })}
                          className="w-24"
                        />
                      </td>
                      <td className="px-3 py-3">
                        <Input
                          id={fieldId(row, 'currency')}
                          aria-label={`Tier ${index + 1} currency`}
                          value={row.currency}
                          error={rowErrors.currency}
                          maxLength={3}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { currency: e.target.value.toUpperCase() })}
                          className="w-20 uppercase"
                        />
                      </td>
                      <td className="px-3 py-3">
                        <Input
                          id={fieldId(row, 'credits')}
                          aria-label={`Tier ${index + 1} credits`}
                          inputMode="numeric"
                          value={row.credits}
                          error={rowErrors.credits}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { credits: e.target.value })}
                          className="w-24"
                        />
                      </td>
                      <td className="px-3 py-3">
                        <Input
                          id={fieldId(row, 'bonus')}
                          aria-label={`Tier ${index + 1} bonus credits`}
                          inputMode="numeric"
                          value={row.bonus}
                          error={rowErrors.bonus}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { bonus: e.target.value })}
                          className="w-24"
                        />
                      </td>
                      <td className="px-3 py-3">
                        <Input
                          id={fieldId(row, 'label')}
                          aria-label={`Tier ${index + 1} label`}
                          value={row.label}
                          maxLength={80}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { label: e.target.value })}
                          className="w-40"
                        />
                      </td>
                      <td className="px-3 py-3">
                        <Input
                          id={fieldId(row, 'display-order')}
                          aria-label={`Tier ${index + 1} display order`}
                          inputMode="numeric"
                          value={row.displayOrder}
                          error={rowErrors.displayOrder}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { displayOrder: e.target.value })}
                          className="w-20"
                        />
                      </td>
                      <td className="px-3 py-3">
                        <input
                          type="checkbox"
                          aria-label={`Tier ${index + 1} popular`}
                          checked={row.isPopular}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { isPopular: e.target.checked })}
                          className="h-4 w-4 rounded border-gray-300 text-primary focus:ring-primary"
                        />
                        {row.isPopular ? <Badge variant="info" className="ml-2">Popular</Badge> : null}
                      </td>
                      <td className="px-3 py-3">
                        <input
                          type="checkbox"
                          aria-label={`Tier ${index + 1} active`}
                          checked={row.isActive}
                          disabled={!canWrite}
                          onChange={(e) => updateRow(row.key, { isActive: e.target.checked })}
                          className="h-4 w-4 rounded border-gray-300 text-primary focus:ring-primary"
                        />
                      </td>
                      <td className="px-3 py-3 text-right">
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={`Remove tier ${row.amount}`}
                          disabled={!canWrite}
                          onClick={() => requestRemoveRow(row.key)}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <div className="flex flex-wrap items-center gap-3">
        <Button variant="secondary" onClick={addRow} type="button" disabled={!canWrite}>
          <Plus className="mr-1 h-4 w-4" /> Add tier
        </Button>
        <Button
          variant="primary"
          onClick={handleSave}
          disabled={!canWrite || saving || hasErrors}
          data-testid="wallet-tiers-save"
        >
          <Save className="mr-1 h-4 w-4" /> {saving ? 'Saving…' : 'Save all tiers'}
        </Button>
      </div>

      <BillingConfirmDialog
        open={pendingRemoveKey !== null}
        title="Delete wallet tier?"
        description="This wallet top-up tier will be removed from the next save. Existing wallet top-ups already linked to this tier will remain unaffected (the tier ID stays referenced server-side), but no new top-ups can be created at this amount."
        confirmLabel="Remove tier"
        variant="danger"
        onConfirm={confirmRemoveRow}
        onCancel={cancelRemoveRow}
      />
    </div>
  );
}
