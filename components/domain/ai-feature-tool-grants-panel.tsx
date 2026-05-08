'use client';

import { useEffect, useMemo, useState } from 'react';
import { Trash2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import {
  deleteAiFeatureToolGrant,
  fetchAiFeatureToolGrants,
  fetchAiTools,
  updateAiFeatureToolGrant,
  upsertAiFeatureToolGrant,
  type AiFeatureToolGrantRow,
  type AiToolRow,
} from '@/lib/ai-management-api';

/**
 * Phase 5 — per-feature AI tool grants.
 *
 * Deny-by-default: a feature only "knows" a tool exists when an active
 * AiFeatureToolGrant + active AiTool row both exist. The model never even
 * sees the tool definition otherwise. Mutations re-invalidate the registry
 * cache server-side; the UI just refetches the current view.
 */

// Mirror of OetLearner.Api.Domain.AiFeatureCodes (backend enforces server-side;
// this is the picker list for "Add grant").
const KNOWN_FEATURE_CODES = [
  'writing.grade',
  'writing.sample_score',
  'speaking.grade',
  'mock.full_grade',
  'writing.coach.suggest',
  'writing.coach.explain',
  'conversation.reply',
  'conversation.opening',
  'conversation.evaluation',
  'pronunciation.tip',
  'pronunciation.score',
  'pronunciation.feedback',
  'summarise.passage',
  'vocabulary.gloss',
  'recalls.mistake_explain',
  'recalls.revision_plan',
  'mock.remediation_draft',
  'admin.content_generation',
  'admin.grammar_draft',
  'admin.pronunciation_draft',
  'admin.vocabulary_draft',
  'admin.conversation_draft',
  'admin.listening_draft',
] as const;

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export function AiFeatureToolGrantsPanel() {
  const [tools, setTools] = useState<AiToolRow[]>([]);
  const [grants, setGrants] = useState<AiFeatureToolGrantRow[]>([]);
  const [filterFeature, setFilterFeature] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);

  const [draftFeature, setDraftFeature] = useState('');
  const [draftTool, setDraftTool] = useState('');

  const reload = async () => {
    setLoading(true);
    try {
      const [t, g] = await Promise.all([
        fetchAiTools(),
        fetchAiFeatureToolGrants(filterFeature || undefined),
      ]);
      setTools(t.tools);
      setGrants(g.grants);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to load tool grants.';
      setToast({ variant: 'error', message });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterFeature]);

  const activeTools = useMemo(() => tools.filter((t) => t.isActive), [tools]);

  const grantedToolCodesForDraftFeature = useMemo(
    () =>
      new Set(
        grants
          .filter((g) => g.featureCode === draftFeature && g.isActive)
          .map((g) => g.toolCode),
      ),
    [grants, draftFeature],
  );

  const draftToolOptions = useMemo(
    () =>
      activeTools
        .filter((t) => !grantedToolCodesForDraftFeature.has(t.code))
        .map((t) => ({ value: t.code, label: `${t.code} — ${t.name}` })),
    [activeTools, grantedToolCodesForDraftFeature],
  );

  const handleAdd = async () => {
    if (!draftFeature || !draftTool) {
      setToast({ variant: 'error', message: 'Pick a feature and a tool.' });
      return;
    }
    try {
      await upsertAiFeatureToolGrant({
        featureCode: draftFeature,
        toolCode: draftTool,
        isActive: true,
      });
      setToast({ variant: 'success', message: `Granted ${draftTool} to ${draftFeature}` });
      setDraftTool('');
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Save failed.';
      setToast({ variant: 'error', message });
    }
  };

  const handleToggle = async (row: AiFeatureToolGrantRow) => {
    try {
      await updateAiFeatureToolGrant(row.id, { isActive: !row.isActive });
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Toggle failed.';
      setToast({ variant: 'error', message });
    }
  };

  const handleDelete = async (row: AiFeatureToolGrantRow) => {
    try {
      await deleteAiFeatureToolGrant(row.id);
      setToast({
        variant: 'success',
        message: `Removed ${row.toolCode} from ${row.featureCode}`,
      });
      await reload();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Delete failed.';
      setToast({ variant: 'error', message });
    }
  };

  const categoryVariant = (category: string | null) => {
    switch (category) {
      case 'External':
        return 'warning' as const;
      case 'Write':
        return 'info' as const;
      case 'Read':
        return 'success' as const;
      default:
        return 'muted' as const;
    }
  };

  const columns: Column<AiFeatureToolGrantRow>[] = [
    {
      key: 'featureCode',
      header: 'Feature',
      render: (r) => <span className="font-mono text-xs">{r.featureCode}</span>,
    },
    {
      key: 'toolCode',
      header: 'Tool',
      render: (r) => (
        <div className="flex flex-col">
          <span className="font-mono text-xs">{r.toolCode}</span>
          <span className="text-xs text-muted">{r.toolName}</span>
        </div>
      ),
    },
    {
      key: 'toolCategory',
      header: 'Category',
      render: (r) => (
        <Badge variant={categoryVariant(r.toolCategory)}>{r.toolCategory ?? 'unknown'}</Badge>
      ),
    },
    {
      key: 'isActive',
      header: 'Status',
      render: (r) => (
        <Badge variant={r.isActive && r.toolActive ? 'success' : 'muted'}>
          {!r.toolActive ? 'tool inactive' : r.isActive ? 'active' : 'paused'}
        </Badge>
      ),
    },
    {
      key: 'updatedAt',
      header: 'Updated',
      render: (r) => (
        <span className="text-xs text-muted">{new Date(r.updatedAt).toLocaleString()}</span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (r) => (
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" onClick={() => handleToggle(r)}>
            {r.isActive ? 'Pause' : 'Resume'}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => handleDelete(r)}
            aria-label={`Delete grant ${r.featureCode} ${r.toolCode}`}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <section
      className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
      aria-label="AI feature tool grants"
    >
      <div className="mb-4">
        <h2 className="text-lg font-semibold">AI tool grants</h2>
        <p className="text-sm text-muted">
          Deny-by-default per-feature grant of AI tools. Without an active grant, the model is never
          told the tool exists. <span className="font-mono text-xs">External</span> tools (e.g.
          dictionary lookup) make outbound HTTP from the API.
        </p>
      </div>

      <div className="mb-4 grid gap-3 rounded-xl border border-border-muted bg-surface-muted p-4 md:grid-cols-3">
        <Select
          label="Filter feature"
          value={filterFeature}
          onChange={(e) => setFilterFeature(e.target.value)}
          placeholder="— all features —"
          options={[
            { value: '', label: 'All features' },
            ...KNOWN_FEATURE_CODES.map((c) => ({ value: c, label: c })),
          ]}
        />
        <div className="md:col-span-2 flex items-end justify-end">
          <Badge variant="outline" className="font-mono">
            {tools.length} tools registered · {activeTools.length} active
          </Badge>
        </div>
      </div>

      <div className="mb-4 grid gap-3 rounded-xl border border-border-muted bg-surface-muted p-4 md:grid-cols-3">
        <Select
          label="Feature"
          value={draftFeature}
          onChange={(e) => setDraftFeature(e.target.value)}
          placeholder="— pick feature —"
          options={KNOWN_FEATURE_CODES.map((c) => ({ value: c, label: c }))}
        />
        <Select
          label="Tool"
          value={draftTool}
          onChange={(e) => setDraftTool(e.target.value)}
          placeholder={draftFeature ? '— pick tool —' : 'pick a feature first'}
          options={draftToolOptions}
        />
        <div className="flex items-end">
          <Button
            variant="primary"
            onClick={handleAdd}
            disabled={!draftFeature || !draftTool}
          >
            Add grant
          </Button>
        </div>
      </div>

      <DataTable
        data={loading ? [] : grants}
        columns={columns}
        keyExtractor={(r) => r.id}
        emptyMessage={
          loading
            ? 'Loading…'
            : filterFeature
              ? `No tools granted to ${filterFeature}.`
              : 'No tool grants configured. Tools default to denied for every feature.'
        }
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </section>
  );
}
