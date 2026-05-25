'use client';

import { Sliders } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getReadingPolicy, updateReadingPolicy, type ReadingPolicyDto } from '@/lib/reading-authoring-api';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Policies', href: '/admin/policies' },
  { label: 'Reading' },
];

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AdminReadingGlobalPolicyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [policy, setPolicy] = useState<ReadingPolicyDto | null>(null);
  const [formData, setFormData] = useState<ReadingPolicyDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const p = await getReadingPolicy();
      setPolicy(p);
      setFormData(p);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, []);

  useEffect(() => {
    queueMicrotask(() => { void load(); });
  }, [load]);

  const setField = <K extends keyof ReadingPolicyDto>(key: K, value: ReadingPolicyDto[K]) => {
    setFormData((prev) => prev ? { ...prev, [key]: value } : prev);
  };

  const save = async () => {
    if (!formData) return;
    setSaving(true);
    try {
      const updated = await updateReadingPolicy(formData);
      setPolicy(updated);
      setFormData(updated);
      setToast({ variant: 'success', message: 'Reading policy saved.' });
    } catch (e) {
      setToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` });
    } finally {
      setSaving(false);
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminSettingsLayout
      title="Reading — global policy"
      description="These settings apply to all learners unless overridden per-user. Changes take effect immediately."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Policies"
      icon={<Sliders className="h-5 w-5" />}
    >
      <AsyncStateWrapper status={status} onRetry={load}>
        {formData && (
          <div className="space-y-6">
            {/* Attempt Limits */}
            <SettingsSection title="Attempt Limits" description="Control how many times learners can attempt each paper.">
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Input
                  type="number"
                  label="Attempts per paper per user"
                  value={formData.attemptsPerPaperPerUser}
                  onChange={(e) => setField('attemptsPerPaperPerUser', Number(e.target.value))}
                />
                <Input
                  type="number"
                  label="Attempt cooldown (minutes)"
                  value={formData.attemptCooldownMinutes}
                  onChange={(e) => setField('attemptCooldownMinutes', Number(e.target.value))}
                />
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowAttemptOnArchivedPaper}
                    onChange={(e) => setField('allowAttemptOnArchivedPaper', e.target.checked)}
                  />
                  Allow attempts on archived papers
                </label>
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowPausingAttempt}
                    onChange={(e) => setField('allowPausingAttempt', e.target.checked)}
                  />
                  Allow pausing attempts
                </label>
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowResumeAfterExpiry}
                    onChange={(e) => setField('allowResumeAfterExpiry', e.target.checked)}
                  />
                  Allow resume after expiry
                </label>
              </div>
            </SettingsSection>

            {/* Timer Settings */}
            <SettingsSection title="Timer Settings" description="Part A and B/C timer durations and expiry behaviour.">
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Input
                  type="number"
                  label="Part A timer (minutes)"
                  value={formData.partATimerMinutes}
                  onChange={(e) => setField('partATimerMinutes', Number(e.target.value))}
                />
                <Input
                  type="number"
                  label="Part B/C timer (minutes)"
                  value={formData.partBCTimerMinutes}
                  onChange={(e) => setField('partBCTimerMinutes', Number(e.target.value))}
                />
                <Input
                  type="number"
                  label="Grace period (seconds)"
                  value={formData.gracePeriodSeconds}
                  onChange={(e) => setField('gracePeriodSeconds', Number(e.target.value))}
                />
                <Input
                  type="number"
                  label="Auto-expire after (minutes)"
                  value={formData.autoExpireAfterMinutes}
                  onChange={(e) => setField('autoExpireAfterMinutes', Number(e.target.value))}
                />
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.autoExpireWorkerEnabled}
                    onChange={(e) => setField('autoExpireWorkerEnabled', e.target.checked)}
                  />
                  Auto-expire worker enabled
                </label>
              </div>
            </SettingsSection>

            {/* Grading */}
            <SettingsSection title="Grading" description="Answer matching and scoring rules.">
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Input
                  label="Short answer normalisation"
                  value={formData.shortAnswerNormalisation}
                  onChange={(e) => setField('shortAnswerNormalisation', e.target.value)}
                />
                <Input
                  label="Sentence completion strictness"
                  value={formData.sentenceCompletionStrictness}
                  onChange={(e) => setField('sentenceCompletionStrictness', e.target.value)}
                />
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.shortAnswerAcceptSynonyms}
                    onChange={(e) => setField('shortAnswerAcceptSynonyms', e.target.checked)}
                  />
                  Accept synonyms for short answers
                </label>
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.matchingAllowPartialCredit}
                    onChange={(e) => setField('matchingAllowPartialCredit', e.target.checked)}
                  />
                  Allow partial credit for matching questions
                </label>
              </div>
            </SettingsSection>

            {/* Review & Explanations */}
            <SettingsSection title="Review & Explanations" description="What learners see after submitting.">
              <div className="grid grid-cols-1 gap-4">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.showExplanationsAfterSubmit}
                    onChange={(e) => setField('showExplanationsAfterSubmit', e.target.checked)}
                  />
                  Show explanations after submit
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.showExplanationsOnlyIfWrong}
                    onChange={(e) => setField('showExplanationsOnlyIfWrong', e.target.checked)}
                  />
                  Show explanations only for wrong answers
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.showCorrectAnswerOnReview}
                    onChange={(e) => setField('showCorrectAnswerOnReview', e.target.checked)}
                  />
                  Show correct answer on review
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowResultDownload}
                    onChange={(e) => setField('allowResultDownload', e.target.checked)}
                  />
                  Allow result download
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.showPastAttempts}
                    onChange={(e) => setField('showPastAttempts', e.target.checked)}
                  />
                  Show past attempts to learner
                </label>
              </div>
            </SettingsSection>

            {/* Accessibility */}
            <SettingsSection title="Accessibility & Display" description="Learner-facing accessibility controls.">
              <div className="grid grid-cols-1 gap-4">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.fontScaleUserControl}
                    onChange={(e) => setField('fontScaleUserControl', e.target.checked)}
                  />
                  Allow learner font scale control
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.highContrastMode}
                    onChange={(e) => setField('highContrastMode', e.target.checked)}
                  />
                  Enable high contrast mode option
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.screenReaderOptimised}
                    onChange={(e) => setField('screenReaderOptimised', e.target.checked)}
                  />
                  Screen reader optimised mode
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowPaperReadingMode}
                    onChange={(e) => setField('allowPaperReadingMode', e.target.checked)}
                  />
                  Allow paper reading mode (print layout)
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.extraTimeApprovalWorkflow}
                    onChange={(e) => setField('extraTimeApprovalWorkflow', e.target.checked)}
                  />
                  Extra time approval workflow enabled
                </label>
              </div>
            </SettingsSection>

            {/* Practice & Learning */}
            <SettingsSection title="Practice & Learning Mode" description="Enable or disable practice features.">
              <div className="grid grid-cols-1 gap-4">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.questionBankEnabled}
                    onChange={(e) => setField('questionBankEnabled', e.target.checked)}
                  />
                  Question bank enabled
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowLearnerRandomisation}
                    onChange={(e) => setField('allowLearnerRandomisation', e.target.checked)}
                  />
                  Allow learner to randomise question order
                </label>
              </div>
            </SettingsSection>

            {/* Security */}
            <SettingsSection title="Security & Rate Limits" description="Submission and session security controls.">
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Input
                  type="number"
                  label="Submit rate limit (per minute)"
                  value={formData.submitRateLimitPerMinute}
                  onChange={(e) => setField('submitRateLimitPerMinute', Number(e.target.value))}
                />
                <Input
                  type="number"
                  label="Autosave rate limit (per minute)"
                  value={formData.autosaveRateLimitPerMinute}
                  onChange={(e) => setField('autosaveRateLimitPerMinute', Number(e.target.value))}
                />
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.requireFreshAuthForSubmit}
                    onChange={(e) => setField('requireFreshAuthForSubmit', e.target.checked)}
                  />
                  Require fresh auth for submit
                </label>
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.allowMultipleConcurrentAttempts}
                    onChange={(e) => setField('allowMultipleConcurrentAttempts', e.target.checked)}
                  />
                  Allow multiple concurrent attempts
                </label>
                <label className="flex items-center gap-2 col-span-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border text-primary"
                    checked={formData.preventMultipleTabs}
                    onChange={(e) => setField('preventMultipleTabs', e.target.checked)}
                  />
                  Prevent multiple tabs
                </label>
              </div>
            </SettingsSection>

            {/* Save footer */}
            <div className="flex items-center justify-between rounded-xl border border-border bg-surface p-4">
              <p className="text-sm text-muted">Changes take effect immediately for all new attempts.</p>
              <Button variant="primary" onClick={() => void save()} loading={saving} disabled={saving}>
                Save policy
              </Button>
            </div>
          </div>
        )}
      </AsyncStateWrapper>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminSettingsLayout>
  );
}

