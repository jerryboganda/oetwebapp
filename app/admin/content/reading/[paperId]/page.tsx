'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, ArrowRight, BookOpen, FileText, HelpCircle, ShieldCheck, Pencil } from 'lucide-react';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import {
  getReadingStructureAdmin,
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
  const paperId = params?.paperId ?? '';

  const [structure, setStructure] = useState<ReadingStructureAdminDto | null>(null);
  const [validation, setValidation] = useState<ReadingValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [editingMeta, setEditingMeta] = useState(false);
  const [metaForm, setMetaForm] = useState({ title: '', difficulty: '', estimatedDurationMinutes: 60, sourceProvenance: '' });
  const [savingMeta, setSavingMeta] = useState(false);
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

  const partASummary = structure?.parts.find((p) => p.partCode === 'A');
  const partBSummary = structure?.parts.find((p) => p.partCode === 'B');
  const partCSummary = structure?.parts.find((p) => p.partCode === 'C');

  const totalQuestions =
    (partASummary?.questions.length ?? 0) +
    (partBSummary?.questions.length ?? 0) +
    (partCSummary?.questions.length ?? 0);

  const errorCount = validation?.issues.filter((i) => i.severity === 'error').length ?? 0;
  const warningCount = validation?.issues.filter((i) => i.severity === 'warning').length ?? 0;

  return (
    <AdminRouteWorkspace className="p-6">
      <div className="mb-4">
        <Link
          href="/admin/content/reading"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          <ArrowLeft className="h-4 w-4" />
          Back to Reading
        </Link>
      </div>

      <ReadingWizardSteps paperId={paperId} currentStep="metadata" />

      <AdminRoutePanel className="mt-6">
        <AdminRouteSectionHeader
          title="Paper Overview"
          description="Summary of the reading paper structure and validation status."
        />

        {loading && (
          <div className="py-8 text-center text-muted-foreground">Loading paper structure…</div>
        )}

        {error && (
          <InlineAlert variant="error" className="mt-4">
            {error}
          </InlineAlert>
        )}

        {!loading && structure && (
          <div className="mt-6 space-y-6">
            {/* Part Counts */}
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <div className="rounded-lg border border-border bg-muted p-4">
                <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                  <FileText className="h-4 w-4" />
                  Part A
                </div>
                <p className="mt-1 text-lg font-semibold text-foreground">
                  {partASummary?.texts.length ?? 0} texts, {partASummary?.questions.length ?? 0} questions
                </p>
                <p className="text-xs text-muted-foreground">Target: 20 questions</p>
              </div>

              <div className="rounded-lg border border-border bg-muted p-4">
                <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                  <BookOpen className="h-4 w-4" />
                  Part B
                </div>
                <p className="mt-1 text-lg font-semibold text-foreground">
                  {partBSummary?.texts.length ?? 0} texts, {partBSummary?.questions.length ?? 0} questions
                </p>
                <p className="text-xs text-muted-foreground">Target: 6 questions</p>
              </div>

              <div className="rounded-lg border border-border bg-muted p-4">
                <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                  <HelpCircle className="h-4 w-4" />
                  Part C
                </div>
                <p className="mt-1 text-lg font-semibold text-foreground">
                  {partCSummary?.texts.length ?? 0} texts, {partCSummary?.questions.length ?? 0} questions
                </p>
                <p className="text-xs text-muted-foreground">Target: 16 questions</p>
              </div>

              <div className="rounded-lg border border-border bg-muted p-4">
                <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                  <ShieldCheck className="h-4 w-4" />
                  Total
                </div>
                <p className="mt-1 text-lg font-semibold text-foreground">
                  {totalQuestions} / 42 questions
                </p>
                <p className="text-xs text-muted-foreground">
                  {structure.parts.length} parts loaded
                </p>
              </div>
            </div>

            {/* Validation Status */}
            {validation && (
              <div className="rounded-lg border border-border bg-surface p-4">
                <h3 className="text-sm font-medium text-foreground mb-2">Validation Status</h3>
                <div className="flex flex-wrap items-center gap-2">
                  {validation.isPublishReady ? (
                    <Badge variant="success">Publish Ready</Badge>
                  ) : (
                    <Badge variant="danger">Not Ready</Badge>
                  )}
                  {errorCount > 0 && (
                    <Badge variant="danger">{errorCount} error{errorCount !== 1 ? 's' : ''}</Badge>
                  )}
                  {warningCount > 0 && (
                    <Badge variant="warning">{warningCount} warning{warningCount !== 1 ? 's' : ''}</Badge>
                  )}
                  {errorCount === 0 && warningCount === 0 && validation.isPublishReady && (
                    <span className="text-sm text-muted-foreground">All checks passed</span>
                  )}
                </div>
              </div>
            )}

            {/* Metadata Edit */}
            <div className="rounded-lg border border-border bg-surface p-4">
              <div className="flex items-center justify-between mb-3">
                <h3 className="text-sm font-medium text-foreground">Paper Metadata</h3>
                {!editingMeta && (
                  <Button variant="ghost" size="sm" onClick={() => setEditingMeta(true)}>
                    <Pencil className="h-3.5 w-3.5 mr-1" />
                    Edit
                  </Button>
                )}
              </div>
              {!editingMeta ? (
                <div className="grid grid-cols-2 gap-2 text-sm">
                  <div><span className="text-muted-foreground">Title:</span> {paper?.title ?? '—'}</div>
                  <div><span className="text-muted-foreground">Difficulty:</span> {paper?.difficulty ?? '—'}</div>
                  <div><span className="text-muted-foreground">Duration:</span> {paper?.estimatedDurationMinutes ?? 60} min</div>
                  <div><span className="text-muted-foreground">Source:</span> {paper?.sourceProvenance ?? '—'}</div>
                </div>
              ) : (
                <div className="space-y-3">
                  <Input
                    label="Title"
                    value={metaForm.title}
                    onChange={(e) => setMetaForm({ ...metaForm, title: e.target.value })}
                  />
                  <div className="grid grid-cols-2 gap-3">
                    <Select
                      label="Difficulty"
                      value={metaForm.difficulty}
                      onChange={(e) => setMetaForm({ ...metaForm, difficulty: e.target.value })}
                      options={[
                        { value: '', label: '— Select —' },
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
            </div>

            {/* Action Buttons */}
            <div className="flex flex-wrap gap-3">
              <Button variant="primary" asChild>
                <Link href={`/admin/content/reading/${paperId}/texts`}>
                  Edit Texts
                  <ArrowRight className="ml-1 h-4 w-4" />
                </Link>
              </Button>
              <Button variant="primary" asChild>
                <Link href={`/admin/content/reading/${paperId}/questions`}>
                  Edit Questions
                  <ArrowRight className="ml-1 h-4 w-4" />
                </Link>
              </Button>
              <Button variant="primary" asChild>
                <Link href={`/admin/content/reading/${paperId}/validate`}>
                  Validate &amp; Publish
                  <ArrowRight className="ml-1 h-4 w-4" />
                </Link>
              </Button>
            </div>
          </div>
        )}
      </AdminRoutePanel>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
