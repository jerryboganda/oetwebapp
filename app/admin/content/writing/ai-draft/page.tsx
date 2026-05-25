'use client';

import { useCallback, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Wand2 } from 'lucide-react';
import { adminGenerateWritingAiDraft } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { Button } from '@/components/admin/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/admin/ui/card';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const PROFESSIONS: { value: string; label: string }[] = [
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'veterinary', label: 'Veterinary' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'occupationaltherapy', label: 'Occupational therapy' },
  { value: 'speechpathology', label: 'Speech pathology' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'dietetics', label: 'Dietetics' },
  { value: 'otheralliedhealth', label: 'Other allied health' },
];

const LETTER_TYPES: { value: string; label: string }[] = [
  { value: 'routine_referral', label: 'Routine referral' },
  { value: 'urgent_referral', label: 'Urgent referral' },
  { value: 'discharge', label: 'Discharge letter' },
  { value: 'transfer', label: 'Transfer letter' },
  { value: 'non_medical_referral', label: 'Referral to non-medical professional' },
  { value: 'referral_to_gp', label: 'Referral to GP' },
];

export default function WritingAiDraftPage() {
  const router = useRouter();
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [profession, setProfession] = useState('medicine');
  const [letterType, setLetterType] = useState('routine_referral');
  const [recipientSpecialty, setRecipientSpecialty] = useState('');
  const [difficulty, setDifficulty] = useState('medium');
  const [count, setCount] = useState(12);
  const [prompt, setPrompt] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const onGenerate = useCallback(async () => {
    if (!canWriteContent) return;
    if (!prompt.trim()) {
      setToast({ variant: 'error', message: 'Provide a scenario brief.' });
      return;
    }
    setSubmitting(true);
    try {
      const res = await adminGenerateWritingAiDraft({
        prompt,
        profession,
        letterType,
        recipientSpecialty: recipientSpecialty.trim() || undefined,
        difficulty,
        targetCaseNoteCount: count,
      });
      setToast({
        variant: res.warning ? 'error' : 'success',
        message: res.warning
          ? `Draft created with warning: ${res.warning}`
          : `Draft "${res.title}" created (${res.caseNoteCount} case-note lines, ~${res.modelLetterWordCount} words).`,
      });
      setTimeout(() => router.push(`/admin/content/${encodeURIComponent(res.contentId)}`), 700);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Draft generation failed.' });
    } finally {
      setSubmitting(false);
    }
  }, [canWriteContent, profession, letterType, recipientSpecialty, difficulty, count, prompt, router]);

  if (isLoading) return null;

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Writing', href: '/admin/content/writing' },
    { label: 'AI Draft' },
  ];

  if (!canWriteContent) {
    return (
      <AdminSettingsLayout title="AI writing draft" breadcrumbs={breadcrumbs} eyebrow="CMS">
        <Card><CardContent className="pt-6"><p className="text-sm text-admin-fg-muted">Content write permission is required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="AI writing draft"
      description="Generate a draft Writing task (case notes + model letter) via the grounded AI gateway. Drafts are always stored as draft and must be reviewed before publishing."
      eyebrow="CMS"
      breadcrumbs={breadcrumbs}
      icon={<Wand2 className="h-5 w-5" />}
      actions={
        <Button asChild variant="outline" startIcon={<ArrowLeft className="h-4 w-4" />}>
          <Link href="/admin/content/writing">Back to Writing CMS</Link>
        </Button>
      }
    >
      <SettingsSection title="Draft generator" description="The grounded AI gateway physically refuses ungrounded prompts. Drafts are always stored as Draft and must be reviewed before publishing.">
        <p className="text-sm leading-6 text-admin-fg-muted">
          This generates a draft Writing task via the grounded AI gateway. Drafts are always stored
          as <strong>draft</strong> — review and edit before publishing. The gateway physically refuses
          ungrounded prompts.
        </p>

        <div className="grid gap-3 sm:grid-cols-3">
          <Select
            label="Profession"
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            options={PROFESSIONS}
          />
          <Select
            label="Letter type"
            value={letterType}
            onChange={(e) => setLetterType(e.target.value)}
            options={LETTER_TYPES}
          />
          <Select
            label="Difficulty"
            value={difficulty}
            onChange={(e) => setDifficulty(e.target.value)}
            options={[
              { value: 'easy', label: 'Easy' },
              { value: 'medium', label: 'Medium' },
              { value: 'hard', label: 'Hard' },
            ]}
          />
          <Input
            label="Recipient specialty (optional)"
            value={recipientSpecialty}
            onChange={(e) => setRecipientSpecialty(e.target.value)}
            placeholder="e.g. cardiology"
          />
          <Input
            label="Target case-note line count"
            type="number"
            value={String(count)}
            onChange={(e) => setCount(Math.max(8, Math.min(20, Number(e.target.value) || 12)))}
          />
        </div>

        <Textarea
          label="Scenario brief"
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          rows={8}
          placeholder="Describe the patient, the clinical scenario, the reason for the letter, and any important context the candidate must communicate. The grounded AI gateway will produce a structured case-notes block and a model letter."
        />

        <div className="flex justify-end">
          <Button disabled={submitting} loading={submitting} onClick={onGenerate}>
            {submitting ? 'Generating…' : 'Generate draft'}
          </Button>
        </div>
      </SettingsSection>

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminSettingsLayout>
  );
}
