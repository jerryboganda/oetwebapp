'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, BookMarked, CheckCircle, ClipboardCheck, History, Save, Send, TimerReset } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { Checkbox, Input, Select, Textarea } from '@/components/ui/form-controls';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Toast } from '@/components/ui/alert';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { createAdminContent, publishAdminContent, submitContentForReview, updateAdminContent } from '@/lib/api';
import { getAdminContentDetailData, getAdminContentImpactData, getAdminCriteriaData } from '@/lib/admin';
import { hasPermission, AdminPermission } from '@/lib/admin-permissions';
import type { AdminContentImpact, AdminCriterion } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';

interface AdminContentEditorProps {
  contentId?: string;
}

interface FormState {
  title: string;
  contentType: string;
  subtestCode: string;
  professionId: string;
  difficulty: string;
  estimatedDurationMinutes: string;
  description: string;
  caseNotes: string;
  modelAnswer: string;
  criteriaFocus: string[];
  sourceType: string;
  qaStatus: string;
}

const defaultFormState: FormState = {
  title: '',
  contentType: 'writing_task',
  subtestCode: 'writing',
  professionId: 'nursing',
  difficulty: 'medium',
  estimatedDurationMinutes: '45',
  description: '',
  caseNotes: '',
  modelAnswer: '',
  criteriaFocus: [],
  sourceType: 'original',
  qaStatus: 'pending',
};

export function AdminContentEditor({ contentId }: AdminContentEditorProps) {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const [pageStatus, setPageStatus] = useState<PageStatus>(contentId ? 'loading' : 'success');
  const [activeTab, setActiveTab] = useState('metadata');
  const [form, setForm] = useState<FormState>(defaultFormState);
  const [contentStatus, setContentStatus] = useState<string>('Draft');
  const [criteriaOptions, setCriteriaOptions] = useState<AdminCriterion[]>([]);
  const [impact, setImpact] = useState<AdminContentImpact | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const isNew = !contentId;
  const userPerms = user?.adminPermissions ?? [];
  const canPublish = hasPermission(userPerms, AdminPermission.ContentPublish, AdminPermission.ContentPublisherApproval);

  useEffect(() => {
    if (!contentId) return;
    const currentContentId = contentId;

    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const [detail, impactSummary] = await Promise.all([
          getAdminContentDetailData(currentContentId),
          getAdminContentImpactData(currentContentId),
        ]);

        if (cancelled) return;

        setForm({
          title: detail.title,
          contentType: detail.contentType,
          subtestCode: detail.subtestCode,
          professionId: detail.professionId || 'nursing',
          difficulty: detail.difficulty,
          estimatedDurationMinutes: String(detail.estimatedDurationMinutes),
          description: detail.description,
          caseNotes: detail.caseNotes,
          modelAnswer: detail.modelAnswer,
          criteriaFocus: detail.criteriaFocus,
          sourceType: detail.sourceType || 'original',
          qaStatus: detail.qaStatus || 'pending',
        });
        if (detail.status) setContentStatus(detail.status);
        setImpact(impactSummary);
        setPageStatus('success');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Failed to load content details.' });
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [contentId]);

  useEffect(() => {
    let cancelled = false;
    async function loadCriteria() {
      try {
        const items = await getAdminCriteriaData({ subtest: form.subtestCode, status: 'active' });
        if (!cancelled) {
          setCriteriaOptions(items);
        }
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setCriteriaOptions([]);
        }
      }
    }

    loadCriteria();
    return () => {
      cancelled = true;
    };
  }, [form.subtestCode]);

  const tabs = useMemo(
    () => [
      { id: 'metadata', label: 'Metadata & Content' },
      { id: 'criteria', label: 'Criteria Mapping' },
      { id: 'rubric', label: 'Model Answer & Rubric' },
    ],
    [],
  );

  function updateField<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function toggleCriterion(criterionId: string) {
    setForm((current) => ({
      ...current,
      criteriaFocus: current.criteriaFocus.includes(criterionId)
        ? current.criteriaFocus.filter((id) => id !== criterionId)
        : [...current.criteriaFocus, criterionId],
    }));
  }

  async function saveContent(publishAfterSave: boolean) {
    setIsSaving(true);
    try {
      const payload = {
        title: form.title,
        contentType: form.contentType,
        subtestCode: form.subtestCode,
        professionId: form.professionId,
        difficulty: form.difficulty,
        estimatedDurationMinutes: Number(form.estimatedDurationMinutes || 45),
        description: form.description,
        caseNotes: form.caseNotes,
        modelAnswer: form.modelAnswer,
        criteriaFocus: JSON.stringify(form.criteriaFocus),
        sourceType: form.sourceType,
        qaStatus: form.qaStatus,
      };

      let resolvedContentId = contentId;
      if (isNew) {
        const created = await createAdminContent(payload);
        resolvedContentId = created.id as string;
      } else if (contentId) {
        await updateAdminContent(contentId, payload);
      }

      if (publishAfterSave && resolvedContentId) {
        if (canPublish) {
          await publishAdminContent(resolvedContentId);
          setContentStatus('Published');
        } else {
          throw new Error('Insufficient permissions to publish directly.');
        }
      }

      setToast({
        variant: 'success',
        message: publishAfterSave ? 'Content saved and published.' : 'Draft saved successfully.',
      });

      if (resolvedContentId && resolvedContentId !== contentId) {
        router.replace(`/admin/content/${resolvedContentId}`);
      } else if (resolvedContentId) {
        const impactSummary = await getAdminContentImpactData(resolvedContentId);
        setImpact(impactSummary);
      }
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save content right now.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSubmitForReview() {
    if (!contentId) return;
    setIsSaving(true);
    try {
      // Save first, then submit
      await updateAdminContent(contentId, {
        title: form.title,
        contentType: form.contentType,
        subtestCode: form.subtestCode,
        professionId: form.professionId,
        difficulty: form.difficulty,
        estimatedDurationMinutes: Number(form.estimatedDurationMinutes || 45),
        description: form.description,
        caseNotes: form.caseNotes,
        modelAnswer: form.modelAnswer,
        criteriaFocus: JSON.stringify(form.criteriaFocus),
        sourceType: form.sourceType,
        qaStatus: form.qaStatus,
      });
      await submitContentForReview(contentId);
      setContentStatus('EditorReview');
      setToast({ variant: 'success', message: 'Content submitted for editor review.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to submit for review.' });
    } finally {
      setIsSaving(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Content workspace">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <div className="space-y-5">
        <div className="flex items-start gap-3">
          <Button variant="ghost" size="sm" onClick={() => router.back()} className="mt-1 px-2">
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div className="min-w-0 flex-1">
            <AdminRouteSectionHeader
              title={isNew ? 'Create Content' : `Edit ${form.title || contentId}`}
              description="Build OET practice content with real metadata, live rubric criteria, and a publishable revision trail."
              meta={contentId ? `Content ID ${contentId}` : 'New draft workspace'}
              highlights={[
                {
                  label: 'QA Status',
                  value: form.qaStatus.replace(/_/g, ' '),
                  icon: ClipboardCheck,
                },
                {
                  label: 'Criteria Linked',
                  value: `${form.criteriaFocus.length} selected`,
                  icon: BookMarked,
                },
                {
                  label: 'Estimated Time',
                  value: `${form.estimatedDurationMinutes || '45'} mins`,
                  icon: TimerReset,
                },
              ]}
              actions={
                <>
                  {/* Status pipeline stepper */}
                  {!isNew && contentId ? (
                    <div className="flex flex-wrap items-center gap-1 text-xs mr-2">
                      {(['Draft', 'EditorReview', 'PublisherApproval', 'Published'] as const).map((step, i) => {
                        const labels: Record<string, string> = { Draft: 'Draft', EditorReview: 'Editor', PublisherApproval: 'Publisher', Published: 'Published' };
                        const isActive = contentStatus === step;
                        const isPast = ['Draft', 'EditorReview', 'PublisherApproval', 'Published'].indexOf(contentStatus) > i;
                        return (
                          <span key={step} className="flex items-center gap-1">
                            {i > 0 ? <span className="text-[var(--color-border)]">→</span> : null}
                            <span className={`px-2 py-0.5 rounded-full ${isActive ? 'bg-[var(--color-accent)] text-white' : isPast ? 'bg-[var(--color-success-bg)] text-[var(--color-success)]' : 'bg-[var(--color-surface-secondary)] text-[var(--color-text-tertiary)]'}`}>
                              {labels[step]}
                            </span>
                          </span>
                        );
                      })}
                      {contentStatus === 'Rejected' ? (
                        <span className="ml-1 px-2 py-0.5 rounded-full bg-[var(--color-error-bg)] text-[var(--color-error)] text-xs">Rejected</span>
                      ) : null}
                    </div>
                  ) : null}
                  {!isNew && contentId ? (
                    <Button variant="outline" onClick={() => router.push(`/admin/content/${contentId}/revisions`)} className="gap-2">
                      <History className="h-4 w-4" /> Revisions
                    </Button>
                  ) : null}
                  <Button variant="outline" onClick={() => saveContent(false)} loading={isSaving} className="gap-2">
                    <Save className="h-4 w-4" /> Save Draft
                  </Button>
                  {!isNew && contentId && (contentStatus === 'Draft' || contentStatus === 'Rejected') ? (
                    <Button variant="outline" onClick={handleSubmitForReview} loading={isSaving} className="gap-2">
                      <Send className="h-4 w-4" /> Submit for Review
                    </Button>
                  ) : null}
                  {canPublish ? (
                    <Button onClick={() => saveContent(true)} loading={isSaving} className="gap-2">
                      <CheckCircle className="h-4 w-4" /> Publish
                    </Button>
                  ) : null}
                </>
              }
            />
          </div>
        </div>
      </div>

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        <div className="space-y-6">
          {impact ? (
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard label="Attempts" value={impact.usage.attemptCount} hint="Learner sessions linked to this content." icon={BookMarked} />
              <AdminRouteSummaryCard label="Evaluations" value={impact.usage.evaluationCount} hint="AI or expert evaluations completed in-window." icon={ClipboardCheck} />
              <AdminRouteSummaryCard label="Study Plan References" value={impact.usage.studyPlanReferences} hint="Study plans currently surfacing this item." icon={TimerReset} />
              <AdminRouteSummaryCard
                label="Archive Safety"
                value={impact.safeToArchive ? 'Safe' : 'Live usage'}
                hint={impact.safeToArchive ? 'No active learner dependencies detected.' : 'This content is still attached to live learner journeys.'}
                tone={impact.safeToArchive ? 'success' : 'warning'}
                icon={CheckCircle}
              />
            </div>
          ) : null}

          <Tabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} />

        <TabPanel id="metadata" activeTab={activeTab}>
          <AdminRoutePanel
            title="Core Content Metadata"
            description="Set the learner-facing metadata, timing, and prompt material that define how this item appears and behaves."
          >
              <Input label="Content Title" value={form.title} onChange={(event) => updateField('title', event.target.value)} />
              <div className="grid gap-4 md:grid-cols-2">
                <Select
                  label="Content Type"
                  value={form.contentType}
                  onChange={(event) => updateField('contentType', event.target.value)}
                  options={[
                    { value: 'writing_task', label: 'Writing Task' },
                    { value: 'speaking_task', label: 'Speaking Task' },
                    { value: 'reading_task', label: 'Reading Task' },
                    { value: 'listening_task', label: 'Listening Task' },
                  ]}
                />
                <Select
                  label="Subtest"
                  value={form.subtestCode}
                  onChange={(event) => updateField('subtestCode', event.target.value)}
                  options={[
                    { value: 'writing', label: 'Writing' },
                    { value: 'speaking', label: 'Speaking' },
                    { value: 'reading', label: 'Reading' },
                    { value: 'listening', label: 'Listening' },
                  ]}
                />
              </div>
              <div className="grid gap-4 md:grid-cols-3">
                <Select
                  label="Profession"
                  value={form.professionId}
                  onChange={(event) => updateField('professionId', event.target.value)}
                  options={[
                    { value: 'nursing', label: 'Nursing' },
                    { value: 'medicine', label: 'Medicine' },
                    { value: 'dentistry', label: 'Dentistry' },
                    { value: 'pharmacy', label: 'Pharmacy' },
                    { value: 'physiotherapy', label: 'Physiotherapy' },
                  ]}
                />
                <Select
                  label="Difficulty"
                  value={form.difficulty}
                  onChange={(event) => updateField('difficulty', event.target.value)}
                  options={[
                    { value: 'easy', label: 'Easy' },
                    { value: 'medium', label: 'Medium' },
                    { value: 'hard', label: 'Hard' },
                  ]}
                />
                <Input
                  label="Estimated Minutes"
                  type="number"
                  min={5}
                  max={240}
                  value={form.estimatedDurationMinutes}
                  onChange={(event) => updateField('estimatedDurationMinutes', event.target.value)}
                />
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <Select
                  label="Source Type"
                  value={form.sourceType}
                  onChange={(event) => updateField('sourceType', event.target.value)}
                  options={[
                    { value: 'original', label: 'Original' },
                    { value: 'adapted', label: 'Adapted' },
                    { value: 'ai_generated', label: 'AI-Generated' },
                    { value: 'community', label: 'Community Contributed' },
                    { value: 'licensed', label: 'Licensed Material' },
                  ]}
                />
                <Select
                  label="QA Status"
                  value={form.qaStatus}
                  onChange={(event) => updateField('qaStatus', event.target.value)}
                  options={[
                    { value: 'pending', label: 'Pending Review' },
                    { value: 'approved', label: 'Approved' },
                    { value: 'rejected', label: 'Rejected' },
                    { value: 'needs_revision', label: 'Needs Revision' },
                  ]}
                />
              </div>
              <Textarea
                label="Learner-Facing Description"
                value={form.description}
                onChange={(event) => updateField('description', event.target.value)}
                className="min-h-[120px]"
              />
              <Textarea
                label="Case Notes / Prompt"
                value={form.caseNotes}
                onChange={(event) => updateField('caseNotes', event.target.value)}
                className="min-h-[220px]"
              />
          </AdminRoutePanel>
        </TabPanel>

        <TabPanel id="criteria" activeTab={activeTab}>
          <AdminRoutePanel
            title="Criteria Mapping"
            description="Criteria are loaded live from the rubric library for the selected subtest so editorial decisions stay aligned with the learner evaluation model."
          >
              {criteriaOptions.length === 0 ? (
                <EmptyState
                  title="No active criteria available"
                  description="Activate rubric criteria for this subtest first, then return here to map the content."
                />
              ) : (
                <div className="grid gap-3 md:grid-cols-2">
                  {criteriaOptions.map((criterion) => (
                    <div key={criterion.id} className="rounded-[20px] border border-border bg-background-light p-4 shadow-sm">
                      <Checkbox
                        checked={form.criteriaFocus.includes(criterion.id)}
                        onChange={() => toggleCriterion(criterion.id)}
                        label={`${criterion.name} (${criterion.weight})`}
                      />
                      <p className="mt-3 text-sm leading-6 text-muted">{criterion.description}</p>
                    </div>
                  ))}
                </div>
              )}
          </AdminRoutePanel>
        </TabPanel>

        <TabPanel id="rubric" activeTab={activeTab}>
          <AdminRoutePanel
            title="Model Answer & Internal Notes"
            description="Store the internal reference answer or structured response payload used by reviewers, AI evaluation, and future revisions."
          >
              <Textarea
                label="Model Answer JSON / Reference Text"
                value={form.modelAnswer}
                onChange={(event) => updateField('modelAnswer', event.target.value)}
                className="min-h-[240px]"
              />
              <p className="text-sm leading-6 text-muted">
                Use structured JSON when the content type already expects it, or plain reference prose while editorial work is in progress.
              </p>
          </AdminRoutePanel>
        </TabPanel>
        </div>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
