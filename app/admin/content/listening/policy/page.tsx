'use client';

import { useCallback, useEffect, useState } from 'react';
import { Settings, Save, User } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  adminGetListeningPolicy,
  adminUpsertListeningPolicy,
  adminGetListeningUserPolicyOverride,
  adminUpsertListeningUserPolicyOverride,
  type ListeningPolicyDto,
  type ListeningUserPolicyOverrideDto,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const DEFAULT_POLICY: ListeningPolicyDto = {
  id: 'global',
  attemptsPerPaperPerUser: 0,
  attemptCooldownMinutes: 0,
  bestScoreDisplay: 'best',
  showPastAttempts: true,
  fullPaperTimerMinutes: 45,
  gracePeriodSeconds: 10,
  onExpirySubmitPolicy: 'auto_submit_graded',
  countdownWarningsJson: '[300,60,15]',
  examReplayAllowed: false,
  learningReplayAllowed: true,
  learningEvidenceLoopEnabled: true,
  shortAnswerNormalisation: 'trim_collapse_case_insensitive',
  shortAnswerAcceptSynonyms: false,
  aiExtractionEnabled: true,
  aiExtractionRequireHumanApproval: true,
  aiExtractionMaxRetriesPerPaper: 5,
  showExplanationsAfterSubmit: true,
  showExplanationsOnlyIfWrong: false,
  showCorrectAnswerOnReview: true,
  defaultExtraTimePct: 0,
  screenReaderOptimised: true,
  autoExpireWorkerEnabled: true,
  autoExpireAfterMinutes: 180,
  allowResumeAfterExpiry: false,
  retainAnswerRowsDays: 730,
  retainAttemptHeadersDays: 3650,
  anonymiseOnAccountDelete: true,
  rowVersion: 0,
  updatedAt: new Date().toISOString(),
};

function Switch({
  id,
  checked,
  onChange,
  label,
  description,
}: {
  id: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
  description?: string;
}) {
  return (
    <label htmlFor={id} className="flex items-start gap-3 cursor-pointer">
      <div className="relative mt-0.5 flex-shrink-0">
        <input
          type="checkbox"
          id={id}
          checked={checked}
          onChange={(e) => onChange(e.target.checked)}
          className="sr-only"
        />
        <div
          className={`h-5 w-9 rounded-full transition-colors ${checked ? 'bg-primary' : 'bg-border'}`}
          aria-hidden="true"
        />
        <div
          className={`absolute top-0.5 left-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform ${checked ? 'translate-x-4' : ''}`}
          aria-hidden="true"
        />
      </div>
      <div>
        <div className="text-sm font-medium text-admin-text">{label}</div>
        {description && <div className="text-xs text-admin-text-muted mt-0.5">{description}</div>}
      </div>
    </label>
  );
}

function NumericField({
  label,
  value,
  onChange,
  min,
  description,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  description?: string;
}) {
  return (
    <div>
      <label className="block text-sm font-medium text-admin-text mb-1">{label}</label>
      {description && <p className="text-xs text-admin-text-muted mb-1">{description}</p>}
      <input
        type="number"
        min={min ?? 0}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm text-admin-text focus:outline-none focus:ring-2 focus:ring-primary/50"
      />
    </div>
  );
}

function TextField({
  label,
  value,
  onChange,
  description,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  description?: string;
}) {
  return (
    <div>
      <label className="block text-sm font-medium text-admin-text mb-1">{label}</label>
      {description && <p className="text-xs text-admin-text-muted mb-1">{description}</p>}
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm text-admin-text focus:outline-none focus:ring-2 focus:ring-primary/50"
      />
    </div>
  );
}

function SectionCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-border bg-background-light p-5 space-y-4">
      <h2 className="text-sm font-semibold text-admin-text uppercase tracking-wide">{title}</h2>
      {children}
    </div>
  );
}

export default function ListeningPolicyPage() {
  const [policy, setPolicy] = useState<ListeningPolicyDto>(DEFAULT_POLICY);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  // User override section
  const [userIdInput, setUserIdInput] = useState('');
  const [override, setOverride] = useState<ListeningUserPolicyOverrideDto | null>(null);
  const [overrideLoading, setOverrideLoading] = useState(false);
  const [overrideSaving, setOverrideSaving] = useState(false);
  const [overrideForm, setOverrideForm] = useState({
    extraTimeEntitlementPct: 0,
    blockAttempts: false,
    accessibilityModeEnabled: false,
    reason: '',
    expiresAt: '',
  });

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminGetListeningPolicy();
      setPolicy(data);
    } catch (e) {
      setToast({ variant: 'error', message: `Failed to load policy: ${(e as Error).message}` });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void reload(); }, [reload]);

  const save = useCallback(async () => {
    setSaving(true);
    try {
      const updated = await adminUpsertListeningPolicy(policy);
      setPolicy(updated);
      setToast({ variant: 'success', message: 'Listening policy saved.' });
    } catch (e) {
      setToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` });
    } finally {
      setSaving(false);
    }
  }, [policy]);

  const lookupUser = useCallback(async () => {
    if (!userIdInput.trim()) return;
    setOverrideLoading(true);
    try {
      const row = await adminGetListeningUserPolicyOverride(userIdInput.trim());
      setOverride(row);
      if (row) {
        setOverrideForm({
          extraTimeEntitlementPct: row.extraTimeEntitlementPct,
          blockAttempts: row.blockAttempts,
          accessibilityModeEnabled: row.accessibilityModeEnabled,
          reason: row.reason ?? '',
          expiresAt: row.expiresAt ? row.expiresAt.slice(0, 16) : '',
        });
      } else {
        setOverrideForm({ extraTimeEntitlementPct: 0, blockAttempts: false, accessibilityModeEnabled: false, reason: '', expiresAt: '' });
      }
    } catch (e) {
      setToast({ variant: 'error', message: `Lookup failed: ${(e as Error).message}` });
    } finally {
      setOverrideLoading(false);
    }
  }, [userIdInput]);

  const saveOverride = useCallback(async () => {
    if (!userIdInput.trim()) return;
    setOverrideSaving(true);
    try {
      const updated = await adminUpsertListeningUserPolicyOverride(userIdInput.trim(), {
        extraTimeEntitlementPct: overrideForm.extraTimeEntitlementPct,
        blockAttempts: overrideForm.blockAttempts,
        accessibilityModeEnabled: overrideForm.accessibilityModeEnabled,
        reason: overrideForm.reason || null,
        expiresAt: overrideForm.expiresAt ? new Date(overrideForm.expiresAt).toISOString() : null,
      });
      setOverride(updated);
      setToast({ variant: 'success', message: `Override saved for user ${userIdInput.trim()}.` });
    } catch (e) {
      setToast({ variant: 'error', message: `Override save failed: ${(e as Error).message}` });
    } finally {
      setOverrideSaving(false);
    }
  }, [userIdInput, overrideForm]);

  function field<K extends keyof ListeningPolicyDto>(key: K) {
    return (v: ListeningPolicyDto[K]) => setPolicy((p) => ({ ...p, [key]: v }));
  }

  if (loading) {
    return (
      <AdminRouteWorkspace>
        <Skeleton className="h-96" />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening Policy Settings">
      <AdminRouteSectionHeader
        icon={<Settings className="w-6 h-6" />}
        title="Listening Policy Settings"
        description="Configure the global Listening policy: retry limits, timer, audio replay, grading, review, accessibility, and lifecycle. Changes take effect within 15 seconds (cache TTL)."
      />

      <AdminRoutePanel title="Actions">
        <div className="flex items-center gap-3">
          <Button onClick={() => void save()} disabled={saving}>
            <Save className="w-4 h-4 mr-1.5" />
            {saving ? 'Saving...' : 'Save Policy'}
          </Button>
          {policy.updatedAt && (
            <span className="text-xs text-admin-text-muted">
              Last saved {new Date(policy.updatedAt).toLocaleString()}
              {policy.updatedByAdminId ? ` by ${policy.updatedByAdminId}` : ''}
            </span>
          )}
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title="Policy Sections">
        <div className="space-y-5">

          {/* §1 Retry */}
          <SectionCard title="§1 Retry">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <NumericField
                label="Attempts per paper per user"
                value={policy.attemptsPerPaperPerUser}
                onChange={field('attemptsPerPaperPerUser')}
                description="0 = unlimited"
              />
              <NumericField
                label="Attempt cooldown (minutes)"
                value={policy.attemptCooldownMinutes}
                onChange={field('attemptCooldownMinutes')}
                description="0 = no cooldown"
              />
              <TextField
                label="Best score display"
                value={policy.bestScoreDisplay}
                onChange={field('bestScoreDisplay')}
                description="best | latest | highest_recent"
              />
              <div className="flex items-center gap-3 pt-5">
                <Switch
                  id="showPastAttempts"
                  checked={policy.showPastAttempts}
                  onChange={field('showPastAttempts')}
                  label="Show past attempts"
                  description="Learners can review their attempt history"
                />
              </div>
            </div>
          </SectionCard>

          {/* §2 Timer */}
          <SectionCard title="§2 Timer">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <NumericField
                label="Full paper timer (minutes)"
                value={policy.fullPaperTimerMinutes}
                onChange={field('fullPaperTimerMinutes')}
                description="Whole-paper timer for exam mode (~40 min real OET)"
              />
              <NumericField
                label="Grace period (seconds)"
                value={policy.gracePeriodSeconds}
                onChange={field('gracePeriodSeconds')}
                description="Extra seconds after timer expires before auto-submit"
              />
              <TextField
                label="On expiry policy"
                value={policy.onExpirySubmitPolicy}
                onChange={field('onExpirySubmitPolicy')}
                description="auto_submit_graded | abandon | warn_only"
              />
              <TextField
                label="Countdown warnings (JSON array, seconds)"
                value={policy.countdownWarningsJson}
                onChange={field('countdownWarningsJson')}
                description='e.g. [300,60,15]'
              />
            </div>
          </SectionCard>

          {/* §3 Audio Replay */}
          <SectionCard title="§3 Audio Replay">
            <div className="space-y-3">
              <Switch
                id="examReplayAllowed"
                checked={policy.examReplayAllowed}
                onChange={field('examReplayAllowed')}
                label="Exam mode replay allowed"
                description="Real OET is one-play — default off"
              />
              <Switch
                id="learningReplayAllowed"
                checked={policy.learningReplayAllowed}
                onChange={field('learningReplayAllowed')}
                label="Learning mode replay allowed"
                description="Free replay in learning / drill modes"
              />
              <Switch
                id="learningEvidenceLoopEnabled"
                checked={policy.learningEvidenceLoopEnabled}
                onChange={field('learningEvidenceLoopEnabled')}
                label="Learning evidence loop enabled"
                description="Jump-to-segment playback on review in learning mode"
              />
            </div>
          </SectionCard>

          {/* §4 Grading */}
          <SectionCard title="§4 Grading">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <TextField
                label="Short answer normalisation"
                value={policy.shortAnswerNormalisation}
                onChange={field('shortAnswerNormalisation')}
                description="trim_collapse_case_insensitive | trim_only | exact"
              />
              <div className="pt-5">
                <Switch
                  id="shortAnswerAcceptSynonyms"
                  checked={policy.shortAnswerAcceptSynonyms}
                  onChange={field('shortAnswerAcceptSynonyms')}
                  label="Accept synonyms (non-standard)"
                  description="OET grades on canonical + accepted variants only — default off"
                />
              </div>
            </div>
          </SectionCard>

          {/* §5 Review */}
          <SectionCard title="§5 Review">
            <div className="space-y-3">
              <Switch
                id="showExplanationsAfterSubmit"
                checked={policy.showExplanationsAfterSubmit}
                onChange={field('showExplanationsAfterSubmit')}
                label="Show explanations after submit"
              />
              <Switch
                id="showExplanationsOnlyIfWrong"
                checked={policy.showExplanationsOnlyIfWrong}
                onChange={field('showExplanationsOnlyIfWrong')}
                label="Show explanations only if wrong"
              />
              <Switch
                id="showCorrectAnswerOnReview"
                checked={policy.showCorrectAnswerOnReview}
                onChange={field('showCorrectAnswerOnReview')}
                label="Show correct answer on review"
              />
            </div>
          </SectionCard>

          {/* §6 Accessibility */}
          <SectionCard title="§6 Accessibility">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <NumericField
                label="Default extra time (%)"
                value={policy.defaultExtraTimePct}
                onChange={field('defaultExtraTimePct')}
                description="Global default — 0 means no extra time unless per-user override"
              />
              <div className="pt-5">
                <Switch
                  id="screenReaderOptimised"
                  checked={policy.screenReaderOptimised}
                  onChange={field('screenReaderOptimised')}
                  label="Screen reader optimised mode"
                  description="Adds ARIA enhancements and focus management"
                />
              </div>
            </div>
          </SectionCard>

          {/* §7 Lifecycle */}
          <SectionCard title="§7 Lifecycle">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <Switch
                id="autoExpireWorkerEnabled"
                checked={policy.autoExpireWorkerEnabled}
                onChange={field('autoExpireWorkerEnabled')}
                label="Auto-expire worker enabled"
                description="Background worker expires stale in-progress attempts"
              />
              <NumericField
                label="Auto-expire after (minutes)"
                value={policy.autoExpireAfterMinutes}
                onChange={field('autoExpireAfterMinutes')}
                description="Attempts older than this are auto-expired"
              />
              <Switch
                id="allowResumeAfterExpiry"
                checked={policy.allowResumeAfterExpiry}
                onChange={field('allowResumeAfterExpiry')}
                label="Allow resume after expiry"
                description="Learners can resume expired attempts in learning mode"
              />
            </div>
          </SectionCard>

          {/* §8 Retention */}
          <SectionCard title="§8 Retention">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <NumericField
                label="Retain answer rows (days)"
                value={policy.retainAnswerRowsDays}
                onChange={field('retainAnswerRowsDays')}
              />
              <NumericField
                label="Retain attempt headers (days)"
                value={policy.retainAttemptHeadersDays}
                onChange={field('retainAttemptHeadersDays')}
              />
              <Switch
                id="anonymiseOnAccountDelete"
                checked={policy.anonymiseOnAccountDelete}
                onChange={field('anonymiseOnAccountDelete')}
                label="Anonymise on account delete"
              />
            </div>
          </SectionCard>

        </div>
      </AdminRoutePanel>

      {/* User Override Section */}
      <AdminRoutePanel title="User Policy Override">
        <div className="space-y-4">
          <p className="text-sm text-admin-text-muted">
            Look up a learner by user ID to view or set their Listening policy override (extra time, block flag, accessibility mode).
          </p>
          <div className="flex gap-2 items-end">
            <div className="flex-1">
              <label className="block text-sm font-medium text-admin-text mb-1">User ID</label>
              <Input
                value={userIdInput}
                onChange={(e) => setUserIdInput(e.target.value)}
                placeholder="Enter user ID"
              />
            </div>
            <Button variant="outline" onClick={() => void lookupUser()} disabled={overrideLoading || !userIdInput.trim()}>
              <User className="w-4 h-4 mr-1.5" />
              {overrideLoading ? 'Looking up...' : 'Look up'}
            </Button>
          </div>

          {(override !== undefined && userIdInput.trim()) && (
            <div className="rounded-2xl border border-border bg-background-light p-4 space-y-4">
              <p className="text-sm font-semibold text-admin-text">
                {override ? `Override for ${userIdInput.trim()}` : `No override found — create one for ${userIdInput.trim()}`}
              </p>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <NumericField
                  label="Extra time entitlement (%)"
                  value={overrideForm.extraTimeEntitlementPct}
                  onChange={(v) => setOverrideForm((f) => ({ ...f, extraTimeEntitlementPct: Math.min(100, Math.max(0, v)) }))}
                  description="0–100. Applied on top of full-paper timer."
                />
                <TextField
                  label="Reason"
                  value={overrideForm.reason}
                  onChange={(v) => setOverrideForm((f) => ({ ...f, reason: v }))}
                  description="Admin note for audit trail"
                />
                <div>
                  <label className="block text-sm font-medium text-admin-text mb-1">Expires at</label>
                  <input
                    type="datetime-local"
                    value={overrideForm.expiresAt}
                    onChange={(e) => setOverrideForm((f) => ({ ...f, expiresAt: e.target.value }))}
                    className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm text-admin-text focus:outline-none focus:ring-2 focus:ring-primary/50"
                  />
                  <p className="text-xs text-admin-text-muted mt-0.5">Leave blank for no expiry</p>
                </div>
                <div className="pt-1 space-y-3">
                  <Switch
                    id="overrideBlockAttempts"
                    checked={overrideForm.blockAttempts}
                    onChange={(v) => setOverrideForm((f) => ({ ...f, blockAttempts: v }))}
                    label="Block attempts"
                    description="Prevents this user from starting new listening attempts"
                  />
                  <Switch
                    id="overrideAccessibilityMode"
                    checked={overrideForm.accessibilityModeEnabled}
                    onChange={(v) => setOverrideForm((f) => ({ ...f, accessibilityModeEnabled: v }))}
                    label="Accessibility mode enabled"
                    description="High-contrast palette, larger focus rings, tab-only nav hints"
                  />
                </div>
              </div>
              <Button onClick={() => void saveOverride()} disabled={overrideSaving}>
                <Save className="w-4 h-4 mr-1.5" />
                {overrideSaving ? 'Saving...' : 'Save Override'}
              </Button>
              {override && (
                <p className="text-xs text-admin-text-muted">
                  Created {new Date(override.createdAt).toLocaleString()}
                  {override.grantedByAdminId ? ` by ${override.grantedByAdminId}` : ''}
                  {' · '}Last updated {new Date(override.updatedAt).toLocaleString()}
                </p>
              )}
            </div>
          )}
        </div>
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
