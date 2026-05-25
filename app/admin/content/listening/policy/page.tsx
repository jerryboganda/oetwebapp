'use client';

import { useCallback, useEffect, useState } from 'react';
import { Save, User } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { Switch } from '@/components/admin/ui/switch';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Label } from '@/components/admin/ui/label';
import { Toast } from '@/components/ui/alert';
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

function SwitchRow({
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
    <div className="flex items-start gap-3">
      <Switch id={id} checked={checked} onCheckedChange={onChange} className="mt-0.5" />
      <div>
        <Label htmlFor={id} className="text-sm font-medium text-admin-fg-strong">{label}</Label>
        {description && <p className="text-xs text-admin-fg-muted mt-0.5">{description}</p>}
      </div>
    </div>
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
    <div className="space-y-1">
      <Label className="text-sm font-medium text-admin-fg-strong">{label}</Label>
      {description && <p className="text-xs text-admin-fg-muted">{description}</p>}
      <Input
        type="number"
        min={min ?? 0}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
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
    <div className="space-y-1">
      <Label className="text-sm font-medium text-admin-fg-strong">{label}</Label>
      {description && <p className="text-xs text-admin-fg-muted">{description}</p>}
      <Input type="text" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

function SectionCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm uppercase tracking-wide">{title}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">{children}</CardContent>
    </Card>
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Policy' },
  ];

  if (loading) {
    return (
      <AdminSettingsLayout title="Listening Policy Settings" breadcrumbs={breadcrumbs}>
        <Skeleton className="h-96 rounded-admin" />
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Settings"
      title="Listening Policy Settings"
      description="Configure the global Listening policy: retry limits, timer, audio replay, grading, review, accessibility, and lifecycle. Changes take effect within 15 seconds (cache TTL)."
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex items-center gap-3">
          {policy.updatedAt && (
            <span className="text-xs text-admin-fg-muted hidden md:inline">
              Last saved {new Date(policy.updatedAt).toLocaleString()}
              {policy.updatedByAdminId ? ` by ${policy.updatedByAdminId}` : ''}
            </span>
          )}
          <Button variant="primary" size="sm" onClick={() => void save()} disabled={saving}>
            <Save className="w-4 h-4 mr-1.5" />
            {saving ? 'Saving…' : 'Save Policy'}
          </Button>
        </div>
      }
    >
      <div className="space-y-5">
        {/* §1 Retry */}
        <SectionCard title="§1 Retry">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <NumericField label="Attempts per paper per user" value={policy.attemptsPerPaperPerUser} onChange={field('attemptsPerPaperPerUser')} description="0 = unlimited" />
            <NumericField label="Attempt cooldown (minutes)" value={policy.attemptCooldownMinutes} onChange={field('attemptCooldownMinutes')} description="0 = no cooldown" />
            <TextField label="Best score display" value={policy.bestScoreDisplay} onChange={field('bestScoreDisplay')} description="best | latest | highest_recent" />
            <div className="pt-5">
              <SwitchRow id="showPastAttempts" checked={policy.showPastAttempts} onChange={field('showPastAttempts')} label="Show past attempts" description="Learners can review their attempt history" />
            </div>
          </div>
        </SectionCard>

        {/* §2 Timer */}
        <SectionCard title="§2 Timer">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <NumericField label="Full paper timer (minutes)" value={policy.fullPaperTimerMinutes} onChange={field('fullPaperTimerMinutes')} description="Whole-paper timer for exam mode (~40 min real OET)" />
            <NumericField label="Grace period (seconds)" value={policy.gracePeriodSeconds} onChange={field('gracePeriodSeconds')} description="Extra seconds after timer expires before auto-submit" />
            <TextField label="On expiry policy" value={policy.onExpirySubmitPolicy} onChange={field('onExpirySubmitPolicy')} description="auto_submit_graded | abandon | warn_only" />
            <TextField label="Countdown warnings (JSON array, seconds)" value={policy.countdownWarningsJson} onChange={field('countdownWarningsJson')} description='e.g. [300,60,15]' />
          </div>
        </SectionCard>

        {/* §3 Audio Replay */}
        <SectionCard title="§3 Audio Replay">
          <div className="space-y-3">
            <SwitchRow id="examReplayAllowed" checked={policy.examReplayAllowed} onChange={field('examReplayAllowed')} label="Exam mode replay allowed" description="Real OET is one-play — default off" />
            <SwitchRow id="learningReplayAllowed" checked={policy.learningReplayAllowed} onChange={field('learningReplayAllowed')} label="Learning mode replay allowed" description="Free replay in learning / drill modes" />
            <SwitchRow id="learningEvidenceLoopEnabled" checked={policy.learningEvidenceLoopEnabled} onChange={field('learningEvidenceLoopEnabled')} label="Learning evidence loop enabled" description="Jump-to-segment playback on review in learning mode" />
          </div>
        </SectionCard>

        {/* §4 Grading */}
        <SectionCard title="§4 Grading">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <TextField label="Short answer normalisation" value={policy.shortAnswerNormalisation} onChange={field('shortAnswerNormalisation')} description="trim_collapse_case_insensitive | trim_only | exact" />
            <div className="pt-5">
              <SwitchRow id="shortAnswerAcceptSynonyms" checked={policy.shortAnswerAcceptSynonyms} onChange={field('shortAnswerAcceptSynonyms')} label="Accept synonyms (non-standard)" description="OET grades on canonical + accepted variants only — default off" />
            </div>
          </div>
        </SectionCard>

        {/* §5 Review */}
        <SectionCard title="§5 Review">
          <div className="space-y-3">
            <SwitchRow id="showExplanationsAfterSubmit" checked={policy.showExplanationsAfterSubmit} onChange={field('showExplanationsAfterSubmit')} label="Show explanations after submit" />
            <SwitchRow id="showExplanationsOnlyIfWrong" checked={policy.showExplanationsOnlyIfWrong} onChange={field('showExplanationsOnlyIfWrong')} label="Show explanations only if wrong" />
            <SwitchRow id="showCorrectAnswerOnReview" checked={policy.showCorrectAnswerOnReview} onChange={field('showCorrectAnswerOnReview')} label="Show correct answer on review" />
          </div>
        </SectionCard>

        {/* §6 Accessibility */}
        <SectionCard title="§6 Accessibility">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <NumericField label="Default extra time (%)" value={policy.defaultExtraTimePct} onChange={field('defaultExtraTimePct')} description="Global default — 0 means no extra time unless per-user override" />
            <div className="pt-5">
              <SwitchRow id="screenReaderOptimised" checked={policy.screenReaderOptimised} onChange={field('screenReaderOptimised')} label="Screen reader optimised mode" description="Adds ARIA enhancements and focus management" />
            </div>
          </div>
        </SectionCard>

        {/* §7 Lifecycle */}
        <SectionCard title="§7 Lifecycle">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <SwitchRow id="autoExpireWorkerEnabled" checked={policy.autoExpireWorkerEnabled} onChange={field('autoExpireWorkerEnabled')} label="Auto-expire worker enabled" description="Background worker expires stale in-progress attempts" />
            <NumericField label="Auto-expire after (minutes)" value={policy.autoExpireAfterMinutes} onChange={field('autoExpireAfterMinutes')} description="Attempts older than this are auto-expired" />
            <SwitchRow id="allowResumeAfterExpiry" checked={policy.allowResumeAfterExpiry} onChange={field('allowResumeAfterExpiry')} label="Allow resume after expiry" description="Learners can resume expired attempts in learning mode" />
          </div>
        </SectionCard>

        {/* §8 Retention */}
        <SectionCard title="§8 Retention">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <NumericField label="Retain answer rows (days)" value={policy.retainAnswerRowsDays} onChange={field('retainAnswerRowsDays')} />
            <NumericField label="Retain attempt headers (days)" value={policy.retainAttemptHeadersDays} onChange={field('retainAttemptHeadersDays')} />
            <SwitchRow id="anonymiseOnAccountDelete" checked={policy.anonymiseOnAccountDelete} onChange={field('anonymiseOnAccountDelete')} label="Anonymise on account delete" />
          </div>
        </SectionCard>

        {/* User Override */}
        <Card>
          <CardHeader>
            <CardTitle>User Policy Override</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <p className="text-sm text-admin-fg-muted">
              Look up a learner by user ID to view or set their Listening policy override (extra time, block flag, accessibility mode).
            </p>
            <div className="flex gap-2 items-end">
              <div className="flex-1 space-y-1">
                <Label className="text-sm font-medium text-admin-fg-strong">User ID</Label>
                <Input value={userIdInput} onChange={(e) => setUserIdInput(e.target.value)} placeholder="Enter user ID" />
              </div>
              <Button variant="outline" onClick={() => void lookupUser()} disabled={overrideLoading || !userIdInput.trim()}>
                <User className="w-4 h-4 mr-1.5" />
                {overrideLoading ? 'Looking up…' : 'Look up'}
              </Button>
            </div>

            {(override !== undefined && userIdInput.trim()) && (
              <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-4 space-y-4">
                <p className="text-sm font-semibold text-admin-fg-strong">
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
                  <div className="space-y-1">
                    <Label className="text-sm font-medium text-admin-fg-strong">Expires at</Label>
                    <Input
                      type="datetime-local"
                      value={overrideForm.expiresAt}
                      onChange={(e) => setOverrideForm((f) => ({ ...f, expiresAt: e.target.value }))}
                    />
                    <p className="text-xs text-admin-fg-muted">Leave blank for no expiry</p>
                  </div>
                  <div className="pt-1 space-y-3">
                    <SwitchRow
                      id="overrideBlockAttempts"
                      checked={overrideForm.blockAttempts}
                      onChange={(v) => setOverrideForm((f) => ({ ...f, blockAttempts: v }))}
                      label="Block attempts"
                      description="Prevents this user from starting new listening attempts"
                    />
                    <SwitchRow
                      id="overrideAccessibilityMode"
                      checked={overrideForm.accessibilityModeEnabled}
                      onChange={(v) => setOverrideForm((f) => ({ ...f, accessibilityModeEnabled: v }))}
                      label="Accessibility mode enabled"
                      description="High-contrast palette, larger focus rings, tab-only nav hints"
                    />
                  </div>
                </div>
                <Button variant="primary" onClick={() => void saveOverride()} disabled={overrideSaving}>
                  <Save className="w-4 h-4 mr-1.5" />
                  {overrideSaving ? 'Saving…' : 'Save Override'}
                </Button>
                {override && (
                  <p className="text-xs text-admin-fg-muted">
                    Created {new Date(override.createdAt).toLocaleString()}
                    {override.grantedByAdminId ? ` by ${override.grantedByAdminId}` : ''}
                    {' · '}Last updated {new Date(override.updatedAt).toLocaleString()}
                  </p>
                )}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}
