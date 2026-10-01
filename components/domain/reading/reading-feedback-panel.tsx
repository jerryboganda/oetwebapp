'use client';

import { useCallback, useEffect, useState } from 'react';
import { Loader2, MessageSquarePlus, Pencil, Trash2, X } from 'lucide-react';
import { cn } from '@/lib/utils';
import {
  createReadingAttemptFeedback,
  deleteReadingAttemptFeedback,
  listReadingAttemptFeedback,
  updateReadingAttemptFeedback,
  type ReadingFeedbackDto,
  type ReadingFeedbackInput,
  type ReadingTutorArea,
} from '@/lib/reading-tutor-api';

/**
 * ReadingFeedbackPanel — tutor feedback CRUD shared by the admin and expert
 * surfaces. Feedback can be scoped to the whole test, a section, a single
 * question, or a skill, with an optional target reference (e.g. the part code,
 * question id, or skill tag).
 */

const SCOPES = ['test', 'section', 'question', 'skill'] as const;
type FeedbackScope = (typeof SCOPES)[number];

const SCOPE_LABELS: Record<FeedbackScope, string> = {
  test: 'Whole test',
  section: 'Section',
  question: 'Question',
  skill: 'Skill',
};

const SCOPE_REF_HINT: Record<FeedbackScope, string> = {
  test: '',
  section: 'Part code (A, B, C)',
  question: 'Question id',
  skill: 'Skill tag',
};

interface FormState {
  scope: FeedbackScope;
  targetRef: string;
  feedbackText: string;
}

const EMPTY_FORM: FormState = { scope: 'test', targetRef: '', feedbackText: '' };

export interface ReadingFeedbackPanelProps {
  attemptId: string;
  area: ReadingTutorArea;
  className?: string;
}

export function ReadingFeedbackPanel({ attemptId, area, className }: ReadingFeedbackPanelProps) {
  const [items, setItems] = useState<ReadingFeedbackDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listReadingAttemptFeedback(attemptId, area);
      setItems(data);
    } catch {
      setError('Failed to load feedback.');
    } finally {
      setLoading(false);
    }
  }, [attemptId, area]);

  useEffect(() => {
    void load();
  }, [load]);

  function resetForm() {
    setForm(EMPTY_FORM);
    setEditingId(null);
  }

  function startEdit(item: ReadingFeedbackDto) {
    const scope = (SCOPES as readonly string[]).includes(item.scope)
      ? (item.scope as FeedbackScope)
      : 'test';
    setForm({ scope, targetRef: item.targetRef ?? '', feedbackText: item.feedbackText });
    setEditingId(item.id);
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    const text = form.feedbackText.trim();
    if (!text) {
      setError('Feedback text is required.');
      return;
    }
    const body: ReadingFeedbackInput = {
      scope: form.scope,
      targetRef: form.scope === 'test' ? null : form.targetRef.trim() || null,
      feedbackText: text,
    };

    setSaving(true);
    try {
      if (editingId) {
        const updated = await updateReadingAttemptFeedback(attemptId, editingId, body, area);
        setItems((prev) => prev.map((item) => (item.id === editingId ? updated : item)));
      } else {
        const created = await createReadingAttemptFeedback(attemptId, body, area);
        setItems((prev) => [created, ...prev]);
      }
      resetForm();
    } catch {
      setError(editingId ? 'Failed to update feedback.' : 'Failed to add feedback.');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: string) {
    setError(null);
    setDeletingId(id);
    try {
      await deleteReadingAttemptFeedback(attemptId, id, area);
      setItems((prev) => prev.filter((item) => item.id !== id));
      if (editingId === id) resetForm();
    } catch {
      setError('Failed to delete feedback.');
    } finally {
      setDeletingId(null);
    }
  }

  return (
    <section
      aria-label="Tutor feedback"
      className={cn(
        'space-y-4 rounded-xl border border-border bg-white p-4 dark:bg-slate-900',
        className,
      )}
    >
      <div className="flex items-center gap-2">
        <MessageSquarePlus className="h-4 w-4 text-muted" aria-hidden="true" />
        <h3 className="text-sm font-semibold text-navy">Tutor feedback</h3>
      </div>

      <form onSubmit={handleSubmit} className="space-y-3">
        <div className="flex flex-wrap gap-3">
          <div className="flex flex-col gap-1.5">
            <label htmlFor={`feedback-scope-${attemptId}`} className="text-sm font-medium text-navy">
              Scope
            </label>
            <select
              id={`feedback-scope-${attemptId}`}
              value={form.scope}
              disabled={saving}
              onChange={(event) =>
                setForm((prev) => ({ ...prev, scope: event.target.value as FeedbackScope }))
              }
              className="rounded-lg border border-border-hover bg-white px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-slate-400 dark:bg-slate-800"
            >
              {SCOPES.map((scope) => (
                <option key={scope} value={scope}>
                  {SCOPE_LABELS[scope]}
                </option>
              ))}
            </select>
          </div>

          {form.scope !== 'test' ? (
            <div className="flex flex-1 flex-col gap-1.5">
              <label htmlFor={`feedback-ref-${attemptId}`} className="text-sm font-medium text-navy">
                Target reference
              </label>
              <input
                id={`feedback-ref-${attemptId}`}
                type="text"
                value={form.targetRef}
                disabled={saving}
                placeholder={SCOPE_REF_HINT[form.scope]}
                onChange={(event) => setForm((prev) => ({ ...prev, targetRef: event.target.value }))}
                className="rounded-lg border border-border-hover bg-white px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-slate-400 dark:bg-slate-800"
              />
            </div>
          ) : null}
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor={`feedback-text-${attemptId}`} className="text-sm font-medium text-navy">
            Feedback
          </label>
          <textarea
            id={`feedback-text-${attemptId}`}
            rows={3}
            value={form.feedbackText}
            disabled={saving}
            onChange={(event) => setForm((prev) => ({ ...prev, feedbackText: event.target.value }))}
            className="rounded-lg border border-border-hover bg-white px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-slate-400 dark:bg-slate-800"
          />
        </div>

        {error ? (
          <p role="alert" className="text-sm text-danger-strong">
            {error}
          </p>
        ) : null}

        <div className="flex items-center gap-3">
          <button
            type="submit"
            disabled={saving}
            className="inline-flex items-center gap-2 rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-slate-800 disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900 dark:hover:bg-white"
          >
            {saving ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {editingId ? 'Update feedback' : 'Add feedback'}
          </button>
          {editingId ? (
            <button
              type="button"
              onClick={resetForm}
              disabled={saving}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border-hover px-4 py-2 text-sm font-medium text-navy transition-colors hover:bg-background-light disabled:opacity-50"
            >
              <X className="h-4 w-4" aria-hidden="true" /> Cancel
            </button>
          ) : null}
        </div>
      </form>

      <div className="space-y-2">
        {loading ? (
          <p className="text-sm text-muted">Loading feedback…</p>
        ) : items.length === 0 ? (
          <p className="rounded-lg border border-dashed border-border-hover px-4 py-6 text-center text-sm text-muted">
            No feedback yet.
          </p>
        ) : (
          <ul className="space-y-2">
            {items.map((item) => (
              <li
                key={item.id}
                className="rounded-lg border border-border bg-background-light px-3 py-2.5"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="eyebrow text-muted">
                      {SCOPE_LABELS[(SCOPES as readonly string[]).includes(item.scope) ? (item.scope as FeedbackScope) : 'test']}
                      {item.targetRef ? ` · ${item.targetRef}` : ''}
                    </p>
                    <p className="mt-0.5 whitespace-pre-wrap text-sm text-navy">
                      {item.feedbackText}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-1">
                    <button
                      type="button"
                      onClick={() => startEdit(item)}
                      aria-label="Edit feedback"
                      className="rounded-md p-1.5 text-muted transition-colors hover:bg-border hover:text-navy dark:text-slate-400"
                    >
                      <Pencil className="h-4 w-4" aria-hidden="true" />
                    </button>
                    <button
                      type="button"
                      onClick={() => handleDelete(item.id)}
                      disabled={deletingId === item.id}
                      aria-label="Delete feedback"
                      className="rounded-md p-1.5 text-muted transition-colors hover:bg-danger/10 hover:text-danger-strong disabled:opacity-50 dark:text-muted"
                    >
                      {deletingId === item.id ? (
                        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                      ) : (
                        <Trash2 className="h-4 w-4" aria-hidden="true" />
                      )}
                    </button>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
