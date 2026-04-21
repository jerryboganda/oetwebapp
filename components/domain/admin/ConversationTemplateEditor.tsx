'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Save, Send, Archive } from 'lucide-react';
import { AdminDashboardShell } from '@/components/layout';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import {
  fetchAdminConversationTemplate,
  createAdminConversationTemplate,
  updateAdminConversationTemplate,
  publishAdminConversationTemplate,
  archiveAdminConversationTemplate,
} from '@/lib/api';

interface TemplateForm {
  title: string;
  taskTypeCode: 'oet-roleplay' | 'oet-handover';
  professionId: string;
  difficulty: 'easy' | 'medium' | 'hard';
  scenario: string;
  roleDescription: string;
  patientContext: string;
  expectedOutcomes: string;
  objectives: string[];
  expectedRedFlags: string[];
  keyVocabulary: string[];
  patientVoiceGender: string;
  patientVoiceAge: string;
  patientVoiceAccent: string;
  patientVoiceTone: string;
  estimatedDurationSeconds: number;
}

const EMPTY: TemplateForm = {
  title: '', taskTypeCode: 'oet-roleplay', professionId: 'medicine', difficulty: 'medium',
  scenario: '', roleDescription: '', patientContext: '', expectedOutcomes: '',
  objectives: [''], expectedRedFlags: [''], keyVocabulary: [''],
  patientVoiceGender: 'female', patientVoiceAge: '45',
  patientVoiceAccent: 'en-GB', patientVoiceTone: 'neutral',
  estimatedDurationSeconds: 300,
};

interface Props { templateId?: string }

export function ConversationTemplateEditor({ templateId }: Props) {
  const router = useRouter();
  const [form, setForm] = useState<TemplateForm>(EMPTY);
  const [loading, setLoading] = useState(!!templateId);
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState<string>('draft');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    if (!templateId) return;
    fetchAdminConversationTemplate(templateId)
      .then((data: unknown) => {
        const d = data as Record<string, unknown>;
        setForm({
          title: (d.title as string) ?? '',
          taskTypeCode: ((d.taskTypeCode as 'oet-roleplay' | 'oet-handover') ?? 'oet-roleplay'),
          professionId: ((d.professionId as string) ?? 'medicine'),
          difficulty: ((d.difficulty as 'easy' | 'medium' | 'hard') ?? 'medium'),
          scenario: (d.scenario as string) ?? '',
          roleDescription: (d.roleDescription as string) ?? '',
          patientContext: (d.patientContext as string) ?? '',
          expectedOutcomes: (d.expectedOutcomes as string) ?? '',
          objectives: Array.isArray(d.objectives) ? (d.objectives as string[]) : [''],
          expectedRedFlags: Array.isArray(d.expectedRedFlags) ? (d.expectedRedFlags as string[]) : [''],
          keyVocabulary: Array.isArray(d.keyVocabulary) ? (d.keyVocabulary as string[]) : [''],
          patientVoiceGender: String((d.patientVoice as Record<string, unknown>)?.gender ?? 'female'),
          patientVoiceAge: String((d.patientVoice as Record<string, unknown>)?.age ?? '45'),
          patientVoiceAccent: String((d.patientVoice as Record<string, unknown>)?.accent ?? 'en-GB'),
          patientVoiceTone: String((d.patientVoice as Record<string, unknown>)?.tone ?? 'neutral'),
          estimatedDurationSeconds: Number(d.estimatedDurationSeconds ?? 300),
        });
        setStatus(String(d.status ?? 'draft'));
      })
      .catch(() => setToast({ variant: 'error', message: 'Failed to load template.' }))
      .finally(() => setLoading(false));
  }, [templateId]);

  async function handleSave(): Promise<string | null> {
    setSaving(true);
    const body = {
      title: form.title, taskTypeCode: form.taskTypeCode, professionId: form.professionId,
      difficulty: form.difficulty, scenario: form.scenario, roleDescription: form.roleDescription,
      patientContext: form.patientContext, expectedOutcomes: form.expectedOutcomes,
      estimatedDurationSeconds: form.estimatedDurationSeconds,
      objectives: form.objectives.map((s) => s.trim()).filter(Boolean),
      expectedRedFlags: form.expectedRedFlags.map((s) => s.trim()).filter(Boolean),
      keyVocabulary: form.keyVocabulary.map((s) => s.trim()).filter(Boolean),
      patientVoice: {
        gender: form.patientVoiceGender,
        age: Number(form.patientVoiceAge) || null,
        accent: form.patientVoiceAccent, tone: form.patientVoiceTone,
      },
    };
    try {
      if (templateId) {
        await updateAdminConversationTemplate(templateId, body);
        setToast({ variant: 'success', message: 'Template saved.' });
        return templateId;
      }
      const created = (await createAdminConversationTemplate(body)) as { id: string };
      setToast({ variant: 'success', message: 'Template created.' });
      router.replace(`/admin/content/conversation/${created.id}`);
      return created.id;
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed.' });
      return null;
    } finally { setSaving(false); }
  }

  async function handlePublish() {
    const id = templateId ?? (await handleSave());
    if (!id) return;
    try {
      await publishAdminConversationTemplate(id);
      setStatus('published');
      setToast({ variant: 'success', message: 'Template published.' });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish failed.' });
    }
  }

  async function handleArchive() {
    if (!templateId) return;
    try {
      await archiveAdminConversationTemplate(templateId);
      setStatus('archived');
      setToast({ variant: 'success', message: 'Template archived.' });
    } catch { setToast({ variant: 'error', message: 'Archive failed.' }); }
  }

  function updateListItem(field: 'objectives' | 'expectedRedFlags' | 'keyVocabulary', index: number, value: string) {
    setForm((prev) => ({ ...prev, [field]: prev[field].map((v, i) => (i === index ? value : v)) }));
  }
  function addListItem(field: 'objectives' | 'expectedRedFlags' | 'keyVocabulary') {
    setForm((prev) => ({ ...prev, [field]: [...prev[field], ''] }));
  }
  function removeListItem(field: 'objectives' | 'expectedRedFlags' | 'keyVocabulary', index: number) {
    setForm((prev) => ({ ...prev, [field]: prev[field].filter((_, i) => i !== index) }));
  }

  if (loading) {
    return (
      <AdminDashboardShell>
        <AdminRouteWorkspace><AdminRoutePanel>Loading…</AdminRoutePanel></AdminRouteWorkspace>
      </AdminDashboardShell>
    );
  }

  return (
    <AdminDashboardShell>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Content"
            title={templateId ? 'Edit scenario' : 'New scenario'}
            description="Scenarios drive the AI Conversation role-play and handover modes."
            actions={
              <div className="flex items-center gap-2">
                <Badge variant={status === 'published' ? 'success' : status === 'archived' ? 'muted' : 'info'} size="sm">
                  {status}
                </Badge>
                <Button variant="secondary" onClick={() => router.push('/admin/content/conversation')}>
                  <ArrowLeft className="mr-1 h-4 w-4" /> Back
                </Button>
                <Button variant="secondary" onClick={handleSave} disabled={saving}>
                  <Save className="mr-1 h-4 w-4" /> {saving ? 'Saving…' : 'Save draft'}
                </Button>
                <Button variant="primary" onClick={handlePublish} disabled={saving}>
                  <Send className="mr-1 h-4 w-4" /> Publish
                </Button>
                {templateId && status !== 'archived' && (
                  <Button variant="secondary" onClick={handleArchive}>
                    <Archive className="mr-1 h-4 w-4" /> Archive
                  </Button>
                )}
              </div>
            }
          />

          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            <Input label="Title" value={form.title} onChange={(e) => setForm((p) => ({ ...p, title: e.target.value }))} />
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Task Type</label>
              <select value={form.taskTypeCode}
                onChange={(e) => setForm((p) => ({ ...p, taskTypeCode: e.target.value as 'oet-roleplay' | 'oet-handover' }))}
                className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="oet-roleplay">OET Clinical Role Play</option>
                <option value="oet-handover">OET Handover</option>
              </select>
            </div>
            <Input label="Profession" value={form.professionId}
              onChange={(e) => setForm((p) => ({ ...p, professionId: e.target.value }))}
              placeholder="medicine, nursing, pharmacy, physiotherapy, dentistry" />
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Difficulty</label>
              <select value={form.difficulty}
                onChange={(e) => setForm((p) => ({ ...p, difficulty: e.target.value as 'easy' | 'medium' | 'hard' }))}
                className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="easy">Easy</option><option value="medium">Medium</option><option value="hard">Hard</option>
              </select>
            </div>
            <Input label="Estimated Duration (seconds)" type="number" value={String(form.estimatedDurationSeconds)}
              onChange={(e) => setForm((p) => ({ ...p, estimatedDurationSeconds: Number(e.target.value) || 300 }))} />
          </div>

          <div className="mt-6 grid grid-cols-1 gap-6">
            <Textarea label="Scenario (candidate-facing context)" value={form.scenario}
              onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) => setForm((p) => ({ ...p, scenario: e.target.value }))}
              rows={4} />
            <Textarea label="Role Description" value={form.roleDescription}
              onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) => setForm((p) => ({ ...p, roleDescription: e.target.value }))}
              rows={3} />
            <Textarea label="Patient / Colleague Context" value={form.patientContext}
              onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) => setForm((p) => ({ ...p, patientContext: e.target.value }))}
              rows={3} />
            <Textarea label="Expected Outcomes" value={form.expectedOutcomes}
              onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) => setForm((p) => ({ ...p, expectedOutcomes: e.target.value }))}
              rows={2} />
          </div>

          <ListEditor label="Objectives (min 3 for publish gate)" values={form.objectives}
            onChange={(i, v) => updateListItem('objectives', i, v)}
            onAdd={() => addListItem('objectives')}
            onRemove={(i) => removeListItem('objectives', i)} />

          <ListEditor label="Expected Red Flags" values={form.expectedRedFlags}
            onChange={(i, v) => updateListItem('expectedRedFlags', i, v)}
            onAdd={() => addListItem('expectedRedFlags')}
            onRemove={(i) => removeListItem('expectedRedFlags', i)} />

          <ListEditor label="Key Vocabulary" values={form.keyVocabulary}
            onChange={(i, v) => updateListItem('keyVocabulary', i, v)}
            onAdd={() => addListItem('keyVocabulary')}
            onRemove={(i) => removeListItem('keyVocabulary', i)} />

          <div className="mt-6 grid grid-cols-2 gap-6 md:grid-cols-4">
            <Input label="Voice gender" value={form.patientVoiceGender}
              onChange={(e) => setForm((p) => ({ ...p, patientVoiceGender: e.target.value }))} />
            <Input label="Voice age" type="number" value={form.patientVoiceAge}
              onChange={(e) => setForm((p) => ({ ...p, patientVoiceAge: e.target.value }))} />
            <Input label="Voice accent" value={form.patientVoiceAccent}
              onChange={(e) => setForm((p) => ({ ...p, patientVoiceAccent: e.target.value }))} />
            <Input label="Voice tone" value={form.patientVoiceTone}
              onChange={(e) => setForm((p) => ({ ...p, patientVoiceTone: e.target.value }))} />
          </div>
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </AdminDashboardShell>
  );
}

interface ListEditorProps {
  label: string;
  values: string[];
  onChange: (index: number, value: string) => void;
  onAdd: () => void;
  onRemove: (index: number) => void;
}

function ListEditor({ label, values, onChange, onAdd, onRemove }: ListEditorProps) {
  return (
    <div className="mt-6">
      <div className="mb-2 flex items-center justify-between">
        <label className="text-xs font-medium uppercase tracking-wide text-muted">{label}</label>
        <Button variant="secondary" onClick={onAdd}>Add</Button>
      </div>
      <div className="space-y-2">
        {values.map((value, i) => (
          <div key={i} className="flex items-center gap-2">
            <input value={value} onChange={(e) => onChange(i, e.target.value)}
              className="flex-1 rounded-lg border border-border bg-surface px-3 py-2 text-sm" />
            <Button variant="secondary" onClick={() => onRemove(i)}>Remove</Button>
          </div>
        ))}
      </div>
    </div>
  );
}
