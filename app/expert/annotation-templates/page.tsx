'use client';

import { useEffect, useState } from 'react';
import { FileEdit, Plus, Pencil, Trash2, Users2 } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { getAnnotationTemplatesData } from '@/lib/learner-data';
import { createAnnotationTemplate, updateAnnotationTemplate, deleteAnnotationTemplate } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ExpertAnnotationTemplate } from '@/lib/types/learner';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type ModalMode = 'create' | 'edit';

const SUBTESTS = ['writing', 'speaking', 'reading', 'listening'];
const CRITERIA: Record<string, string[]> = {
  writing: ['overall_task_fulfilment', 'appropriateness_of_language', 'comprehension_of_stimulus', 'linguistic_features_(grammar_and_cohesion)', 'linguistic_features_(vocabulary)', 'presentation_features'],
  speaking: ['intelligibility', 'fluency', 'appropriateness_of_language', 'resources_of_grammar_and_expression', 'relationship_building'],
};

export default function AnnotationTemplatesPage() {
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [templates, setTemplates] = useState<ExpertAnnotationTemplate[]>([]);
  const [toast, setToast] = useState<ToastState>(null);
  const [isMutating, setIsMutating] = useState(false);

  // Filters
  const [filterSubtest, setFilterSubtest] = useState<string>('');
  const [filterCriterion, setFilterCriterion] = useState<string>('');

  // Modal state
  const [showModal, setShowModal] = useState(false);
  const [modalMode, setModalMode] = useState<ModalMode>('create');
  const [editTarget, setEditTarget] = useState<ExpertAnnotationTemplate | null>(null);
  const [form, setForm] = useState({ subtestCode: 'writing', criterionCode: '', label: '', templateText: '', isShared: false });

  async function loadTemplates() {
    setPageStatus('loading');
    try {
      const data = await getAnnotationTemplatesData({
        subtestCode: filterSubtest || undefined,
        criterionCode: filterCriterion || undefined,
      });
      setTemplates(data);
      setPageStatus(data.length > 0 ? 'success' : 'empty');
    } catch {
      setPageStatus('error');
    }
  }

  useEffect(() => {
    analytics.track('content_view', { page: 'annotation-templates' });
    loadTemplates();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterSubtest, filterCriterion]);

  function openCreate() {
    setModalMode('create');
    setEditTarget(null);
    setForm({ subtestCode: 'writing', criterionCode: '', label: '', templateText: '', isShared: false });
    setShowModal(true);
  }

  function openEdit(template: ExpertAnnotationTemplate) {
    setModalMode('edit');
    setEditTarget(template);
    setForm({
      subtestCode: template.subtestCode,
      criterionCode: template.criterionCode,
      label: template.label,
      templateText: template.templateText,
      isShared: template.isShared,
    });
    setShowModal(true);
  }

  async function handleSave() {
    if (!form.label.trim() || !form.templateText.trim()) {
      setToast({ variant: 'error', message: 'Label and template text are required.' });
      return;
    }
    setIsMutating(true);
    try {
      if (modalMode === 'create') {
        await createAnnotationTemplate(form);
        setToast({ variant: 'success', message: 'Template created.' });
      } else if (editTarget) {
        await updateAnnotationTemplate(editTarget.id, form);
        setToast({ variant: 'success', message: 'Template updated.' });
      }
      setShowModal(false);
      await loadTemplates();
    } catch {
      setToast({ variant: 'error', message: `Failed to ${modalMode} template.` });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleDelete(templateId: string) {
    setIsMutating(true);
    try {
      await deleteAnnotationTemplate(templateId);
      setTemplates((prev) => prev.filter((t) => t.id !== templateId));
      setToast({ variant: 'success', message: 'Template deleted.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to delete template.' });
    } finally {
      setIsMutating(false);
    }
  }

  const criteriaOptions = CRITERIA[form.subtestCode] ?? [];

  return (
    <div className="space-y-6 p-6">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <LearnerPageHero
        title="Annotation Templates"
        subtitle="Manage reusable feedback templates for your expert reviews."
        icon={<FileEdit className="w-7 h-7" />}
      />

      {/* Filters + Create */}
      <div className="flex flex-wrap items-center gap-3">
        <Select
          value={filterSubtest}
          onChange={(e) => { setFilterSubtest(e.target.value); setFilterCriterion(''); }}
        >
          <option value="">All subtests</option>
          {SUBTESTS.map((s) => <option key={s} value={s}>{s.charAt(0).toUpperCase() + s.slice(1)}</option>)}
        </Select>
        {filterSubtest && CRITERIA[filterSubtest] && (
          <Select value={filterCriterion} onChange={(e) => setFilterCriterion(e.target.value)}>
            <option value="">All criteria</option>
            {CRITERIA[filterSubtest].map((c) => (
              <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>
            ))}
          </Select>
        )}
        <Button onClick={openCreate} className="ml-auto">
          <Plus className="w-4 h-4 mr-1" /> New Template
        </Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        loadingMessage="Loading templates…"
        errorMessage="Unable to load annotation templates."
        emptySlot={
          <EmptyState
            icon={<FileEdit className="w-12 h-12 text-gray-400" />}
            title="No templates yet"
            description="Create your first annotation template to speed up reviews."
          />
        }
      >
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {templates.map((t) => (
            <Card key={t.id} className="p-4 flex flex-col">
              <div className="flex items-start justify-between mb-2">
                <div>
                  <h3 className="font-semibold text-sm text-gray-900 dark:text-gray-100">{t.label}</h3>
                  <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                    {t.subtestCode} &middot; {t.criterionCode.replace(/_/g, ' ')}
                  </p>
                </div>
                <div className="flex items-center gap-1">
                  {t.isShared && (
                    <Badge variant="outline" className="text-xs"><Users2 className="w-3 h-3 mr-0.5" />Shared</Badge>
                  )}
                </div>
              </div>
              <p className="text-sm text-gray-700 dark:text-gray-300 flex-1 line-clamp-3">{t.templateText}</p>
              <div className="mt-3 flex items-center justify-between">
                <span className="text-xs text-gray-400">Used {t.usageCount}×</span>
                <div className="flex items-center gap-1">
                  <Button size="sm" variant="ghost" onClick={() => openEdit(t)}>
                    <Pencil className="w-3.5 h-3.5" />
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => handleDelete(t.id)} disabled={isMutating}>
                    <Trash2 className="w-3.5 h-3.5 text-red-500" />
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      </AsyncStateWrapper>

      {/* Create/Edit Modal */}
      {showModal && (
        <Modal title={modalMode === 'create' ? 'New Template' : 'Edit Template'} onClose={() => setShowModal(false)}>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Subtest</label>
              <Select value={form.subtestCode} onChange={(e) => setForm({ ...form, subtestCode: e.target.value, criterionCode: '' })}>
                {SUBTESTS.map((s) => <option key={s} value={s}>{s.charAt(0).toUpperCase() + s.slice(1)}</option>)}
              </Select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Criterion</label>
              <Select value={form.criterionCode} onChange={(e) => setForm({ ...form, criterionCode: e.target.value })}>
                <option value="">Select criterion</option>
                {criteriaOptions.map((c) => <option key={c} value={c}>{c.replace(/_/g, ' ')}</option>)}
              </Select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Label</label>
              <Input value={form.label} onChange={(e) => setForm({ ...form, label: e.target.value })} placeholder="e.g., Cohesion Issue" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Template Text</label>
              <textarea
                className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 p-3 text-sm text-gray-700 dark:text-gray-300 min-h-[100px] focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                value={form.templateText}
                onChange={(e) => setForm({ ...form, templateText: e.target.value })}
                placeholder="The cohesive devices used here could be improved by…"
              />
            </div>
            <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
              <input
                type="checkbox"
                checked={form.isShared}
                onChange={(e) => setForm({ ...form, isShared: e.target.checked })}
                className="rounded border-gray-300"
              />
              Share with other experts
            </label>
            <div className="flex justify-end gap-2 pt-2">
              <Button variant="outline" onClick={() => setShowModal(false)}>Cancel</Button>
              <Button onClick={handleSave} disabled={isMutating}>
                {isMutating ? 'Saving…' : modalMode === 'create' ? 'Create' : 'Update'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
