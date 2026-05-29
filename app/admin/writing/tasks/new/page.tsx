'use client';

import { useCallback, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ShieldCheck } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Checkbox, Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Writing', href: '/admin/writing' },
  { label: 'Tasks', href: '/admin/content/writing' },
  { label: 'New' },
];
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  createWritingPaper,
  submitWritingPaperForReview,
  updateWritingStructure,
  type WritingAuthoringStructure,
  type WritingTaskCreateDto,
} from '@/lib/content-upload-api';
import { analytics } from '@/lib/analytics';

const PROFESSIONS = [
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'veterinary', label: 'Veterinary' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'occupational-therapy', label: 'Occupational Therapy' },
  { value: 'speech-pathology', label: 'Speech Pathology' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'dietetics', label: 'Dietetics' },
];

const LETTER_TYPES = [
  { value: 'routine_referral', label: 'Routine referral' },
  { value: 'urgent_referral', label: 'Urgent referral' },
  { value: 'non_medical_referral', label: 'Non-medical referral' },
  { value: 'update_discharge', label: 'Update and discharge' },
  { value: 'update_referral_specialist_to_gp', label: 'Specialist update / referral' },
  { value: 'transfer_letter', label: 'Transfer letter' },
];

// Veterinary does not permit non_medical_referral per the backend allow-list
// at WritingContentStructure.AllowedLetterTypesByProfession.
const VETERINARY_DISALLOWED_LETTER_TYPES = new Set(['non_medical_referral']);

type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface FormState {
  title: string;
  professionId: string;
  letterType: string;
  taskPrompt: string;
  caseNotesText: string;
  modelAnswerText: string;
  estimatedDurationMinutes: number;
  sourceProvenance: string;
  integrityAcknowledged: boolean;
}

const INITIAL: FormState = {
  title: '',
  professionId: 'medicine',
  letterType: 'routine_referral',
  taskPrompt: '',
  caseNotesText: '',
  modelAnswerText: '',
  estimatedDurationMinutes: 45,
  sourceProvenance: '',
  integrityAcknowledged: false,
};

export default function NewWritingTaskPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [form, setForm] = useState<FormState>(INITIAL);
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const update = useCallback(<K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
  }, []);

  const letterTypeOptions = useMemo(() => {
    if (form.professionId === 'veterinary') {
      return LETTER_TYPES.filter((opt) => !VETERINARY_DISALLOWED_LETTER_TYPES.has(opt.value));
    }
    return LETTER_TYPES;
  }, [form.professionId]);

  const validationError = useMemo(() => {
    if (!form.title.trim()) return 'Title is required.';
    if (!form.professionId) return 'Profession is required.';
    if (!form.letterType) return 'Letter type is required.';
    if (form.professionId === 'veterinary' && VETERINARY_DISALLOWED_LETTER_TYPES.has(form.letterType)) {
      return `Letter type "${form.letterType}" is not allowed for veterinary.`;
    }
    if (!form.taskPrompt.trim()) return 'Task prompt is required.';
    if (!form.caseNotesText.trim()) return 'Case notes are required.';
    if (!form.modelAnswerText.trim()) return 'Model answer is required.';
    if (!form.sourceProvenance.trim()) return 'Source provenance is required.';
    if (form.estimatedDurationMinutes < 5 || form.estimatedDurationMinutes > 240) {
      return 'Estimated duration must be between 5 and 240 minutes.';
    }
    if (!form.integrityAcknowledged) return 'Integrity acknowledgement is required.';
    return null;
  }, [form]);

  const save = useCallback(async (alsoSubmitForReview: boolean) => {
    if (validationError) {
      setToast({ variant: 'error', message: validationError });
      if (!form.integrityAcknowledged) {
        analytics.track('admin_writing_task_integrity_ack_blocked');
      }
      return;
    }
    setSubmitting(true);
    try {
      analytics.track('admin_writing_task_authoring_started');
      const dto: WritingTaskCreateDto = {
        title: form.title.trim(),
        professionId: form.professionId,
        letterType: form.letterType,
        estimatedDurationMinutes: form.estimatedDurationMinutes,
        priority: 0,
        sourceProvenance: form.sourceProvenance.trim(),
        integrityAcknowledged: true,
      };
      const created = await createWritingPaper(dto);

      // Persist the authoring structure (task prompt + case notes + model answer)
      // through the existing writing-structure endpoint so the publish gate
      // validators have something to inspect.
      const structure: WritingAuthoringStructure = {
        taskPrompt: form.taskPrompt.trim(),
        letterType: form.letterType,
        caseNotes: form.caseNotesText.trim(),
        modelAnswerText: form.modelAnswerText.trim(),
        criteriaFocus: ['purpose', 'content', 'conciseness_clarity', 'genre_style', 'organisation_layout', 'language'],
      };
      await updateWritingStructure(created.id, structure);
      analytics.track('admin_writing_task_draft_saved', { paperId: created.id });

      if (alsoSubmitForReview) {
        await submitWritingPaperForReview(created.id);
        analytics.track('admin_writing_task_submitted_for_review', { paperId: created.id });
        setToast({ variant: 'success', message: 'Task created and submitted for review.' });
      } else {
        setToast({ variant: 'success', message: 'Task saved as draft.' });
      }
      router.push('/admin/content/writing');
    } catch (e) {
      const err = e as Error & { detail?: { code?: string; error?: string } };
      const code = err.detail?.code;
      if (code === 'integrity_acknowledgement_required') {
        analytics.track('admin_writing_task_integrity_ack_blocked');
        setToast({ variant: 'error', message: 'Integrity acknowledgement is required by the server.' });
      } else {
        setToast({ variant: 'error', message: err.detail?.error || err.message || 'Save failed.' });
      }
    } finally {
      setSubmitting(false);
    }
  }, [form, router, validationError]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Create Writing task" breadcrumbs={BREADCRUMBS}>
        <p className="text-sm text-admin-fg-muted">Admin access required.</p>
      </AdminSettingsLayout>
    );
  }

  if (!canWriteContent) {
    return (
      <AdminSettingsLayout title="Create Writing task" breadcrumbs={BREADCRUMBS}>
        <p className="text-sm text-admin-fg-muted">You do not have the ContentWrite permission required to author writing tasks.</p>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Create Writing task"
      description="Author an original OET Writing task. All content must be original or properly licensed. No recalled or leaked exam material."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Writing"
      icon={<ShieldCheck className="h-5 w-5" />}
      backHref="/admin/content/writing"
    >
      <SettingsSection title="Task metadata">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Input
            label="Title *"
            value={form.title}
            onChange={(e) => update('title', e.target.value)}
            placeholder="e.g. Medicine: Routine Referral (Acne to Dermatology)"
            maxLength={200}
          />
          <Select
            label="Profession *"
            value={form.professionId}
            onChange={(e) => {
              const next = e.target.value;
              update('professionId', next);
              // If the previously chosen letter type is now disallowed, reset.
              if (next === 'veterinary' && VETERINARY_DISALLOWED_LETTER_TYPES.has(form.letterType)) {
                update('letterType', 'routine_referral');
              }
            }}
            options={PROFESSIONS}
          />
          <Select
            label="Letter type *"
            value={form.letterType}
            onChange={(e) => update('letterType', e.target.value)}
            options={letterTypeOptions}
          />
          <Input
            label="Estimated duration (minutes) *"
            type="number"
            min={5}
            max={240}
            value={form.estimatedDurationMinutes}
            onChange={(e) => update('estimatedDurationMinutes', Number(e.target.value) || 45)}
          />
          <Input
            label="Source provenance *"
            value={form.sourceProvenance}
            onChange={(e) => update('sourceProvenance', e.target.value)}
            placeholder="e.g. Original content authored by [team member] on [date]"
            maxLength={256}
            hint="Required before publish. Describe the origin of this content for the audit trail."
            className="md:col-span-2"
          />
        </div>
      </SettingsSection>

      <SettingsSection title="Task prompt">
        <Textarea
          label="Task prompt *"
          value={form.taskPrompt}
          onChange={(e) => update('taskPrompt', e.target.value)}
          rows={3}
          maxLength={1000}
          placeholder="Using the case notes, write a [letter type] to [recipient]. The body of your letter should be approximately 180–200 words."
        />
      </SettingsSection>

      <SettingsSection title="Case notes (learner stimulus)">
        <Textarea
          label="Case notes *"
          value={form.caseNotesText}
          onChange={(e) => update('caseNotesText', e.target.value)}
          rows={14}
          placeholder="Patient details, social/medical history, presenting problem, examination findings, management plan, writing task instruction…"
          hint="Plain text. Use blank lines between sections."
        />
      </SettingsSection>

      <SettingsSection title="Model answer (reference letter)">
        <Textarea
          label="Model answer *"
          value={form.modelAnswerText}
          onChange={(e) => update('modelAnswerText', e.target.value)}
          rows={14}
          placeholder="Dear [Recipient], …"
          hint="Reference 180–200 word body. Shown to learners only after they submit their attempt."
        />
      </SettingsSection>

      <SettingsSection title="Content integrity acknowledgement">
        <p className="mb-3 text-sm text-admin-fg-muted">
          OET prohibits the use of recalled or leaked exam content. Please confirm before saving:
        </p>
        <Checkbox
          checked={form.integrityAcknowledged}
          onChange={(e) => update('integrityAcknowledged', e.target.checked)}
          label="I confirm this content is original or properly licensed and is not derived from any recalled or leaked OET exam paper. I accept responsibility for the audit trail recorded with my admin id."
        />
      </SettingsSection>

      <SettingsSection title="Actions">
        <div className="flex flex-wrap items-center gap-3">
          <Button
            variant="secondary"
            disabled={submitting}
            onClick={() => void save(false)}
          >
            Save as draft
          </Button>
          <Button
            variant="primary"
            disabled={submitting}
            onClick={() => void save(true)}
          >
            Save and submit for review
          </Button>
          {validationError && (
            <p className="text-xs text-admin-danger" role="status">{validationError}</p>
          )}
        </div>
      </SettingsSection>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}
