'use client';

import { useState } from 'react';
import { Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select, Checkbox } from '@/components/ui/form-controls';
import type { AdminBillingPlan } from '@/lib/types/admin';
import type { UserAccessSubscription } from '@/lib/user-access';

interface PackageListProps {
  plans: AdminBillingPlan[];
  subscriptions: UserAccessSubscription[];
  onChange: (next: UserAccessSubscription[]) => void;
  disabled?: boolean;
}

function makeLocalId(): string {
  return `pending-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

/**
 * Add-a-package form + current package list. New rows are local drafts
 * (`isPending: true`) until the caller persists them via `grantUserPackage`
 * on submit/save — this component never calls the API directly.
 */
export function PackageList({ plans, subscriptions, onChange, disabled }: PackageListProps) {
  const [planCode, setPlanCode] = useState('');
  const [expiresAt, setExpiresAt] = useState('');
  const [makePrimary, setMakePrimary] = useState(subscriptions.length === 0);
  const [grantIncludedCredits, setGrantIncludedCredits] = useState(true);

  const planOptions = plans.map((plan) => ({
    value: plan.code ?? plan.id,
    label: plan.price ? `${plan.name} (${plan.currency ?? ''}${plan.price})` : plan.name,
  }));

  function handleAdd() {
    if (!planCode) return;
    const plan = plans.find((p) => (p.code ?? p.id) === planCode);
    const draft: UserAccessSubscription = {
      id: makeLocalId(),
      planCode,
      planName: plan?.name ?? planCode,
      status: 'pending',
      expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
      isPrimary: makePrimary,
      isPending: true,
      grantIncludedCredits,
    };
    const next = makePrimary
      ? subscriptions.map((sub) => ({ ...sub, isPrimary: false })).concat(draft)
      : subscriptions.concat(draft);
    onChange(next);
    setPlanCode('');
    setExpiresAt('');
    setMakePrimary(false);
    setGrantIncludedCredits(true);
  }

  function handleRemove(id: string) {
    onChange(subscriptions.filter((sub) => sub.id !== id));
  }

  return (
    <div className="space-y-3">
      <div className="space-y-3 rounded-2xl border border-dashed border-border p-3">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select
            label="Plan"
            value={planCode}
            onChange={(event) => setPlanCode(event.target.value)}
            options={[{ value: '', label: 'Select a plan...' }, ...planOptions]}
            disabled={disabled}
          />
          <Input
            label="Expiry (optional)"
            type="date"
            value={expiresAt}
            onChange={(event) => setExpiresAt(event.target.value)}
            disabled={disabled}
          />
        </div>
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
          <Button type="button" size="sm" onClick={handleAdd} disabled={disabled || !planCode}>
            Add package
          </Button>
        </div>
      </div>

      {subscriptions.length === 0 ? (
        <p className="text-sm text-muted">No packages assigned yet.</p>
      ) : (
        <ul className="divide-y divide-border rounded-2xl border border-border">
          {subscriptions.map((sub) => (
            <li key={sub.id} className="flex items-center justify-between gap-3 px-3 py-2">
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 truncate text-sm font-medium text-navy">
                  {sub.planName}
                  {sub.isPrimary ? <Badge variant="primary">Primary</Badge> : null}
                  {sub.isPending ? <Badge variant="warning">Pending</Badge> : null}
                </p>
                <p className="text-xs text-muted">
                  Status: {sub.status}
                  {sub.expiresAt ? ` · Expires ${new Date(sub.expiresAt).toLocaleDateString()}` : ''}
                </p>
              </div>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                className="text-destructive"
                onClick={() => handleRemove(sub.id)}
                disabled={disabled}
                aria-label={`Remove ${sub.planName}`}
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
