'use client';

import { useEffect, useState } from 'react';
import { ChevronDown, ChevronUp, SlidersHorizontal } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Select, Checkbox } from '@/components/ui/form-controls';
import { adminListMaterialFolders, type MaterialFolderDto } from '@/lib/materials-api';
import { readErrorMessage } from '@/lib/read-error-message';
import type { AdminBillingPlan } from '@/lib/types/admin';
import { isProfessionMismatch, type GrantUserPackageInput } from '@/lib/api/user-access-packages';
import { grantUserPackage } from '@/lib/user-access';
import {
  fetchAdminBillingPlans,
  fetchAdminRecallSetTags,
  fetchAllocatableVideos,
  fetchUserAccess,
  putUserAccessScope,
  type AllocatableVideo,
  type RecallSetTagDto,
  type UserAccess,
} from '@/lib/user-access';
import { ACCESS_PRESETS, buildModuleOverrides, type AccessPreset } from '@/lib/user-access-presets';
import { RecallSetPicker } from './recall-set-picker';
import { FolderScopePicker } from './folder-scope-picker';
import { VideoScopePicker } from './video-scope-picker';

interface QuickGrantModalProps {
  userId: string;
  userLabel: string;
  /** `undefined` = unknown to this caller, which disables the profession-mismatch check. */
  learnerProfessionId?: string | null;
  open: boolean;
  onClose: () => void;
  /** Fired when the admin picks "Custom" instead of a preset — the caller decides what
   *  that means (expand the full Advanced panel in place, or navigate to it). */
  onCustom: () => void;
  onGranted: (access: UserAccess) => void;
}

/** True if a subscription still counts as "active enough" to unlock gated modules. */
function isEligibleSubscription(sub: { status: string; expiresAt: string | null }): boolean {
  const status = sub.status.toLowerCase();
  if (status === 'suspended' || status === 'pending' || status === 'cancelled') return false;
  if (sub.expiresAt && new Date(sub.expiresAt).getTime() <= Date.now()) return false;
  return true;
}

/**
 * One-click access presets (Recalls Only, Materials Only, Videos Only, Full
 * Access) layered on top of the full Access & Allocation panel. See
 * docs/superpowers/specs/2026-07-29-quick-grant-access-presets-design.md.
 */
export function QuickGrantModal({
  userId,
  userLabel,
  learnerProfessionId,
  open,
  onClose,
  onCustom,
  onGranted,
}: QuickGrantModalProps) {
  const [step, setStep] = useState<'pick' | 'confirm'>('pick');
  const [selectedPreset, setSelectedPreset] = useState<AccessPreset | null>(null);
  const [baseline, setBaseline] = useState<UserAccess | null>(null);
  const [plans, setPlans] = useState<AdminBillingPlan[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [planCode, setPlanCode] = useState('');
  const [overrideProfession, setOverrideProfession] = useState(false);
  const [scopeExpanded, setScopeExpanded] = useState(false);
  const [scopeOptionsLoading, setScopeOptionsLoading] = useState(false);
  const [recallSets, setRecallSets] = useState<RecallSetTagDto[]>([]);
  const [folderTree, setFolderTree] = useState<MaterialFolderDto[]>([]);
  const [videos, setVideos] = useState<AllocatableVideo[]>([]);
  const [scopeSelection, setScopeSelection] = useState<string[]>([]);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setStep('pick');
    setSelectedPreset(null);
    setPlanCode('');
    setOverrideProfession(false);
    setScopeExpanded(false);
    setScopeSelection([]);
    setError(null);

    let cancelled = false;
    setIsLoading(true);
    Promise.all([fetchUserAccess(userId), fetchAdminBillingPlans()])
      .then(([access, plansResult]) => {
        if (cancelled) return;
        setBaseline(access);
        setPlans(plansResult);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(readErrorMessage(err, 'Unable to load this learner’s current access.'));
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [open, userId]);

  const hasEligiblePackage = baseline?.subscriptions.some(isEligibleSubscription) ?? false;
  const selectedPlan = plans.find((plan) => (plan.code ?? plan.id) === planCode);
  const professionMismatch = isProfessionMismatch(selectedPlan, learnerProfessionId);
  const blockedByProfession = professionMismatch && !overrideProfession;
  const needsPlan = !hasEligiblePackage;
  const canGrant = Boolean(selectedPreset) && (!needsPlan || (planCode && !blockedByProfession)) && !isSaving;

  function handlePickPreset(preset: AccessPreset) {
    setSelectedPreset(preset);
    setStep('confirm');
  }

  function handleBack() {
    setStep('pick');
    setSelectedPreset(null);
    setError(null);
  }

  async function handleExpandScope() {
    const next = !scopeExpanded;
    setScopeExpanded(next);
    if (!next || !selectedPreset?.scopeField) return;
    if (selectedPreset.scopeField === 'recallSetCodes' && recallSets.length === 0) {
      setScopeOptionsLoading(true);
      try {
        setRecallSets(await fetchAdminRecallSetTags());
      } finally {
        setScopeOptionsLoading(false);
      }
    } else if (selectedPreset.scopeField === 'materialFolderIds' && folderTree.length === 0) {
      setScopeOptionsLoading(true);
      try {
        setFolderTree(await adminListMaterialFolders());
      } finally {
        setScopeOptionsLoading(false);
      }
    } else if (selectedPreset.scopeField === 'videoIds' && videos.length === 0) {
      setScopeOptionsLoading(true);
      try {
        setVideos(await fetchAllocatableVideos());
      } finally {
        setScopeOptionsLoading(false);
      }
    }
  }

  async function handleGrant() {
    if (!selectedPreset || !baseline) return;
    setIsSaving(true);
    setError(null);
    try {
      if (needsPlan) {
        const packagePayload: GrantUserPackageInput = {
          planCode,
          makePrimary: baseline.subscriptions.length === 0,
          overrideProfessionMismatch: professionMismatch && overrideProfession,
        };
        await grantUserPackage(userId, packagePayload);
      }
      const saved = await putUserAccessScope(userId, {
        modules: buildModuleOverrides(selectedPreset),
        recallSetCodes: selectedPreset.scopeField === 'recallSetCodes' ? scopeSelection : baseline.recallSetCodes,
        materialFolderIds:
          selectedPreset.scopeField === 'materialFolderIds' ? scopeSelection : baseline.materialFolderIds,
        videoIds: selectedPreset.scopeField === 'videoIds' ? scopeSelection : baseline.videoIds,
        accessExpiresAt: baseline.accessExpiresAt,
      });
      onGranted(saved);
      onClose();
    } catch (err) {
      setError(readErrorMessage(err, 'Unable to grant access.'));
    } finally {
      setIsSaving(false);
    }
  }

  const planOptions = plans.map((plan) => ({
    value: plan.code ?? plan.id,
    label: plan.price ? `${plan.name} (${plan.currency ?? ''}${plan.price})` : plan.name,
  }));

  return (
    <Modal open={open} onClose={onClose} title={`Quick Grant — ${userLabel}`} size="md">
      {error ? (
        <InlineAlert key={error} variant="error" dismissible className="mb-4">
          {error}
        </InlineAlert>
      ) : null}

      {isLoading ? (
        <p className="text-sm text-muted">Loading current access…</p>
      ) : step === 'pick' ? (
        <div className="space-y-2">
          <p className="text-sm text-muted">Pick what this learner should have access to.</p>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {ACCESS_PRESETS.map((preset) => {
              const Icon = preset.icon;
              return (
                <button
                  key={preset.id}
                  type="button"
                  onClick={() => handlePickPreset(preset)}
                  className="flex items-start gap-3 rounded-2xl border border-border bg-background-light p-3 text-left transition-colors hover:border-primary hover:bg-primary/5"
                >
                  <Icon className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden />
                  <span>
                    <span className="block text-sm font-semibold text-navy">{preset.label}</span>
                    <span className="block text-xs text-muted">{preset.description}</span>
                  </span>
                </button>
              );
            })}
            <button
              type="button"
              onClick={() => {
                onClose();
                onCustom();
              }}
              className="flex items-start gap-3 rounded-2xl border border-dashed border-border bg-background-light p-3 text-left transition-colors hover:border-primary hover:bg-primary/5 sm:col-span-2"
            >
              <SlidersHorizontal className="mt-0.5 h-5 w-5 shrink-0 text-muted" aria-hidden />
              <span>
                <span className="block text-sm font-semibold text-navy">Custom</span>
                <span className="block text-xs text-muted">
                  Open the full access panel for multi-plan, add-ons, or mixed module combinations.
                </span>
              </span>
            </button>
          </div>
        </div>
      ) : selectedPreset ? (
        <div className="space-y-4">
          <div className="rounded-2xl border border-border bg-background-light p-3">
            <p className="text-sm font-semibold text-navy">{selectedPreset.label}</p>
            <p className="text-xs text-muted">{selectedPreset.description}</p>
          </div>

          {needsPlan ? (
            <div className="space-y-2 rounded-2xl border border-dashed border-border p-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-muted">
                This learner has no active package — activate one to unlock any module
              </p>
              <Select
                label="Activate with"
                value={planCode}
                onChange={(event) => {
                  setPlanCode(event.target.value);
                  setOverrideProfession(false);
                }}
                options={[{ value: '', label: 'Select a plan...' }, ...planOptions]}
              />
              {professionMismatch ? (
                <InlineAlert variant="warning" title="Profession mismatch">
                  <p>
                    This plan is for <strong>{selectedPlan?.profession}</strong>, but the learner is registered
                    differently. Content is matched to their registered profession, so they may see nothing.
                  </p>
                  <Checkbox
                    label="Grant anyway (override the profession check)"
                    className="mt-3 bg-transparent"
                    checked={overrideProfession}
                    onChange={(event) => setOverrideProfession(event.target.checked)}
                  />
                </InlineAlert>
              ) : null}
            </div>
          ) : null}

          {selectedPreset.scopeField ? (
            <div>
              <button
                type="button"
                onClick={() => void handleExpandScope()}
                className="flex items-center gap-1 text-sm font-medium text-primary"
              >
                Customize scope
                {scopeExpanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
              </button>
              {scopeExpanded ? (
                <div className="mt-2">
                  {scopeOptionsLoading ? (
                    <p className="text-sm text-muted">Loading options…</p>
                  ) : selectedPreset.scopeField === 'recallSetCodes' ? (
                    <RecallSetPicker recallSets={recallSets} selectedCodes={scopeSelection} onChange={setScopeSelection} />
                  ) : selectedPreset.scopeField === 'materialFolderIds' ? (
                    <FolderScopePicker folderTree={folderTree} selectedIds={scopeSelection} onChange={setScopeSelection} />
                  ) : (
                    <VideoScopePicker videos={videos} selectedIds={scopeSelection} onChange={setScopeSelection} />
                  )}
                  <p className="mt-1 text-xs text-muted">Nothing ticked = every item their plan grants.</p>
                </div>
              ) : null}
            </div>
          ) : null}

          <div className="flex items-center justify-between gap-2 border-t border-border pt-3">
            <Button type="button" variant="ghost" onClick={handleBack} disabled={isSaving}>
              Back
            </Button>
            <Button type="button" onClick={() => void handleGrant()} disabled={!canGrant} loading={isSaving}>
              Grant Access
            </Button>
          </div>
        </div>
      ) : null}
    </Modal>
  );
}
