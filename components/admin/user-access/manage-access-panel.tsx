'use client';

import { useEffect, useState } from 'react';
import { Stethoscope } from 'lucide-react';
import { Input } from '@/components/ui/form-controls';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { adminListMaterialFolders, type MaterialFolderDto } from '@/lib/materials-api';
import { readErrorMessage } from '@/lib/read-error-message';
import type { AdminBillingAddOn, AdminBillingPlan } from '@/lib/types/admin';
import {
  restoreUserPackage,
  suspendUserPackage,
  type UserAccessSubscriptionRow,
} from '@/lib/api/user-access-packages';
import {
  fetchAdminAddons,
  fetchAdminBillingPlans,
  fetchAdminRecallSetTags,
  isModuleEnabled,
  type RecallSetTagDto,
  type UserAccess,
} from '@/lib/user-access';
import { PackageList } from './package-list';
import { AddonPicker } from './addon-picker';
import { ModuleToggles } from './module-toggles';
import { FolderScopePicker } from './folder-scope-picker';
import { RecallSetPicker } from './recall-set-picker';

interface ManageAccessPanelProps {
  /** Required for suspend/restore, which act on already-persisted packages. */
  userId?: string;
  value: UserAccess;
  onChange: (next: UserAccess) => void;
  /** The learner's registered profession — every access decision keys off it, so
   *  it is shown here and drives the plan mismatch warning. Pass `undefined` (not
   *  an empty string) when the caller does not know it: blank means "none set". */
  learnerProfessionId?: string | null;
  learnerProfessionLabel?: string | null;
  plans?: AdminBillingPlan[];
  addons?: AdminBillingAddOn[];
  recallSets?: RecallSetTagDto[];
  folderTree?: MaterialFolderDto[];
  disabled?: boolean;
}

/**
 * Fully controlled access editor: packages, add-ons, module toggles, and
 * folder/recall-set scope, all bound to a single `UserAccess` value. Grants and
 * scope changes are drafts — the caller (Add User modal or the user detail page)
 * persists them via grantUserPackage / grantUserAddon / putUserAccessScope on
 * submit or save.
 *
 * Suspend/restore are the exception: they are reversible state transitions on a
 * package that already exists server-side, so they are applied immediately and
 * merged back into the draft rather than queued for save.
 *
 * Picker option lists (plans/add-ons/recall sets/folder tree) can be passed
 * in by the caller; any that are omitted are fetched internally.
 */
export function ManageAccessPanel({
  userId,
  value,
  onChange,
  learnerProfessionId,
  learnerProfessionLabel,
  plans: plansProp,
  addons: addonsProp,
  recallSets: recallSetsProp,
  folderTree: folderTreeProp,
  disabled,
}: ManageAccessPanelProps) {
  const [plans, setPlans] = useState<AdminBillingPlan[]>(plansProp ?? []);
  const [addons, setAddons] = useState<AdminBillingAddOn[]>(addonsProp ?? []);
  const [recallSets, setRecallSets] = useState<RecallSetTagDto[]>(recallSetsProp ?? []);
  const [folderTree, setFolderTree] = useState<MaterialFolderDto[]>(folderTreeProp ?? []);
  const [isLoadingOptions, setIsLoadingOptions] = useState<boolean>(
    !plansProp || !addonsProp || !recallSetsProp || !folderTreeProp,
  );
  const [busySubscriptionId, setBusySubscriptionId] = useState<string | null>(null);
  const [packageError, setPackageError] = useState<string | null>(null);

  const needsPlans = !plansProp;
  const needsAddons = !addonsProp;
  const needsRecallSets = !recallSetsProp;
  const needsFolderTree = !folderTreeProp;

  useEffect(() => {
    if (!needsPlans && !needsAddons && !needsRecallSets && !needsFolderTree) {
      return;
    }

    let cancelled = false;

    async function loadMissingOptions() {
      setIsLoadingOptions(true);
      try {
        const [plansResult, addonsResult, recallSetsResult, folderTreeResult] = await Promise.all([
          needsPlans ? fetchAdminBillingPlans() : Promise.resolve(plansProp ?? []),
          needsAddons ? fetchAdminAddons() : Promise.resolve(addonsProp ?? []),
          needsRecallSets ? fetchAdminRecallSetTags() : Promise.resolve(recallSetsProp ?? []),
          needsFolderTree ? adminListMaterialFolders() : Promise.resolve(folderTreeProp ?? []),
        ]);
        if (cancelled) return;
        setPlans(plansResult);
        setAddons(addonsResult);
        setRecallSets(recallSetsResult);
        setFolderTree(folderTreeResult);
      } catch (error) {
        console.error('Failed to load access picker options', error);
      } finally {
        if (!cancelled) setIsLoadingOptions(false);
      }
    }

    void loadMissingOptions();
    return () => {
      cancelled = true;
    };
    // Only the *presence* of each externally supplied option list should
    // retrigger a fetch, not its identity (arrays are recreated on every
    // parent render), otherwise this effect would refetch in a loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [needsPlans, needsAddons, needsRecallSets, needsFolderTree]);

  const materialsEnabled = isModuleEnabled(value.moduleOverrides, 'MaterialsLibrary');
  const recallsEnabled = isModuleEnabled(value.moduleOverrides, 'Recalls');
  const professionDisplay = learnerProfessionLabel?.trim() || learnerProfessionId?.trim() || null;

  /**
   * Re-reads the changed package from the server response but keeps the local
   * draft as the source of truth for membership, so an unsaved grant or removal
   * elsewhere in the panel survives a suspend/restore.
   */
  async function applyPackageTransition(
    subscriptionId: string,
    transition: (userId: string, subscriptionId: string) => Promise<UserAccess>,
  ) {
    if (!userId) return;
    setBusySubscriptionId(subscriptionId);
    setPackageError(null);
    try {
      const saved = await transition(userId, subscriptionId);
      const savedById = new Map(saved.subscriptions.map((sub) => [sub.id, sub]));
      onChange({
        ...value,
        subscriptions: value.subscriptions.map((sub) =>
          sub.isPending ? sub : savedById.get(sub.id) ?? sub,
        ),
      });
    } catch (error) {
      console.error('Failed to change package state', error);
      setPackageError(readErrorMessage(error, 'Unable to update this package.'));
    } finally {
      setBusySubscriptionId(null);
    }
  }

  return (
    <div className="space-y-6">
      {learnerProfessionId !== undefined ? (
        <div className="flex items-center gap-3 rounded-2xl border border-border bg-background-light px-4 py-3">
          <Stethoscope className="h-5 w-5 shrink-0 text-primary" aria-hidden />
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Registered profession</p>
            <p className="text-sm font-semibold text-navy">{professionDisplay ?? 'Not set'}</p>
          </div>
        </div>
      ) : null}

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Packages</h3>
        {packageError ? (
          // Keyed on the message: InlineAlert latches its own dismissed state, so a
          // fresh error after a dismiss needs a fresh instance to show at all.
          <InlineAlert key={packageError} variant="error" dismissible>
            {packageError}
          </InlineAlert>
        ) : null}
        <PackageList
          plans={plans}
          subscriptions={value.subscriptions as UserAccessSubscriptionRow[]}
          onChange={(subscriptions) => onChange({ ...value, subscriptions })}
          learnerProfessionId={learnerProfessionId}
          onSuspend={userId ? (id) => applyPackageTransition(id, suspendUserPackage) : undefined}
          onRestore={userId ? (id) => applyPackageTransition(id, restoreUserPackage) : undefined}
          busySubscriptionId={busySubscriptionId}
          disabled={disabled || isLoadingOptions}
        />
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Add-ons</h3>
        <AddonPicker
          addons={addons}
          subscriptions={value.subscriptions}
          selected={value.addOns}
          onChange={(addOns) => onChange({ ...value, addOns })}
          disabled={disabled || isLoadingOptions}
        />
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Module access</h3>
        <ModuleToggles
          overrides={value.moduleOverrides}
          onChange={(moduleOverrides) => onChange({ ...value, moduleOverrides })}
          disabled={disabled}
        />
      </section>

      {materialsEnabled ? (
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-navy">Materials Library scope</h3>
          <FolderScopePicker
            folderTree={folderTree}
            selectedIds={value.materialFolderIds}
            onChange={(materialFolderIds) => onChange({ ...value, materialFolderIds })}
            disabled={disabled || isLoadingOptions}
          />
        </section>
      ) : null}

      {recallsEnabled ? (
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-navy">Recall sets scope</h3>
          <RecallSetPicker
            recallSets={recallSets}
            selectedCodes={value.recallSetCodes}
            onChange={(recallSetCodes) => onChange({ ...value, recallSetCodes })}
            disabled={disabled || isLoadingOptions}
          />
        </section>
      ) : null}

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Master access expiry</h3>
        <div className="flex flex-wrap items-end gap-3">
          <Input
            label="Access expires"
            type="date"
            value={value.accessExpiresAt ? value.accessExpiresAt.slice(0, 10) : ''}
            onChange={(event) =>
              onChange({
                ...value,
                accessExpiresAt: event.target.value ? new Date(event.target.value).toISOString() : null,
              })
            }
            disabled={disabled}
            className="max-w-[220px]"
          />
          {value.accessExpiresAt ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => onChange({ ...value, accessExpiresAt: null })}
              disabled={disabled}
            >
              Clear expiry
            </Button>
          ) : null}
        </div>
      </section>
    </div>
  );
}
