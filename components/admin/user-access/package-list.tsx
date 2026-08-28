'use client';

import { useState } from 'react';
import { CalendarClock, PauseCircle, PlayCircle, Star, Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Input, Select, Checkbox } from '@/components/ui/form-controls';
import type { AdminBillingPlan } from '@/lib/types/admin';
import {
  isProfessionMismatch,
  planAccessDurationDays,
  type UpdateUserPackageDatesInput,
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
  onSetPrimary?: (subscriptionId: string) => void | Promise<void>;
  /** Absolute date override for a persisted package (PDF date-override): replaces
   *  Start/End wholesale, expires immediately on a past End, and syncs the
   *  course-gifted AI credit lots sourced from this package. */
  onEditDates?: (
    subscriptionId: string,
    input: UpdateUserPackageDatesInput,
  ) => void | Promise<void>;
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

/** ISO timestamp → `yyyy-mm-dd` for a date input; '' when absent/unparseable. */
function isoToDateInput(iso: string | null | undefined): string {
  if (!iso) return '';
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? '' : toDateInput(parsed);
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
  onSetPrimary,
  onEditDates,
  busySubscriptionId,
  disabled,
}: PackageListProps) {
  const [planCode, setPlanCode] = useState('');
  const [startsAt, setStartsAt] = useState(() => toDateInput(new Date()));
  const [expiryOverride, setExpiryOverride] = useState('');
  const [makePrimary, setMakePrimary] = useState(subscriptions.length === 0);
  const [overrideProfession, setOverrideProfession] = useState(false);
  const [confirmRemoveId, setConfirmRemoveId] = useState<string | null>(null);
  const [activeDraftId, setActiveDraftId] = useState<string | null>(null);
  const [editingDatesId, setEditingDatesId] = useState<string | null>(null);
  const [editStartsAt, setEditStartsAt] = useState('');
  const [editExpiresAt, setEditExpiresAt] = useState('');
  const [editClearExpiry, setEditClearExpiry] = useState(false);

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
  const addFormExpiryBeforeStart = Boolean(startsAt && effectiveExpiry && effectiveExpiry < startsAt);

  function syncDraft(
    code: string,
    starts: string,
    expiryOverrideVal: string,
    primaryVal: boolean,
    overrideProfVal: boolean,
  ) {
    if (!code) {
      if (activeDraftId) {
        onChange(subscriptions.filter((sub) => sub.id !== activeDraftId));
        setActiveDraftId(null);
      }
      return;
    }

    const selPlan = plans.find((p) => (p.code ?? p.id) === code);
    const mismatch = isProfessionMismatch(selPlan, learnerProfessionId);
    if (mismatch && !overrideProfVal) {
      if (activeDraftId) {
        onChange(subscriptions.filter((sub) => sub.id !== activeDraftId));
        setActiveDraftId(null);
      }
      return;
    }

    const idToUse = activeDraftId ?? makeLocalId();
    if (!activeDraftId) {
      setActiveDraftId(idToUse);
    }

    const durationDays = planAccessDurationDays(selPlan);
    const defExpiry = starts ? addDays(starts, durationDays) : '';
    const effExpiry = expiryOverrideVal || defExpiry;

    const draft: UserAccessSubscriptionRow = {
      id: idToUse,
      planCode: code,
      planName: selPlan?.name ?? code,
      status: 'pending',
      startsAt: toIso(starts),
      expiresAt: toIso(effExpiry),
      isPrimary: primaryVal,
      isPending: true,
      grantIncludedCredits: false,
      overrideProfessionMismatch: mismatch && overrideProfVal,
    };

    const withoutDraft = subscriptions.filter((sub) => sub.id !== idToUse);
    const next = primaryVal
      ? withoutDraft.map((sub) => ({ ...sub, isPrimary: false })).concat(draft)
      : withoutDraft.concat(draft);

    onChange(next);
  }

  function resetForm() {
    setPlanCode('');
    setStartsAt(toDateInput(new Date()));
    setExpiryOverride('');
    setMakePrimary(false);
    setOverrideProfession(false);
    setActiveDraftId(null);
  }

  function handleAdd() {
    if (!planCode || blockedByProfession) return;
    setActiveDraftId(null);
    resetForm();
  }

  function handleRemove(id: string) {
    setConfirmRemoveId(null);
    if (id === activeDraftId) {
      resetForm();
    } else {
      onChange(subscriptions.filter((sub) => sub.id !== id));
    }
  }

  function beginEditDates(sub: UserAccessSubscriptionRow) {
    setEditingDatesId(sub.id);
    setEditStartsAt(isoToDateInput(sub.startedAt ?? sub.startsAt));
    setEditExpiresAt(isoToDateInput(sub.expiresAt));
    setEditClearExpiry(!sub.expiresAt);
  }

  function resetEditDates() {
    setEditingDatesId(null);
    setEditStartsAt('');
    setEditExpiresAt('');
    setEditClearExpiry(false);
  }

  async function saveEditDates(id: string) {
    if (!onEditDates) return;
    try {
      await onEditDates(id, {
        startsAt: editStartsAt
          ? new Date(`${editStartsAt}T00:00:00.000Z`).toISOString()
          : null,
        expiresAt: editClearExpiry || !editExpiresAt
          ? null
          : new Date(`${editExpiresAt}T00:00:00.000Z`).toISOString(),
        clearExpiresAt: editClearExpiry,
      });
      resetEditDates();
    } catch {
      // Keep editor open so the admin can correct the input; the parent surfaces the error.
    }
  }

  return (
    <div className="space-y-3">
      <div className="space-y-3 rounded-2xl border border-dashed border-border p-3">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select
            id="add-package-plan"
            label="Plan"
            value={planCode}
            onChange={(event) => {
              const val = event.target.value;
              setPlanCode(val);
              setOverrideProfession(false);
              syncDraft(val, startsAt, expiryOverride, makePrimary, false);
            }}
            options={[{ value: '', label: 'Select a plan...' }, ...planOptions]}
            disabled={disabled}
          />
          <Input
            id="add-package-start-date"
            label="Start date"
            type="date"
            value={startsAt}
            onChange={(event) => {
              const val = event.target.value;
              setStartsAt(val);
              syncDraft(planCode, val, expiryOverride, makePrimary, overrideProfession);
            }}
            hint="Access begins on this date."
            disabled={disabled}
          />
          <Input
            id="add-package-expiry-date"
            label="Expiry date"
            type="date"
            value={effectiveExpiry}
            onChange={(event) => {
              const val = event.target.value;
              setExpiryOverride(val);
              syncDraft(planCode, startsAt, val, makePrimary, overrideProfession);
            }}
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
                onClick={() => {
                  setExpiryOverride('');
                  syncDraft(planCode, startsAt, '', makePrimary, overrideProfession);
                }}
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
              onChange={(event) => {
                const val = event.target.checked;
                setOverrideProfession(val);
                syncDraft(planCode, startsAt, expiryOverride, makePrimary, val);
              }}
              disabled={disabled}
            />
          </InlineAlert>
        ) : null}

        <div className="flex flex-wrap gap-4">
          <div className="space-y-1">
            <Checkbox
              label="Make primary"
              title="Primary controls the learner's summary and default plan metadata only; other active packages still contribute access."
              checked={makePrimary}
              onChange={(event) => {
                const val = event.target.checked;
                setMakePrimary(val);
                syncDraft(planCode, startsAt, expiryOverride, val, overrideProfession);
              }}
              disabled={disabled}
            />
            <p className="px-4 text-xs leading-5 text-muted">
              Primary controls the learner&apos;s summary/default plan. Other active packages still add access,
              credits, and expiry.
            </p>
          </div>
        </div>
        {(selectedPlan?.bundledAiCredits ?? 0) > 0 ? (
          <p className="text-sm text-admin-fg-strong">
            Includes {selectedPlan?.bundledAiCredits} gifted Shared AI credits, granted automatically when you add this package.
          </p>
        ) : null}
        {addFormExpiryBeforeStart ? (
          <InlineAlert variant="error">The end date cannot be before the start date.</InlineAlert>
        ) : null}
        <div className="flex justify-end">
          <Button
            type="button"
            size="sm"
            onClick={handleAdd}
            disabled={disabled || !planCode || blockedByProfession || addFormExpiryBeforeStart}
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
            const startedAtRaw = sub.startedAt ?? sub.startsAt ?? null;
            const startedOk = !startedAtRaw || new Date(startedAtRaw).getTime() <= Date.now();
            const notExpired = !sub.expiresAt || new Date(sub.expiresAt).getTime() > Date.now();
            const canSetPrimary = !sub.isPending
              && !sub.isPrimary
              && !isSuspended
              && ['active', 'trial', 'freezerequested'].includes(sub.status.toLowerCase())
              && startedOk
              && notExpired;
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
                  {canSetPrimary && onSetPrimary ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => void onSetPrimary(sub.id)}
                      disabled={disabled || isBusy}
                      loading={isBusy}
                      title="Use this package for the learner's summary and default plan metadata"
                    >
                      <Star className="h-4 w-4" />
                      Set primary
                    </Button>
                  ) : null}
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
                  {!sub.isPending && onEditDates ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() =>
                        editingDatesId === sub.id ? resetEditDates() : beginEditDates(sub)
                      }
                      disabled={disabled || isBusy}
                      title="Replace this package's start/end dates (PDF date override)"
                    >
                      <CalendarClock className="h-4 w-4" />
                      {editingDatesId === sub.id ? 'Close dates' : 'Edit dates'}
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
                {editingDatesId === sub.id && !sub.isPending ? (
                  <div className="w-full space-y-3 rounded-xl border border-border bg-background-light p-3">
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                      <Input
                        id={`edit-${sub.id}-start-date`}
                        label="Start date"
                        type="date"
                        value={editStartsAt}
                        onChange={(event) => setEditStartsAt(event.target.value)}
                        hint="Replaces the saved start date."
                        disabled={disabled || isBusy}
                      />
                      <Input
                        id={`edit-${sub.id}-expiry-date`}
                        label="Expiry date"
                        type="date"
                        value={editClearExpiry ? '' : editExpiresAt}
                        onChange={(event) => setEditExpiresAt(event.target.value)}
                        hint="Replaces the saved end date. A past date expires the package immediately."
                        disabled={disabled || isBusy || editClearExpiry}
                      />
                    </div>
                    <Checkbox
                      label="No expiry (access never ends)"
                      checked={editClearExpiry}
                      onChange={(event) => setEditClearExpiry(event.target.checked)}
                      disabled={disabled || isBusy}
                    />
                    {editStartsAt && !editClearExpiry && editExpiresAt && editExpiresAt < editStartsAt ? (
                      <InlineAlert variant="error">
                        The end date cannot be before the start date.
                      </InlineAlert>
                    ) : null}
                    <div className="flex items-center gap-2">
                      <Button
                        type="button"
                        size="sm"
                        onClick={() => void saveEditDates(sub.id)}
                        disabled={
                          disabled
                          || isBusy
                          || Boolean(
                            editStartsAt
                            && !editClearExpiry
                            && editExpiresAt
                            && editExpiresAt < editStartsAt,
                          )
                          || (!editStartsAt && !editClearExpiry && !editExpiresAt)
                        }
                        loading={isBusy}
                      >
                        Save dates
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={resetEditDates}
                        disabled={disabled || isBusy}
                      >
                        Cancel
                      </Button>
                    </div>
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
