'use client';

import { useEffect, useState } from 'react';
import { Input } from '@/components/ui/form-controls';
import { Button } from '@/components/admin/ui/button';
import { adminListMaterialFolders, type MaterialFolderDto } from '@/lib/materials-api';
import type { AdminBillingAddOn, AdminBillingPlan } from '@/lib/types/admin';
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
  /** Not required for local edits — kept for callers that want to key/cache by user. */
  userId?: string;
  value: UserAccess;
  onChange: (next: UserAccess) => void;
  plans?: AdminBillingPlan[];
  addons?: AdminBillingAddOn[];
  recallSets?: RecallSetTagDto[];
  folderTree?: MaterialFolderDto[];
  disabled?: boolean;
}

/**
 * Fully controlled access editor: packages, add-ons, module toggles, and
 * folder/recall-set scope, all bound to a single `UserAccess` value. Never
 * calls the mutation endpoints itself — the caller (Add User modal or the
 * user detail page) is responsible for persisting the draft via
 * grantUserPackage / grantUserAddon / putUserAccessScope on submit or save.
 *
 * Picker option lists (plans/add-ons/recall sets/folder tree) can be passed
 * in by the caller; any that are omitted are fetched internally.
 */
export function ManageAccessPanel({
  value,
  onChange,
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

  return (
    <div className="space-y-6">
      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Packages</h3>
        <PackageList
          plans={plans}
          subscriptions={value.subscriptions}
          onChange={(subscriptions) => onChange({ ...value, subscriptions })}
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
