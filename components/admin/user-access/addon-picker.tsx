'use client';

import { useState } from 'react';
import { Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Select } from '@/components/ui/form-controls';
import type { AdminBillingAddOn } from '@/lib/types/admin';
import type { UserAccessAddOn, UserAccessSubscription } from '@/lib/user-access';

interface AddonPickerProps {
  addons: AdminBillingAddOn[];
  subscriptions: UserAccessSubscription[];
  selected: UserAccessAddOn[];
  onChange: (next: UserAccessAddOn[]) => void;
  disabled?: boolean;
}

/**
 * Add-on multiselect. New rows are local drafts (`isPending: true`) until the
 * caller persists them via `grantUserAddon` on submit/save. Add-ons can only
 * be attached to already-committed (non-pending) packages, since a locally
 * drafted package has no real subscription id yet.
 */
export function AddonPicker({ addons, subscriptions, selected, onChange, disabled }: AddonPickerProps) {
  const [addonCode, setAddonCode] = useState('');
  const [subscriptionId, setSubscriptionId] = useState('');

  const committedSubscriptions = subscriptions.filter((sub) => !sub.isPending);
  const addonOptions = addons.map((addon) => ({ value: addon.code, label: addon.name }));
  const subscriptionOptions = committedSubscriptions.map((sub) => ({ value: sub.id, label: sub.planName }));

  function handleAdd() {
    if (!addonCode) return;
    const alreadySelected = selected.some(
      (addon) => addon.code === addonCode && (addon.subscriptionId ?? '') === subscriptionId,
    );
    if (alreadySelected) return;
    onChange([...selected, { code: addonCode, subscriptionId: subscriptionId || undefined, isPending: true }]);
    setAddonCode('');
    setSubscriptionId('');
  }

  function handleRemove(index: number) {
    onChange(selected.filter((_, i) => i !== index));
  }

  function addonLabel(code: string) {
    return addons.find((addon) => addon.code === code)?.name ?? code;
  }

  function subscriptionLabel(id?: string) {
    if (!id) return 'No specific subscription';
    return subscriptions.find((sub) => sub.id === id)?.planName ?? id;
  }

  return (
    <div className="space-y-3">
      <div className="space-y-3 rounded-2xl border border-dashed border-border p-3">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Select
            label="Add-on"
            value={addonCode}
            onChange={(event) => setAddonCode(event.target.value)}
            options={[{ value: '', label: 'Select an add-on...' }, ...addonOptions]}
            disabled={disabled}
          />
          <Select
            label="Attach to subscription"
            value={subscriptionId}
            onChange={(event) => setSubscriptionId(event.target.value)}
            options={[{ value: '', label: 'No specific subscription' }, ...subscriptionOptions]}
            disabled={disabled || committedSubscriptions.length === 0}
          />
        </div>
        <div className="flex justify-end">
          <Button type="button" size="sm" onClick={handleAdd} disabled={disabled || !addonCode}>
            Add add-on
          </Button>
        </div>
      </div>

      {selected.length === 0 ? (
        <p className="text-sm text-muted">No add-ons granted yet.</p>
      ) : (
        <ul className="divide-y divide-border rounded-2xl border border-border">
          {selected.map((addOn, index) => (
            <li key={`${addOn.code}-${addOn.subscriptionId ?? 'none'}-${index}`} className="flex items-center justify-between gap-3 px-3 py-2">
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 truncate text-sm font-medium text-navy">
                  {addonLabel(addOn.code)}
                  {addOn.isPending ? <Badge variant="warning">Pending</Badge> : null}
                </p>
                <p className="text-xs text-muted">{subscriptionLabel(addOn.subscriptionId)}</p>
              </div>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                className="text-destructive"
                onClick={() => handleRemove(index)}
                disabled={disabled}
                aria-label={`Remove ${addonLabel(addOn.code)}`}
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
