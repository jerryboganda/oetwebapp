'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, MessagesSquare, Save, Sparkles } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminConversationAiDraft,
  createAdminConversationTemplate,
  publishAdminConversationTemplate,
  type AdminConversationAiDraftResult,
} from '@/lib/api';
import { buildConversationCreatePayload } from '@/lib/admin/conversation-payload';

const PROFESSION_OPTIONS = [
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'optometry', label: 'Optometry' },
];

const TASK_OPTIONS = [
  { value: 'oet-roleplay', label: 'OET Roleplay' },
  { value: 'oet-handover', label: 'OET Handover' },
];

type ToastState = { variant: 'success' | 'error' | 'warning'; message: string } | null;

export default function ConversationAiDraftPage() {
  const router = useRouter();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublish = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [profession, setProfession] = useState('medicine');
  const [topic, setTopic] = useState('');
  const [scenario, setScenario] = useState('');
  const [durationSeconds, setDurationSeconds] = useState(300);
  const [taskType, setTaskType] = useState<'oet-roleplay' | 'oet-handover'>('oet-roleplay');
  const [generating, setGenerating] = useState(false);
  const [saving, setSaving] = useState(false);
  const [draft, setDraft] = useState<AdminConversationAiDraftResult | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const generate = async () => {
    setGenerating(true);
    setDraft(null);
    try {
      const res = await adminConversationAiDraft({
        profession,
        topic: topic.trim() || undefined,
        scenario: scenario.trim() || undefined,
        durationSeconds,
        taskType,
      });
      setDraft(res);
      if (res.warning) {
        setToast({ variant: 'warning', message: res.warning });
      }
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'AI draft failed.' });
    } finally {
      setGenerating(false);
    }
  };

  const saveDraft = async (publish: boolean) => {
    if (!draft || !canWrite) return;
    setSaving(true);
    try {
      const created = (await createAdminConversationTemplate(
        buildConversationCreatePayload(draft, profession),
      )) as { id: string };
      if (publish && canPublish) {
        await publishAdminConversationTemplate(created.id);
      }
      setToast({ variant: 'success', message: publish ? 'Created and published.' : 'Saved as draft.' });
      router.push(`/admin/content/conversation/${created.id}`);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed.' });
      setSaving(false);
    }
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Conversation AI draft">
      <Link
        href="/admin/content/conversation"
        className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
      >
        <ArrowLeft className="h-4 w-4" /> Back to conversation templates
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Sparkles}
        accent="navy"
        title="AI draft a conversation scenario"
        description="Generates a grounded conversation template — scenario, role, patient context, objectives, red flags, and key vocabulary."
      />

      <AdminRoutePanel title="Parameters">
        <div className="grid gap-3 sm:grid-cols-2">
          <Select
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            label="Profession"
            options={PROFESSION_OPTIONS}
          />
          <Select
            value={taskType}
            onChange={(e) => setTaskType(e.target.value as 'oet-roleplay' | 'oet-handover')}
            label="Task type"
            options={TASK_OPTIONS}
          />
          <Input
            value={topic}
            onChange={(e) => setTopic(e.target.value)}
            label="Topic (optional)"
            placeholder="e.g. medication adherence"
          />
          <Input
            type="number"
            min={120}
            max={600}
            value={durationSeconds}
            onChange={(e) => setDurationSeconds(Number(e.target.value) || 300)}
            label="Duration (seconds)"
          />
          <div className="sm:col-span-2">
            <label className="text-sm font-medium">Scenario hint (optional)</label>
            <textarea
              value={scenario}
              onChange={(e) => setScenario(e.target.value)}
              rows={3}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              placeholder="One or two sentences describing the situation. Optional."
            />
          </div>
        </div>
        <div className="mt-3 flex justify-end">
          <Button
            onClick={() => void generate()}
            disabled={generating || !canWrite}
            className="inline-flex items-center gap-2"
          >
            <Sparkles className="h-4 w-4" /> {generating ? 'Generating…' : 'Generate'}
          </Button>
        </div>
      </AdminRoutePanel>

      {draft && (
        <AdminRoutePanel
          title={draft.title}
          description={`${draft.taskTypeCode} · ${draft.profession} · ${draft.difficulty} · ${draft.estimatedDurationSeconds}s`}
        >
          {draft.warning && (
            <InlineAlert variant="warning" title="AI gateway warning">
              {draft.warning}
            </InlineAlert>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <Card className="p-4">
              <div className="text-xs font-semibold uppercase text-muted">Scenario</div>
              <p className="mt-1 text-sm whitespace-pre-wrap">{draft.scenario}</p>
            </Card>
            <Card className="p-4">
              <div className="text-xs font-semibold uppercase text-muted">Role description</div>
              <p className="mt-1 text-sm whitespace-pre-wrap">{draft.roleDescription}</p>
            </Card>
            <Card className="p-4">
              <div className="text-xs font-semibold uppercase text-muted">Patient context</div>
              <p className="mt-1 text-sm whitespace-pre-wrap">{draft.patientContext}</p>
            </Card>
            <Card className="p-4">
              <div className="text-xs font-semibold uppercase text-muted">Expected outcomes</div>
              <p className="mt-1 text-sm whitespace-pre-wrap">{draft.expectedOutcomes}</p>
            </Card>
            <Card className="p-4 sm:col-span-2">
              <div className="text-xs font-semibold uppercase text-muted">Objectives</div>
              <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">
                {draft.objectives.map((o, i) => <li key={i}>{o}</li>)}
              </ul>
            </Card>
            <Card className="p-4">
              <div className="text-xs font-semibold uppercase text-muted">Expected red flags</div>
              <div className="mt-2 flex flex-wrap gap-1">
                {draft.expectedRedFlags.map((f, i) => (
                  <Badge key={i} variant="danger">{f}</Badge>
                ))}
              </div>
            </Card>
            <Card className="p-4">
              <div className="text-xs font-semibold uppercase text-muted">Key vocabulary</div>
              <div className="mt-2 flex flex-wrap gap-1">
                {draft.keyVocabulary.map((v, i) => (
                  <Badge key={i} variant="info">{v}</Badge>
                ))}
              </div>
            </Card>
          </div>
          <div className="mt-4 flex flex-wrap justify-end gap-2">
            <Button
              variant="outline"
              onClick={() => void saveDraft(false)}
              disabled={saving || !canWrite}
              className="inline-flex items-center gap-2"
            >
              <Save className="h-4 w-4" /> Save as draft
            </Button>
            {canPublish && (
              <Button
                onClick={() => void saveDraft(true)}
                disabled={saving}
                className="inline-flex items-center gap-2"
              >
                <MessagesSquare className="h-4 w-4" /> Save & Publish
              </Button>
            )}
          </div>
        </AdminRoutePanel>
      )}

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : toast.variant === 'warning' ? 'warning' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}
