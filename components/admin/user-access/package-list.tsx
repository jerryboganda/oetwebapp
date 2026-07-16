'use client';

import { useState } from 'react';
import { PauseCircle, PlayCircle, Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Input, Select, Checkbox } from '@/components/ui/form-controls';
import type { AdminBillingPlan } from '@/lib/types/admin';
import {
  isProfessionMismatch,
  planAccessDurationDays,
  type UserAccessSubscriptionRow,
} from '@/lib/api/user-access-packages';

interface PackageListProps {
  plans: AdminBillingPlan[];
  subscriptions: UserAccessSubscriptionRow[];
  onChange: (next: UserAccessSubscriptionRow[]) => void;
  /** The learner's registered profession id. `undefined` = unknown to this caller,
   *  which disables the mismatch check (a blank string is a real mismatch). */
  learnerProfessionId?: string | null;
  onSuspend?: (subscriptionId: string) => void | Promise<void>;
  onRestore?: (subscriptionId: string) => void | Promise<void>;
  busySubscriptionId?: string | null;
  disabled?: boolean;
}

function makeLocalId(): string {
  return `pending-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function toDateInput(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function addDays(dateInput: string, days: number): string {
  const base = new Date(`${dateInput}T00:00:00.000Z`);
  if (Number.isNaN(base.getTime())) return '';
  base.setUTCDate(base.getUTCDate() + days);
  return toDateInput(base);
}

function toIso(dateInput: string): string | null {
  return dateInput ? new Date(`${dateInput}T00:00:00.000Z`).toISOString() : null;
}

function formatDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toLocaleDateString();
}

/**
 * Add-a-package form + current package list. New rows are local drafts
 * (`isPending: true`) until the caller persists them via `grantUserPackage`
 * on submit/save — this component never grants packages itself.
 *
 * Suspend/restore/remove act on already-persisted rows, so they are delegated
 * to the caller through `onSuspend` / `onRestore` (which hit the API
 * immediately) and `onChange` (remove, which the caller flushes on save).
 */
export function PackageList({
  plans,
  subscriptions,
  onChange,
  learnerProfessionId,
  onSuspend,
  onRestore,
  busySubscriptionId,
  disabled,
}: PackageListProps) {
  const [planCode, setPlanCode] = useState('');
  const [startsAt, setStartsAt] = useState(() => toDateInput(new Date()));
  const [expiryOverride, setExpiryOverride] = useState('');
  const [makePrimary, setMakePrimary] = useState(subscriptions.length === 0);
  const [grantIncludedCredits, setGrantIncludedCredits] = useState(true);
  const [overrideProfession, setOverrideProfession] = useState(false);
  const [confirmRemoveId, setConfirmRemoveId] = useState<string | null>(null);

  const planOptions = plans.map((plan) => ({
    value: plan.code ?? plan.id,
    label: plan.price ? `${plan.name} (${plan.currency ?? ''}${plan.price})` : plan.name,
  }));

  const selectedPlan = plans.find((plan) => (plan.code ?? plan.id) === planCode);
  const accessDurationDays = planAccessDurationDays(selectedPlan);
  const defaultExpiry = startsAt ? addDays(startsAt, accessDurationDays) : '';
  const effectiveExpiry = expiryOverride || defaultExpiry;
  const isExpiryOverridden = Boolean(expiryOverride) && expiryOverride !== defaultExpiry;
  const professionMismatch = isProfessionMismatch(selectedPlan, learnerProfessionId);
  const blockedByProfession = professionMismatch && !overrideProfession;

  function resetForm() {
    setPlanCode('');
    setStartsAt(toDateInput(new Date()));
    setExpiryOverride('');
    setMakePrimary(false);
    setGrantIncludedCredits(true);
    setOverrideProfession(false);
  }

  function handleAdd() {
    if (!planCode || blockedByProfession) return;
    const draft: UserAccessSubscriptionRow = {
      id: makeLocalId(),
      planCode,
      planName: selectedPlan?.name ?? planCode,
      status: 'pending',
      startsAt: toIso(startsAt),
      expiresAt: toIso(effectiveExpiry),
      isPrimary: makePrimary,
      isPending: true,
      grantIncludedCredits,
      overrideProfessionMismatch: professionMismatch && overrideProfession,
    };
    const next = makePrimary
      ? subscriptions.map((sub) => ({ ...sub, isPrimary: false })).concat(draft)
      : subscriptions.concat(draft);
    onChange(next);
    resetForm();
  }

  function handleRemove(id: string) {
    setConfirmRemoveId(null);
    onChange(subscriptions.filter((sub) => sub.id !== id));
  }

  return (
    <div className="space-y-3">
      <div className="space-y-3 rounded-2xl border border-dashed border-border p-3">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select
            label="Plan"
            value={planCode}
            onChange={(event) => {
              setPlanCode(event.target.value);
              setOverrideProfession(false);
            }}
            options={[{ value: '', label: 'Select a plan...' }, ...planOptions]}
            disabled={disabled}
          />
          <Input
            label="Start date"
            type="date"
            value={startsAt}
            onChange={(event) => setStartsAt(event.target.value)}
            hint="Access begins on this date."
            disabled={disabled}
          />
          <Input
            label="Expiry date"
            type="date"
            value={effectiveExpiry}
            onChange={(event) => setExpiryOverride(event.target.value)}
            hint={`Defaults to ${accessDurationDays} days after the start date${
              isExpiryOverridden ? ' — overridden' : ''
            }.`}
            disabled={disabled || !startsAt}
          />
          {isExpiryOverridden ? (
            <div className="flex items-end">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setExpiryOverride('')}
                disabled={disabled}
              >
                Use default ({accessDurationDays} days)
              </Button>
            </div>
          ) : null}
        </div>

        {professionMismatch ? (
          <InlineAlert variant="warning" title="Profession mismatch">
            <p>
              This package is for <strong>{selectedPlan?.profession}</strong>, but the learner is registered as{' '}
              <strong>{learnerProfessionId?.trim() || 'no profession'}</strong>. Content for this package is
              matched to the learner&apos;s registered profession, so they may see nothing.
            </p>
            <Checkbox
              label="Grant anyway (override the profession check)"
              className="mt-3 bg-transparent"
              checked={overrideProfession}
              onChange={(event) => setOverrideProfession(event.target.checked)}
              disabled={disabled}
            />
          </InlineAlert>
        ) : null}

        <div className="flex flex-wrap gap-4">
          <Checkbox
            label="Make primary"
            checked={makePrimary}
            onChange={(event) => setMakePrimary(event.target.checked)}
            disabled={disabled}
          />
          <Checkbox
            label="Grant included credits"
            checked={grantIncludedCredits}
            onChange={(event) => setGrantIncludedCredits(event.target.checked)}
            disabled={disabled}
          />
        </div>
        <div className="flex justify-end">
          <Button
            type="button"
            size="sm"
            onClick={handleAdd}
            disabled={disabled || !planCode || blockedByProfession}
          >
            Add package
          </Button>
        </div>
      </div>

      {subscriptions.length === 0 ? (
        <p className="text-sm text-muted">No packages assigned yet.</p>
      ) : (
        <ul className="divide-y divide-border rounded-2xl border border-border">
          {subscriptions.map((sub) => {
            const isSuspended = sub.status.toLowerCase() === 'suspended';
            const awaitingFulfilment = sub.fulfilmentStatus === 'pending_manual';
            const isBusy = busySubscriptionId === sub.id;
            const startedLabel = formatDate(sub.startsAt ?? sub.startedAt);
            const expiresLabel = formatDate(sub.expiresAt);
            return (
              <li key={sub.id} className="flex flex-wrap items-center justify-between gap-3 px-3 py-2">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 truncate text-sm font-medium text-navy">
                    {sub.planName}
                    {sub.isPrimary ? <Badge variant="primary">Primary</Badge> : null}
                    {sub.isPending ? <Badge variant="warning">Not saved</Badge> : null}
                    {isSuspended ? <Badge variant="danger">Suspended</Badge> : null}
                    {awaitingFulfilment ? <Badge variant="warning">Awaiting fulfilment</Badge> : null}
                  </p>
                  <p className="text-xs text-muted">
                    Status: {sub.status}
                    {startedLabel ? ` · Starts ${startedLabel}` : ''}
                    {expiresLabel ? ` · Expires ${expiresLabel}` : ''}
                  </p>
                  {awaitingFulfilment ? (
                    <p className="text-xs text-amber-700 dark:text-amber-300">
                      Manually delivered — stays inactive until it is marked fulfilled.
                    </p>
                  ) : null}
                </div>
                <div className="flex items-center gap-1">
                  {!sub.isPending && isSuspended && onRestore ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void onRestore(sub.id)}
                      disabled={disabled || isBusy}
                      loading={isBusy}
                    >
                      <PlayCircle className="h-4 w-4" />
                      Restore
                    </Button>
                  ) : null}
                  {!sub.isPending && !isSuspended && onSuspend ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void onSuspend(sub.id)}
                      disabled={disabled || isBusy}
                      loading={isBusy}
                    >
                      <PauseCircle className="h-4 w-4" />
                      Suspend
                    </Button>
                  ) : null}
                  {confirmRemoveId === sub.id ? (
                    <>
                      <Button
                        type="button"
                        size="sm"
                        variant="destructive"
                        onClick={() => handleRemove(sub.id)}
                        disabled={disabled}
                      >
                        Confirm remove
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => setConfirmRemoveId(null)}
                        disabled={disabled}
                      >
                        Cancel
                      </Button>
                    </>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      className="text-destructive"
                      onClick={() => (sub.isPending ? handleRemove(sub.id) : setConfirmRemoveId(sub.id))}
                      disabled={disabled || isBusy}
                      aria-label={`Remove ${sub.planName}`}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
