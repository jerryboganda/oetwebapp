'use client';

/**
 * Video wizard — step 1: details.
 * Title (required), description, categories (with inline create), tags CSV,
 * difficulty and subtest. Mirrors the Speaking card wizard's classification
 * step (plain useState form, inline taxonomy create).
 */

import { useCallback, useEffect, useState } from 'react';
import { Plus, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import {
  adminCreateVideoCategory,
  adminListVideoCategories,
  adminPatchVideo,
  type AdminVideoCategory,
  type AdminVideoDetail,
  type VideoDifficulty,
} from '@/lib/api/video-library';
import {
  unseedVideoValue,
  VIDEO_DIFFICULTY_OPTIONS,
  VIDEO_SUBTEST_OPTIONS,
} from './video-wizard-config';

export function StepDetails() {
  const wizard = useAdminWizard<AdminVideoDetail>();
  const video = wizard.entity;

  const [title, setTitle] = useState(unseedVideoValue(video.title));
  const [description, setDescription] = useState(video.description ?? '');
  const [tagsCsv, setTagsCsv] = useState(video.tagsCsv ?? '');
  const [difficulty, setDifficulty] = useState<string>(video.difficulty ?? 'core');
  const [subtestCode, setSubtestCode] = useState(video.subtestCode ?? 'general');
  const [categoryIds, setCategoryIds] = useState<string[]>(video.categoryIds ?? []);

  const [categories, setCategories] = useState<AdminVideoCategory[]>([]);
  const [showNewCategory, setShowNewCategory] = useState(false);
  const [newCategoryTitle, setNewCategoryTitle] = useState('');
  const [newCategoryDescription, setNewCategoryDescription] = useState('');
  const [creatingCategory, setCreatingCategory] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    adminListVideoCategories(true)
      .then(setCategories)
      .catch(() => setCategories([]));
  }, []);

  const canAdvance = title.trim().length > 0;

  const submit = useCallback(async () => {
    if (!title.trim()) {
      setError('Title is required.');
      throw new Error('invalid');
    }
    setError(null);
    await adminPatchVideo(video.videoId, {
      title: title.trim(),
      description: description.trim(),
      tagsCsv: tagsCsv.trim(),
      difficulty: difficulty as VideoDifficulty,
      categoryIds,
      subtestCode: subtestCode || null,
    });
    await wizard.refresh();
  }, [video.videoId, title, description, tagsCsv, difficulty, categoryIds, subtestCode, wizard]);

  useStepRegistration('details', { canAdvance, submit });

  function toggleCategory(id: string) {
    setCategoryIds((prev) => (prev.includes(id) ? prev.filter((c) => c !== id) : [...prev, id]));
  }

  async function handleCreateCategory() {
    if (!newCategoryTitle.trim()) {
      setError('Category name is required.');
      return;
    }
    setCreatingCategory(true);
    setError(null);
    try {
      const created = await adminCreateVideoCategory({
        title: newCategoryTitle.trim(),
        description: newCategoryDescription.trim() || undefined,
      });
      setCategories((prev) => [...prev, created]);
      setCategoryIds((prev) => [...prev, created.id]);
      setNewCategoryTitle('');
      setNewCategoryDescription('');
      setShowNewCategory(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the category.');
    } finally {
      setCreatingCategory(false);
    }
  }

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Details</h2>
        <p className="text-sm text-muted">What this video is about and how it is filed in the library.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-4 md:grid-cols-2">
        <Input
          label="Title"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder='e.g. "Reading Part B — skimming strategies"'
          maxLength={200}
          required
        />
        <Input
          label="Tags (comma-separated)"
          value={tagsCsv}
          onChange={(e) => setTagsCsv(e.target.value)}
          placeholder="e.g. skimming, part-b, time management"
          maxLength={500}
        />
      </div>

      <Textarea
        label="Description"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        rows={4}
        maxLength={4000}
        placeholder="What learners will get out of this video (shown on the watch page)."
      />

      <div className="grid gap-4 md:grid-cols-2">
        <Select
          label="Difficulty"
          value={difficulty}
          onChange={(e) => setDifficulty(e.target.value)}
          options={VIDEO_DIFFICULTY_OPTIONS}
          required
        />
        <Select
          label="Subtest"
          value={subtestCode}
          onChange={(e) => setSubtestCode(e.target.value)}
          options={VIDEO_SUBTEST_OPTIONS}
          required
        />
      </div>

      <div className="space-y-2">
        <p className="text-sm font-semibold tracking-tight text-navy">Categories</p>
        {categories.length === 0 ? (
          <p className="text-xs text-muted">No categories yet — create the first one below.</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {categories.map((category) => {
              const selected = categoryIds.includes(category.id);
              return (
                <Button
                  key={category.id}
                  type="button"
                  size="sm"
                  variant={selected ? 'primary' : 'outline'}
                  onClick={() => toggleCategory(category.id)}
                  aria-pressed={selected}
                >
                  {category.title}
                  {category.status !== 'active' ? ' (inactive)' : ''}
                </Button>
              );
            })}
          </div>
        )}

        {showNewCategory ? (
          <div className="space-y-2 rounded-2xl border border-border bg-background-light p-3">
            <Input
              label="New category name"
              value={newCategoryTitle}
              onChange={(e) => setNewCategoryTitle(e.target.value)}
              placeholder='e.g. "Exam strategy"'
              maxLength={120}
            />
            <Textarea
              label="Description (optional)"
              value={newCategoryDescription}
              onChange={(e) => setNewCategoryDescription(e.target.value)}
              rows={2}
              maxLength={2000}
            />
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="primary"
                size="sm"
                onClick={() => void handleCreateCategory()}
                disabled={creatingCategory}
              >
                <Plus className="mr-1 h-3.5 w-3.5" /> Add category
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setShowNewCategory(false)}
                disabled={creatingCategory}
              >
                <X className="mr-1 h-3.5 w-3.5" /> Cancel
              </Button>
            </div>
          </div>
        ) : (
          <Button type="button" variant="outline" size="sm" onClick={() => setShowNewCategory(true)}>
            <Plus className="mr-1 h-3.5 w-3.5" /> New category
          </Button>
        )}
      </div>
    </div>
  );
}
