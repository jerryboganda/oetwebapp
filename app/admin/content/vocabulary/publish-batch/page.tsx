'use client';

/**
 * Vocabulary publish-batch (SUBAGENT_E).
 *
 * UI replacement for `scripts/admin/publish-vocab.mjs`. Lists draft
 * VocabularyTerm rows, surfaces per-item missing-field indicators, and
 * offers two bulk actions:
 *
 *   1. "AI fill missing fields" — for each selected row, calls
 *      POST /v1/admin/vocabulary/ai/draft with the term as seedPrompt,
 *      then POST /v1/admin/vocabulary/ai/draft/accept which creates a
 *      polished active VocabularyTerm row. The original draft is left
 *      in place so the admin can archive/delete it if they wish.
 *   2. "Publish selected" — PUTs `{ status: 'active' }` on each draft.
 *      Backend's EnforceVocabularyPublishGate validates required fields.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { Pagination } from '@/components/ui/pagination';
import { BulkRunner } from '@/components/admin/bulk-runner/BulkRunner';
import {
  adminBulkListDraftVocab,
  adminBulkUpdateVocabItem,
  requestAdminVocabularyAiDraft,
  acceptAdminVocabularyAiDrafts,
  type AdminBulkVocabRow,
} from '@/lib/api';

type AiDraftResponse = {
  rulebookVersion?: string;
  drafts?: Array<Record<string, unknown>>;
  warning?: string | null;
};

function missingFields(row: AdminBulkVocabRow): string[] {
  const out: string[] = [];
  if (!row.definition || row.definition.trim() === '') out.push('definition');
  if (!row.exampleSentence || row.exampleSentence.trim() === '') out.push('example');
  // We don't have IPA in the list projection — that's surfaced when the
  // backend rejects the publish with the gate error.
  return out;
}

export default function VocabularyPublishBatchPage() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [rows, setRows] = useState<AdminBulkVocabRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(50);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [mode, setMode] = useState<'publish' | 'ai-fill'>('publish');

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await adminBulkListDraftVocab({
        page,
        pageSize,
        search: search.trim() || undefined,
      });
      setRows(data.items ?? []);
      setTotal(data.total ?? 0);
      setSelected(new Set());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load draft vocabulary items.');
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, search]);

  useEffect(() => { void refresh(); }, [refresh]);

  const allSelected = rows.length > 0 && selected.size === rows.length;
  const toggleAll = () => setSelected(allSelected ? new Set() : new Set(rows.map(r => r.id)));
  const toggleOne = (id: string) =>
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });

  const itemsToRun = useMemo(() => rows.filter(r => selected.has(r.id)), [rows, selected]);

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Publish draft vocabulary"
        description="Activate sparse drafts in bulk. Optionally regenerate richer entries via the grounded AI draft endpoint."
      />

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}

      <AdminRoutePanel>
        <div className="flex flex-wrap items-center gap-3">
          <Input
            value={search}
            onChange={(e) => { setPage(1); setSearch(e.target.value); }}
            placeholder="Search term…"
            className="max-w-xs"
          />
          <Button type="button" size="sm" variant="ghost" onClick={() => void refresh()}>
            <RefreshCw className="mr-1 h-4 w-4" /> Refresh
          </Button>
          <div className="ml-auto text-sm text-slate-500">
            Page {page}/{totalPages} · {total} drafts total
          </div>
        </div>

        {error && <InlineAlert variant="error" className="mt-3">{error}</InlineAlert>}

        {loading ? (
          <div className="mt-4 space-y-2">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            className="mt-4"
            title="No draft vocabulary items."
            description="Either everything is already active, or your search returned no matches."
          />
        ) : (
          <>
            <div className="mt-4 overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-left text-xs uppercase text-slate-600 dark:bg-slate-900/50 dark:text-slate-400">
                  <tr>
                    <th className="w-10 px-3 py-2">
                      <input
                        type="checkbox"
                        aria-label="Select all"
                        checked={allSelected}
                        onChange={toggleAll}
                      />
                    </th>
                    <th className="px-3 py-2">Term</th>
                    <th className="px-3 py-2">Category</th>
                    <th className="px-3 py-2">Profession</th>
                    <th className="px-3 py-2">Missing</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map(r => {
                    const missing = missingFields(r);
                    return (
                      <tr key={r.id} className="border-t border-slate-100 dark:border-slate-800">
                        <td className="px-3 py-2">
                          <input
                            type="checkbox"
                            aria-label={`Select ${r.term}`}
                            checked={selected.has(r.id)}
                            onChange={() => toggleOne(r.id)}
                          />
                        </td>
                        <td className="px-3 py-2">
                          <div className="font-medium">{r.term}</div>
                          <div className="text-xs text-slate-500">{r.id}</div>
                        </td>
                        <td className="px-3 py-2">{r.category}</td>
                        <td className="px-3 py-2 text-xs">{r.professionId ?? '—'}</td>
                        <td className="px-3 py-2">
                          {missing.length === 0
                            ? <Badge variant="success">complete</Badge>
                            : (
                              <div className="flex flex-wrap gap-1">
                                {missing.map(m => (
                                  <Badge key={m} variant="warning">no {m}</Badge>
                                ))}
                              </div>
                            )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <div className="mt-3">
              <Pagination
                page={page}
                pageSize={pageSize}
                total={total}
                onPageChange={setPage}
                onPageSizeChange={() => { /* fixed page size on this page */ }}
                itemLabel="draft"
              />
            </div>
          </>
        )}
      </AdminRoutePanel>

      <AdminRoutePanel>
        <AdminRouteSectionHeader
          title="Bulk action"
          description={`Selected: ${selected.size}/${rows.length} on this page.`}
        />
        <div className="mb-4 flex flex-wrap gap-2">
          <Button
            type="button"
            size="sm"
            variant={mode === 'publish' ? 'primary' : 'outline'}
            onClick={() => setMode('publish')}
          >
            Publish selected
          </Button>
          <Button
            type="button"
            size="sm"
            variant={mode === 'ai-fill' ? 'primary' : 'outline'}
            onClick={() => setMode('ai-fill')}
          >
            AI fill missing fields
          </Button>
        </div>

        {mode === 'publish' && (
          <BulkRunner
            items={itemsToRun}
            getKey={(r) => r.id}
            startLabel="Publish"
            renderRow={(r) => (
              <div>
                <div className="font-medium">{r.term}</div>
                <div className="text-xs text-slate-500">{r.category} · {r.professionId ?? 'all'}</div>
              </div>
            )}
            run={async (r) => {
              try {
                await adminBulkUpdateVocabItem(r.id, { status: 'active' });
                return { ok: true, detail: 'activated' };
              } catch (e) {
                return { ok: false, error: e instanceof Error ? e.message : String(e) };
              }
            }}
            onFinished={(summary) => {
              setToast({
                variant: summary.failed > 0 ? 'error' : 'success',
                message: `Done — ${summary.ok} activated, ${summary.failed} failed.`,
              });
              void refresh();
            }}
          />
        )}

        {mode === 'ai-fill' && (
          <>
            <InlineAlert variant="info" className="mb-3">
              For each selected term, the grounded gateway generates one polished entry and
              accepts it as a new <code>active</code> VocabularyTerm. The original draft is left
              in place so you can review and archive it manually if needed.
            </InlineAlert>
            <BulkRunner
              items={itemsToRun}
              getKey={(r) => r.id}
              startLabel="Generate &amp; accept"
              renderRow={(r) => (
                <div>
                  <div className="font-medium">{r.term}</div>
                  <div className="text-xs text-slate-500">{r.category} · {r.professionId ?? 'all'}</div>
                </div>
              )}
              run={async (r) => {
                try {
                  const ai = await requestAdminVocabularyAiDraft({
                    count: 1,
                    examTypeCode: 'oet',
                    professionId: r.professionId,
                    category: r.category,
                    difficulty: r.difficulty ?? undefined,
                    seedPrompt: r.term,
                  }) as AiDraftResponse;

                  const drafts = ai.drafts ?? [];
                  if (drafts.length === 0) {
                    return { ok: false, error: ai.warning ?? 'AI returned no drafts.' };
                  }

                  const accepted = await acceptAdminVocabularyAiDrafts({
                    examTypeCode: 'oet',
                    professionId: r.professionId,
                    sourceProvenance: `Admin bulk AI-fill (UI) · ${new Date().toISOString()}`,
                    drafts,
                  }) as { createdIds?: string[]; count?: number };

                  return {
                    ok: true,
                    detail: `created ${accepted.count ?? accepted.createdIds?.length ?? drafts.length} new term(s)`,
                    warnings: ai.warning ? [ai.warning] : undefined,
                  };
                } catch (e) {
                  return { ok: false, error: e instanceof Error ? e.message : String(e) };
                }
              }}
              onFinished={(summary) => {
                setToast({
                  variant: summary.failed > 0 ? 'error' : 'success',
                  message: `AI fill — ${summary.ok} ok, ${summary.failed} failed.`,
                });
                void refresh();
              }}
            />
          </>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
