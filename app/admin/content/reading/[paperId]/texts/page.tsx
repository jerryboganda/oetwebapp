'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, Plus, Trash2, ArrowUp, ArrowDown, Save } from 'lucide-react';

import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle, CardDescription, CardAction } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import { ReadingPartTabs } from '@/components/domain/admin/reading/ReadingPartTabs';
import {
  getReadingStructureAdmin,
  upsertReadingText,
  removeReadingText,
  reorderReadingTexts,
  type ReadingPartCode,
  type ReadingTextDto,
  type ReadingPartAdminDto,
} from '@/lib/reading-authoring-api';

interface TextFormData {
  id: string | null;
  readingPartId: string;
  displayOrder: number;
  title: string;
  source: string;
  bodyHtml: string;
  wordCount: number;
  topicTag: string;
}

function createEmptyForm(readingPartId: string, displayOrder: number): TextFormData {
  return {
    id: null,
    readingPartId,
    displayOrder,
    title: '',
    source: '',
    bodyHtml: '',
    wordCount: 0,
    topicTag: '',
  };
}

function formFromDto(dto: ReadingTextDto): TextFormData {
  return {
    id: dto.id,
    readingPartId: dto.readingPartId,
    displayOrder: dto.displayOrder,
    title: dto.title,
    source: dto.source ?? '',
    bodyHtml: dto.bodyHtml,
    wordCount: dto.wordCount,
    topicTag: dto.topicTag ?? '',
  };
}

function countWordsFromHtml(html: string): number {
  const text = html.replace(/<[^>]*>/g, ' ').replace(/&nbsp;/g, ' ');
  const words = text.trim().split(/\s+/).filter(Boolean);
  return words.length;
}

export default function ReadingTextsEditorPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [parts, setParts] = useState<ReadingPartAdminDto[]>([]);
  const [activeTab, setActiveTab] = useState<ReadingPartCode>('A');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [editingForm, setEditingForm] = useState<TextFormData | null>(null);
  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null);

  const fetchStructure = useCallback(async () => {
    if (!paperId) return;
    try {
      const data = await getReadingStructureAdmin(paperId);
      setParts(data.parts);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to load structure';
      setToast({ message, variant: 'error' });
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    fetchStructure();
  }, [fetchStructure]);

  const activePart = parts.find((p) => p.partCode === activeTab) ?? null;
  const activeTexts = activePart?.texts ?? [];

  const handleAddText = () => {
    if (!activePart) return;
    const nextOrder = activeTexts.length > 0
      ? Math.max(...activeTexts.map((t) => t.displayOrder)) + 1
      : 1;
    setEditingForm(createEmptyForm(activePart.id, nextOrder));
  };

  const handleEditText = (dto: ReadingTextDto) => {
    setEditingForm(formFromDto(dto));
  };

  const handleCancelEdit = () => {
    setEditingForm(null);
  };

  const handleDeleteText = async (textId: string) => {
    if (!window.confirm('Are you sure you want to delete this text passage? This cannot be undone.')) {
      return;
    }
    try {
      await removeReadingText(paperId, textId);
      setToast({ message: 'Text deleted successfully', variant: 'success' });
      await fetchStructure();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to delete text';
      setToast({ message, variant: 'error' });
    }
  };

  const handleSaveText = async () => {
    if (!editingForm) return;
    if (!editingForm.title.trim()) {
      setToast({ message: 'Title is required', variant: 'error' });
      return;
    }
    if (!editingForm.bodyHtml.trim()) {
      setToast({ message: 'Body text is required', variant: 'error' });
      return;
    }

    setSaving(true);
    try {
      await upsertReadingText(paperId, {
        id: editingForm.id,
        readingPartId: editingForm.readingPartId,
        displayOrder: editingForm.displayOrder,
        title: editingForm.title.trim(),
        source: editingForm.source.trim() || null,
        bodyHtml: editingForm.bodyHtml,
        wordCount: editingForm.wordCount,
        topicTag: editingForm.topicTag.trim() || null,
      });
      setToast({ message: editingForm.id ? 'Text updated successfully' : 'Text added successfully', variant: 'success' });
      setEditingForm(null);
      await fetchStructure();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to save text';
      setToast({ message, variant: 'error' });
    } finally {
      setSaving(false);
    }
  };

  const handleMoveUp = async (index: number) => {
    if (index <= 0 || !activePart) return;
    const reordered = [...activeTexts];
    [reordered[index - 1], reordered[index]] = [reordered[index], reordered[index - 1]];
    const orderedIds = reordered.map((t) => t.id);
    try {
      await reorderReadingTexts(paperId, activePart.id, orderedIds);
      await fetchStructure();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to reorder';
      setToast({ message, variant: 'error' });
    }
  };

  const handleMoveDown = async (index: number) => {
    if (index >= activeTexts.length - 1 || !activePart) return;
    const reordered = [...activeTexts];
    [reordered[index], reordered[index + 1]] = [reordered[index + 1], reordered[index]];
    const orderedIds = reordered.map((t) => t.id);
    try {
      await reorderReadingTexts(paperId, activePart.id, orderedIds);
      await fetchStructure();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to reorder';
      setToast({ message, variant: 'error' });
    }
  };

  const handleBodyHtmlChange = (value: string) => {
    if (!editingForm) return;
    const autoWordCount = countWordsFromHtml(value);
    setEditingForm({ ...editingForm, bodyHtml: value, wordCount: autoWordCount });
  };

  const textCounts = {
    A: parts.find((p) => p.partCode === 'A')?.texts.length ?? 0,
    B: parts.find((p) => p.partCode === 'B')?.texts.length ?? 0,
    C: parts.find((p) => p.partCode === 'C')?.texts.length ?? 0,
  };

  if (!paperId) {
    return (
      <AdminSettingsLayout
        title="Reading Texts Editor"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Texts' },
        ]}
      >
        <SettingsSection title="Missing paper">
          <p className="text-sm text-admin-fg-muted">No paper ID provided.</p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Reading Texts Editor"
      description="Add and manage reading passages for each part of the paper."
      eyebrow="Reading authoring"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: 'Paper', href: `/admin/content/reading/${paperId}` },
        { label: 'Texts' },
      ]}
    >
      <ReadingWizardSteps paperId={paperId} currentStep="texts" />

      <ReadingPartTabs activeTab={activeTab} onTabChange={setActiveTab} counts={textCounts} context="texts" />

      {loading ? (
        <Card>
          <CardContent className="space-y-3 py-6">
            <Skeleton variant="text" className="h-5 w-1/3" />
            <Skeleton variant="card" />
            <Skeleton variant="card" />
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardHeader>
            <div className="min-w-0">
              <CardTitle>{`Part ${activeTab} Texts`}</CardTitle>
              <CardDescription>
                {`${activeTexts.length} passage${activeTexts.length !== 1 ? 's' : ''} added`}
              </CardDescription>
            </div>
            <CardAction>
              <Button
                variant="primary"
                size="sm"
                onClick={handleAddText}
                disabled={!!editingForm}
                startIcon={<Plus className="h-4 w-4" />}
              >
                Add Text
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>
          {activeTexts.length === 0 && !editingForm && (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <p className="text-sm text-admin-fg-muted mb-3">
                No texts have been added to Part {activeTab} yet.
              </p>
              <Button variant="primary" size="sm" onClick={handleAddText}>
                <Plus className="h-4 w-4 mr-1" />
                Add First Text
              </Button>
            </div>
          )}

          {activeTexts.length > 0 && (
            <div className="space-y-2">
              {activeTexts.map((text, idx) => (
                <div
                  key={text.id}
                  className="flex items-center gap-3 rounded-xl border border-admin-border bg-admin-bg-subtle px-4 py-3 transition-colors hover:bg-[var(--admin-state-hover)]"
                >
                  <div className="flex flex-col items-center gap-0.5 text-admin-fg-muted">
                    <button
                      type="button"
                      onClick={() => handleMoveUp(idx)}
                      disabled={idx === 0}
                      className="p-0.5 hover:text-primary disabled:opacity-30 disabled:cursor-not-allowed"
                      aria-label="Move up"
                    >
                      <ArrowUp className="h-3.5 w-3.5" />
                    </button>
                    <button
                      type="button"
                      onClick={() => handleMoveDown(idx)}
                      disabled={idx === activeTexts.length - 1}
                      className="p-0.5 hover:text-primary disabled:opacity-30 disabled:cursor-not-allowed"
                      aria-label="Move down"
                    >
                      <ArrowDown className="h-3.5 w-3.5" />
                    </button>
                  </div>

                  <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-xs font-bold text-primary">
                    {text.displayOrder}
                  </div>

                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-semibold text-admin-fg-strong truncate">{text.title}</p>
                    {text.source && (
                      <p className="text-xs text-admin-fg-muted truncate mt-0.5">{text.source}</p>
                    )}
                  </div>

                  <Badge variant="muted" className="shrink-0">
                    {text.wordCount} words
                  </Badge>

                  {text.topicTag && (
                    <Badge variant="outline" className="shrink-0 text-xs">
                      {text.topicTag}
                    </Badge>
                  )}

                  <div className="flex items-center gap-1 shrink-0">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleEditText(text)}
                      disabled={!!editingForm}
                    >
                      Edit
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDeleteText(text.id)}
                      className="text-rose-400 hover:text-rose-300 hover:bg-rose-500/10"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}

          {editingForm && (
            <div className="mt-4 rounded-xl border border-primary/30 bg-admin-bg-subtle p-4 space-y-4">
              <h3 className="text-sm font-bold text-admin-fg-strong">
                {editingForm.id ? 'Edit Text Passage' : 'Add New Text Passage'}
              </h3>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <label htmlFor="text-title" className="text-xs font-medium text-admin-fg-muted">
                    Title <span className="text-rose-400">*</span>
                  </label>
                  <Input
                    id="text-title"
                    value={editingForm.title}
                    onChange={(e) => setEditingForm({ ...editingForm, title: e.target.value })}
                    placeholder="e.g. Text 1: Patient Discharge"
                  />
                </div>

                <div className="space-y-1.5">
                  <label htmlFor="text-source" className="text-xs font-medium text-admin-fg-muted">
                    Source
                  </label>
                  <Input
                    id="text-source"
                    value={editingForm.source}
                    onChange={(e) => setEditingForm({ ...editingForm, source: e.target.value })}
                    placeholder="e.g. British Medical Journal, 2023"
                  />
                </div>
              </div>

              <div className="space-y-1.5">
                <label htmlFor="text-body" className="text-xs font-medium text-admin-fg-muted">
                  Body HTML <span className="text-rose-400">*</span>
                </label>
                <Textarea
                  id="text-body"
                  value={editingForm.bodyHtml}
                  onChange={(e) => handleBodyHtmlChange(e.target.value)}
                  placeholder="Paste the reading passage text/HTML here…"
                  rows={12}
                  className="font-mono text-xs"
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <label htmlFor="text-wordcount" className="text-xs font-medium text-admin-fg-muted">
                    Word Count
                  </label>
                  <Input
                    id="text-wordcount"
                    type="number"
                    min={0}
                    value={editingForm.wordCount}
                    onChange={(e) => setEditingForm({ ...editingForm, wordCount: parseInt(e.target.value, 10) || 0 })}
                  />
                  <p className="text-xs text-admin-fg-muted">Auto-calculated from body. Override if needed.</p>
                </div>

                <div className="space-y-1.5">
                  <label htmlFor="text-topic" className="text-xs font-medium text-admin-fg-muted">
                    Topic Tag
                  </label>
                  <Input
                    id="text-topic"
                    value={editingForm.topicTag}
                    onChange={(e) => setEditingForm({ ...editingForm, topicTag: e.target.value })}
                    placeholder="e.g. cardiology, paediatrics"
                  />
                </div>
              </div>

              <div className="flex items-center gap-2 pt-2">
                <Button variant="primary" size="sm" onClick={handleSaveText} disabled={saving}>
                  <Save className="h-4 w-4 mr-1" />
                  {saving ? 'Saving…' : 'Save Text'}
                </Button>
                <Button variant="ghost" size="sm" onClick={handleCancelEdit} disabled={saving}>
                  Cancel
                </Button>
              </div>
            </div>
          )}
          </CardContent>
        </Card>
      )}

      <div className="flex items-center justify-between pt-2">
        <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
          <Link href={`/admin/content/reading/${paperId}`}>Back to Overview</Link>
        </Button>

        <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
          <Link href={`/admin/content/reading/${paperId}/questions`}>Next: Questions</Link>
        </Button>
      </div>

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminSettingsLayout>
  );
}
