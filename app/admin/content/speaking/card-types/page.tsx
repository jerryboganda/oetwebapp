'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Admin CRUD for the fully-configurable hidden speaking card types
 * (e.g. "Card 4 – Examination Card"). The owner adds the ~6 types here. Card
 * types are NEVER shown to students — they aid human + AI marking only.
 */
import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Plus, Save, Trash2, Loader2, Pencil, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Textarea, Checkbox } from '@/components/ui/form-controls';
import {
  adminListSpeakingCardTypes,
  adminCreateSpeakingCardType,
  adminUpdateSpeakingCardType,
  adminDeleteSpeakingCardType,
  type SpeakingCardTypeDetail,
} from '@/lib/api/speaking-role-play-cards';

interface EditorState {
  id: string | null;
  name: string;
  description: string;
  sortOrder: string;
  isActive: boolean;
}

const EMPTY: EditorState = { id: null, name: '', description: '', sortOrder: '0', isActive: true };

export default function SpeakingCardTypesPage() {
  const [types, setTypes] = useState<SpeakingCardTypeDetail[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    try {
      setTypes(await adminListSpeakingCardTypes(true));
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load card types.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const handleSave = useCallback(
    async (e: FormEvent) => {
      e.preventDefault();
      if (!editor || !editor.name.trim()) return;
      setSaving(true);
      setError(null);
      try {
        const input = {
          name: editor.name.trim(),
          description: editor.description.trim(),
          sortOrder: Number(editor.sortOrder) || 0,
          isActive: editor.isActive,
        };
        if (editor.id) {
          await adminUpdateSpeakingCardType(editor.id, input);
        } else {
          await adminCreateSpeakingCardType(input);
        }
        setEditor(null);
        await load();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not save the card type.');
      } finally {
        setSaving(false);
      }
    },
    [editor, load],
  );

  const handleDelete = useCallback(
    async (id: string, name: string) => {
      if (!window.confirm(`Delete card type "${name}"? If cards use it, it will be deactivated instead.`)) {
        return;
      }
      try {
        await adminDeleteSpeakingCardType(id);
        await load();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not delete the card type.');
      }
    },
    [load],
  );

  return (
    <div className="mx-auto max-w-3xl px-4 py-8">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Speaking card types</h1>
          <p className="text-sm text-muted">
            Hidden taxonomy that aids human &amp; AI marking. Never shown to students.
          </p>
        </div>
        {!editor && (
          <Button onClick={() => setEditor({ ...EMPTY })}>
            <Plus className="mr-1.5 h-4 w-4" /> New type
          </Button>
        )}
      </header>

      {error ? (
        <p className="mb-4 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
          {error}
        </p>
      ) : null}

      {editor ? (
        <form onSubmit={(e) => void handleSave(e)} className="mb-6 space-y-4 rounded-xl border border-border bg-surface p-5">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-bold uppercase tracking-wide text-muted">
              {editor.id ? 'Edit card type' : 'New card type'}
            </h2>
            <button type="button" onClick={() => setEditor(null)} aria-label="Cancel" className="text-muted hover:text-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
          <Input
            label="Name"
            value={editor.name}
            onChange={(e) => setEditor({ ...editor, name: e.target.value })}
            placeholder='e.g. "Examination Card"'
            maxLength={120}
            required
          />
          <Textarea
            label="Description (marking guidance for human + AI)"
            value={editor.description}
            onChange={(e) => setEditor({ ...editor, description: e.target.value })}
            rows={3}
            maxLength={2000}
            placeholder="What this card type tests and how it should be marked."
          />
          <div className="grid gap-4 md:grid-cols-2">
            <Input
              label="Sort order"
              type="number"
              value={editor.sortOrder}
              onChange={(e) => setEditor({ ...editor, sortOrder: e.target.value })}
            />
            <div className="flex items-end">
              <Checkbox
                label="Active"
                checked={editor.isActive}
                onChange={(e) => setEditor({ ...editor, isActive: e.target.checked })}
              />
            </div>
          </div>
          <div className="flex gap-2 border-t border-border pt-4">
            <Button type="submit" disabled={saving || !editor.name.trim()}>
              {saving ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : <Save className="mr-1.5 h-4 w-4" />}
              Save
            </Button>
            <Button type="button" variant="outline" onClick={() => setEditor(null)}>
              Cancel
            </Button>
          </div>
        </form>
      ) : null}

      {loading ? (
        <div className="flex items-center justify-center py-12">
          <Loader2 className="h-5 w-5 animate-spin text-muted" />
        </div>
      ) : types.length === 0 ? (
        <p className="rounded-xl border border-dashed border-border bg-surface px-4 py-10 text-center text-sm text-muted">
          No card types yet. Add the first one (e.g. &quot;Examination Card&quot;).
        </p>
      ) : (
        <ul className="space-y-2">
          {types.map((t) => (
            <li
              key={t.id}
              className="flex items-start justify-between gap-3 rounded-xl border border-border bg-surface px-4 py-3"
            >
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-foreground">{t.name}</span>
                  {!t.isActive && (
                    <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold uppercase text-slate-500">
                      Inactive
                    </span>
                  )}
                  <span className="text-xs text-muted">· {t.cardCount} card{t.cardCount === 1 ? '' : 's'}</span>
                </div>
                {t.description ? <p className="mt-1 text-sm text-muted">{t.description}</p> : null}
              </div>
              <div className="flex flex-shrink-0 gap-1">
                <button
                  type="button"
                  aria-label={`Edit ${t.name}`}
                  onClick={() =>
                    setEditor({
                      id: t.id,
                      name: t.name,
                      description: t.description,
                      sortOrder: String(t.sortOrder),
                      isActive: t.isActive,
                    })
                  }
                  className="rounded-md p-2 text-muted hover:bg-background hover:text-foreground"
                >
                  <Pencil className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  aria-label={`Delete ${t.name}`}
                  onClick={() => void handleDelete(t.id, t.name)}
                  className="rounded-md p-2 text-muted hover:bg-rose-50 hover:text-rose-600"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
