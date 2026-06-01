'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowRight, BookOpen, Copy, FileText, HelpCircle, Loader2, ShieldCheck, Pencil, Eye } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Button } from '@/components/admin/ui/button';
import { Badge, statusToTone, type BadgeTone } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import { ReadingManifestPanel } from './ReadingManifestPanel';
import {
  getReadingStructureAdmin,
  cloneReadingPaper,
  validateReadingPaper,
  type ReadingStructureAdminDto,
  type ReadingValidationReport,
} from '@/lib/reading-authoring-api';
import {
  updateContentPaper,
  getContentPaper,
  type ContentPaperDto,
} from '@/lib/content-upload-api';

export default function AdminReadingPaperOverviewPage() {
  const params = useParams<{ paperId: string }>();
  const router = useRouter();
  const paperId = params?.paperId ?? '';

  const [structure, setStructure] = useState<ReadingStructureAdminDto | null>(null);
  const [validation, setValidation] = useState<ReadingValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [editingMeta, setEditingMeta] = useState(false);
  const [metaForm, setMetaForm] = useState({ title: '', difficulty: '', estimatedDurationMinutes: 60, sourceProvenance: '' });
  const [savingMeta, setSavingMeta] = useState(false);
  const [cloning, setCloning] = useState(false);
  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null);

  useEffect(() => {
    if (!paperId) return;

    let cancelled = false;

    async function load() {
      setLoading(true);
      setError(null);
      try {
        const structureData = await getReadingStructureAdmin(paperId);
        if (cancelled) return;
        setStructure(structureData);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Failed to load paper structure');
      }

      try {
        const report = await validateReadingPaper(paperId);
        if (cancelled) return;
        setValidation(report);
      } catch {
        // Validation may fail for drafts — that's fine
      }

      if (!cancelled) setLoading(false);
    }

    load();
    return () => { cancelled = true; };
  }, [paperId]);

  useEffect(() => {
    if (!paperId) return;
    getContentPaper(paperId).then((p) => {
      setPaper(p);
      setMetaForm({
        title: p.title ?? '',
        difficulty: p.difficulty ?? '',
        estimatedDurationMinutes: p.estimatedDurationMinutes ?? 60,
        sourceProvenance: p.sourceProvenance ?? '',
      });
    }).catch(() => { /* paper may not exist yet */ });
  }, [paperId]);

  const reloadStructure = useCallback(async () => {
    if (!paperId) return;
    try {
      const structureData = await getReadingStructureAdmin(paperId);
      setStructure(structureData);
    } catch { /* ignore */ }
    try {
      const report = await validateReadingPaper(paperId);
      setValidation(report);
    } catch { /* validation may fail for drafts */ }
  }, [paperId]);

  async function handleSaveMeta() {
    setSavingMeta(true);
    try {
      const updated = await updateContentPaper(paperId, {
        title: metaForm.title.trim() || undefined,
        difficulty: metaForm.difficulty || null,
        estimatedDurationMinutes: metaForm.estimatedDurationMinutes,
        sourceProvenance: metaForm.sourceProvenance.trim() || null,
      });
      setPaper(updated);
      setEditingMeta(false);
      setToast({ message: 'Metadata saved', variant: 'success' });
    } catch (err) {
      setToast({ message: err instanceof Error ? err.message : 'Save failed', variant: 'error' });
    } finally {
      setSavingMeta(false);
    }
  }

  async function handleClonePaper() {
    if (!paperId || cloning) return;
    setCloning(true);
    try {
      const cloned = await cloneReadingPaper(paperId, { resetReviewState: true });
      setToast({ message: 'Draft revision created', variant: 'success' });
      router.push(cloned.adminRoute);
    } catch (err) {
      setToast({ message: err instanceof Error ? err.message : 'Clone failed', variant: 'error' });
      setCloning(false);
    }
  }

  const partASummary = structure?.parts.find((p) => p.partCode === 'A');
  const partBSummary = structure?.parts.find((p) => p.partCode === 'B');
  const partCSummary = structure?.parts.find((p) => p.partCode === 'C');

  const totalQuestions =
    (partASummary?.questions.length ?? 0) +
    (partBSummary?.questions.length ?? 0) +
    (partCSummary?.questions.length ?? 0);

  const errorCount = validation?.issues.filter((i) => i.severity === 'error').length ?? 0;
  const warningCount = validation?.issues.filter((i) => i.severity === 'warning').length ?? 0;

  const paperStatusTone: BadgeTone = paper?.status ? statusToTone(paper.status) : 'default';

  return (
    <>
      <AdminSettingsLayout
        title={paper?.title ?? 'Reading Paper Overview'}
        description="Summary of the reading paper structure and validation status."
        eyebrow="Reading authoring"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Overview' },
        ]}
        actions={
          paper?.status ? (
            <Badge variant={paperStatusTone}>{paper.status}</Badge>
          ) : undefined
        }
      >
        <div className="space-y-6">
          <ReadingWizardSteps paperId={paperId} currentStep="metadata" />

          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

          {loading ? (
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {[0, 1, 2, 3].map((i) => (
                <Skeleton key={i} variant="card" />
              ))}
            </div>
          ) : structure ? (
            <>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <KpiTile
                  label="Part A"
                  value={partASummary?.questions.length ?? 0}
                  tone="primary"
                  icon={<FileText className="h-4 w-4" />}
                  size="sm"
                />
                <KpiTile
                  label="Part B"
                  value={partBSummary?.questions.length ?? 0}
                  tone="info"
                  icon={<BookOpen className="h-4 w-4" />}
                  size="sm"
                />
                <KpiTile
                  label="Part C"
                  value={partCSummary?.questions.length ?? 0}
                  tone="warning"
                  icon={<HelpCircle className="h-4 w-4" />}
                  size="sm"
                />
                <KpiTile
                  label="Total"
                  value={`${totalQuestions} / 42`}
                  tone={totalQuestions === 42 ? 'success' : 'default'}
                  icon={<ShieldCheck className="h-4 w-4" />}
                  size="sm"
                />
              </div>

              {validation ? (
                <SettingsSection
                  title="Validation status"
                  description="Last-run validation summary. Re-run from the Validate step."
                >
                  <div className="flex flex-wrap items-center gap-2">
                    {validation.isPublishReady ? (
                      <Badge variant="success">Publish Ready</Badge>
                    ) : (
                      <Badge variant="danger">Not Ready</Badge>
                    )}
                    {errorCount > 0 && (
                      <Badge variant="danger">
                        {errorCount} error{errorCount !== 1 ? 's' : ''}
                      </Badge>
                    )}
                    {warningCount > 0 && (
                      <Badge variant="warning">
                        {warningCount} warning{warningCount !== 1 ? 's' : ''}
                      </Badge>
                    )}
                    {errorCount === 0 && warningCount === 0 && validation.isPublishReady && (
                      <span className="text-sm text-admin-fg-muted">All checks passed</span>
                    )}
                  </div>
                </SettingsSection>
              ) : null}

              <SettingsSection
                title="Paper metadata"
                description="Authoring details. Edit and save to update the underlying content paper."
                actions={
                  !editingMeta ? (
                    <Button variant="ghost" size="sm" onClick={() => setEditingMeta(true)} startIcon={<Pencil className="h-3.5 w-3.5" />}>
                      Edit
                    </Button>
                  ) : undefined
                }
              >
                {!editingMeta ? (
                  <dl className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
                    <div>
                      <dt className="text-xs uppercase tracking-wider text-admin-fg-muted">Title</dt>
                      <dd className="mt-1 text-admin-fg-strong">{paper?.title ?? '-'}</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wider text-admin-fg-muted">Difficulty</dt>
                      <dd className="mt-1 text-admin-fg-strong capitalize">{paper?.difficulty ?? '-'}</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wider text-admin-fg-muted">Duration</dt>
                      <dd className="mt-1 text-admin-fg-strong">{paper?.estimatedDurationMinutes ?? 60} min</dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wider text-admin-fg-muted">Source</dt>
                      <dd className="mt-1 text-admin-fg-strong">{paper?.sourceProvenance ?? '-'}</dd>
                    </div>
                  </dl>
                ) : (
                  <div className="space-y-4">
                    <Input
                      label="Title"
                      value={metaForm.title}
                      onChange={(e) => setMetaForm({ ...metaForm, title: e.target.value })}
                    />
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                      <Select
                        label="Difficulty"
                        value={metaForm.difficulty}
                        onChange={(e) => setMetaForm({ ...metaForm, difficulty: e.target.value })}
                        options={[
                          { value: '', label: 'Select…' },
                          { value: 'easy', label: 'Easy' },
                          { value: 'medium', label: 'Medium' },
                          { value: 'hard', label: 'Hard' },
                        ]}
                      />
                      <Input
                        label="Duration (minutes)"
                        type="number"
                        min={1}
                        value={metaForm.estimatedDurationMinutes}
                        onChange={(e) => setMetaForm({ ...metaForm, estimatedDurationMinutes: parseInt(e.target.value) || 60 })}
                      />
                    </div>
                    <Input
                      label="Source Provenance"
                      value={metaForm.sourceProvenance}
                      onChange={(e) => setMetaForm({ ...metaForm, sourceProvenance: e.target.value })}
                      placeholder="e.g. Original OET material, BMJ 2024..."
                    />
                    <div className="flex gap-2 pt-1">
                      <Button variant="primary" size="sm" onClick={handleSaveMeta} disabled={savingMeta}>
                        {savingMeta ? 'Saving…' : 'Save Metadata'}
                      </Button>
                      <Button variant="ghost" size="sm" onClick={() => setEditingMeta(false)}>
                        Cancel
                      </Button>
                    </div>
                  </div>
                )}
              </SettingsSection>

              <SettingsSection
                title="Next steps"
                description="Jump to a step in the reading authoring wizard."
              >
                <div className="flex flex-wrap gap-3">
                  <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
                    <Link href={`/admin/content/reading/${paperId}/texts`}>Manage PDFs</Link>
                  </Button>
                  <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
                    <Link href={`/admin/content/reading/${paperId}/questions`}>Edit Questions</Link>
                  </Button>
                  <Button asChild variant="secondary" size="sm" startIcon={<Eye className="h-4 w-4" />}>
                    <Link href={`/admin/content/reading/${paperId}/preview`}>Preview as student</Link>
                  </Button>
                  <Button asChild variant="secondary" size="sm" startIcon={<BookOpen className="h-4 w-4" />}>
                    <Link href={`/admin/content/reading/${paperId}/structure`}>Structure review</Link>
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={handleClonePaper}
                    disabled={cloning}
                    startIcon={cloning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Copy className="h-4 w-4" />}
                  >
                    Clone draft revision
                  </Button>
                  <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
                    <Link href={`/admin/content/reading/${paperId}/validate`}>Validate &amp; Publish</Link>
                  </Button>
                </div>
              </SettingsSection>

              <ReadingManifestPanel
                paperId={paperId}
                paperTitle={paper?.title ?? ''}
                onImported={() => { void reloadStructure(); }}
                onNotify={(variant, message) => setToast({ message, variant })}
              />
            </>
          ) : null}
        </div>
      </AdminSettingsLayout>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </>
  );
}
