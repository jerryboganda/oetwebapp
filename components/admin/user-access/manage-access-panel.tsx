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
  setPrimaryUserPackage,
  suspendUserPackage,
  updateUserPackageDates,
  type UserAccessSubscriptionRow,
} from '@/lib/api/user-access-packages';
import {
  fetchAdminAddons,
  fetchAdminBillingPlans,
  fetchAdminRecallSetTags,
  fetchAllocatableVideos,
  hasEligiblePackage,
  isModuleEnabled,
  type AllocatableVideo,
  type RecallSetTagDto,
  type UserAccess,
} from '@/lib/user-access';
import { PackageList } from './package-list';
import { AddonPicker } from './addon-picker';
import { ModuleToggles } from './module-toggles';
import { FolderScopePicker } from './folder-scope-picker';
import { VideoScopePicker } from './video-scope-picker';
import { RecallSetPicker } from './recall-set-picker';

interface ManageAccessPanelProps {
  /** Required for package state/primary actions, which act on persisted packages. */
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
  videos?: AllocatableVideo[];
  disabled?: boolean;
  /** Called after an immediate server mutation (suspend/restore/primary/date) so the parent can refresh derived data like AI credits. */
  onImmediateMutation?: (nextAccess: UserAccess) => void;
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
  videos: videosProp,
  disabled,
  onImmediateMutation,
}: ManageAccessPanelProps) {
  const [plans, setPlans] = useState<AdminBillingPlan[]>(plansProp ?? []);
  const [addons, setAddons] = useState<AdminBillingAddOn[]>(addonsProp ?? []);
  const [recallSets, setRecallSets] = useState<RecallSetTagDto[]>(recallSetsProp ?? []);
  const [folderTree, setFolderTree] = useState<MaterialFolderDto[]>(folderTreeProp ?? []);
  const [videos, setVideos] = useState<AllocatableVideo[]>(videosProp ?? []);
  const [isLoadingOptions, setIsLoadingOptions] = useState<boolean>(
    !plansProp || !addonsProp || !recallSetsProp || !folderTreeProp || !videosProp,
  );
  const [busySubscriptionId, setBusySubscriptionId] = useState<string | null>(null);
  const [packageError, setPackageError] = useState<string | null>(null);

  const needsPlans = !plansProp;
  const needsAddons = !addonsProp;
  const needsRecallSets = !recallSetsProp;
  const needsFolderTree = !folderTreeProp;
  const needsVideos = !videosProp;

  useEffect(() => {
    if (!needsPlans && !needsAddons && !needsRecallSets && !needsFolderTree && !needsVideos) {
      return;
    }

    let cancelled = false;

    async function loadMissingOptions() {
      setIsLoadingOptions(true);
      try {
        const [plansResult, addonsResult, recallSetsResult, folderTreeResult, videosResult] = await Promise.all([
          needsPlans ? fetchAdminBillingPlans() : Promise.resolve(plansProp ?? []),
          needsAddons ? fetchAdminAddons() : Promise.resolve(addonsProp ?? []),
          needsRecallSets ? fetchAdminRecallSetTags() : Promise.resolve(recallSetsProp ?? []),
          needsFolderTree ? adminListMaterialFolders() : Promise.resolve(folderTreeProp ?? []),
          needsVideos ? fetchAllocatableVideos() : Promise.resolve(videosProp ?? []),
        ]);
        if (cancelled) return;
        setPlans(plansResult);
        setAddons(addonsResult);
        setRecallSets(recallSetsResult);
        setFolderTree(folderTreeResult);
        setVideos(videosResult);
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
  }, [needsPlans, needsAddons, needsRecallSets, needsFolderTree, needsVideos]);

  const materialsEnabled = isModuleEnabled(value.moduleOverrides, 'MaterialsLibrary');
  const videosEnabled = isModuleEnabled(value.moduleOverrides, 'VideoLibrary');
  const recallsEnabled = isModuleEnabled(value.moduleOverrides, 'Recalls');
  const professionDisplay = learnerProfessionLabel?.trim() || learnerProfessionId?.trim() || null;

  // Module toggles and content scope (recall sets, folders, videos) only ever take
  // effect on top of an eligible package — with none, the backend fails low before
  // it even reads these overrides, so saving them here would silently do nothing.
  const learnerHasEligiblePackage = hasEligiblePackage(value.subscriptions);
  const hasAnyModuleGrant = value.moduleOverrides.some((override) => override.enabled)
    || value.recallSetCodes.length > 0
    || value.materialFolderIds.length > 0
    || value.videoIds.length > 0;
  const grantWouldBeNoOp = !learnerHasEligiblePackage && hasAnyModuleGrant;

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
      const nextAccess: UserAccess = {
        ...value,
        ...saved,
        subscriptions: value.subscriptions.map((sub) =>
          sub.isPending ? sub : savedById.get(sub.id) ?? sub,
        ),
        // Server is authoritative for master expiry and the persisted subscription list — keep local drafts only for membership.
        accessExpiresAt: saved.accessExpiresAt,
      };
      onChange(nextAccess);
      onImmediateMutation?.(nextAccess);
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
          masterAccessExpiresAt={value.accessExpiresAt}
          onSuspend={userId ? (id) => applyPackageTransition(id, suspendUserPackage) : undefined}
          onRestore={userId ? (id) => applyPackageTransition(id, restoreUserPackage) : undefined}
          onSetPrimary={userId ? (id) => applyPackageTransition(id, setPrimaryUserPackage) : undefined}
          onEditDates={
            userId
              ? (id, input) =>
                  applyPackageTransition(id, (uid, sid) => updateUserPackageDates(uid, sid, input))
              : undefined
          }
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

      {grantWouldBeNoOp ? (
        <InlineAlert variant="warning" title="No active package">
          This learner has no eligible package. Recalls, Materials, Videos, and Mocks access all require one —
          the module toggles and content scope below will have no effect until a package is added above (or
          reactivated if suspended/expired).
        </InlineAlert>
      ) : null}

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
          <h3 className="text-sm font-semibold text-navy">Materials Library content</h3>
          <p className="text-xs text-muted">
            Automatic by default: leave the optional restriction below empty and the learner receives every
            Materials Library folder their package grants.
          </p>
          <details className="rounded-xl border border-border bg-background-light px-3 py-2">
            <summary className="cursor-pointer text-sm font-medium text-navy">Optional manual restriction</summary>
            <p className="mt-2 text-xs text-muted">
              Select folders only when you deliberately need to limit this learner below their package allowance.
            </p>
            <div className="mt-3">
              <FolderScopePicker
                folderTree={folderTree}
                selectedIds={value.materialFolderIds}
                onChange={(materialFolderIds) => onChange({ ...value, materialFolderIds })}
                disabled={disabled || isLoadingOptions}
              />
            </div>
          </details>
        </section>
      ) : null}

      {videosEnabled ? (
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-navy">Video Library content</h3>
          <p className="text-xs text-muted">
            Automatic by default: leave the optional restriction below empty and the learner receives every
            video their package grants for their profession.
          </p>
          <details className="rounded-xl border border-border bg-background-light px-3 py-2">
            <summary className="cursor-pointer text-sm font-medium text-navy">
              Optional manual restriction
            </summary>
            <p className="mt-2 text-xs text-muted">
              Select sections or individual videos only when you deliberately need to limit this learner below
              their package allowance.
            </p>
            <div className="mt-3">
              <VideoScopePicker
                videos={videos}
                selectedIds={value.videoIds}
                onChange={(videoIds) => onChange({ ...value, videoIds })}
                disabled={disabled || isLoadingOptions}
              />
            </div>
          </details>
        </section>
      ) : null}

      {recallsEnabled ? (
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-navy">Recall content</h3>
          <p className="text-xs text-muted">
            Automatic by default: leave the optional restriction below empty and the learner receives every
            recall set their package grants.
          </p>
          <details className="rounded-xl border border-border bg-background-light px-3 py-2">
            <summary className="cursor-pointer text-sm font-medium text-navy">Optional manual restriction</summary>
            <p className="mt-2 text-xs text-muted">
              Select recall sets only when you deliberately need to limit this learner below their package allowance.
            </p>
            <div className="mt-3">
              <RecallSetPicker
                recallSets={recallSets}
                selectedCodes={value.recallSetCodes}
                onChange={(recallSetCodes) => onChange({ ...value, recallSetCodes })}
                disabled={disabled || isLoadingOptions}
              />
            </div>
          </details>
        </section>
      ) : null}

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Master access expiry</h3>
        <p className="text-xs text-muted">
          Global access cap: once set, the learner loses <strong>all</strong> access after this date —
          login and token checks enforce it, even when a package card above shows a later end date.
          Package mutations tighten it but never extend it. Clear it to hand control back to the
          per-package end dates.
        </p>
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
