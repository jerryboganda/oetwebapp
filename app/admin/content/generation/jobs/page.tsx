'use client';

// SUBAGENT_C: Content-generation job runner + monitor. Replaces the
// `launch-all-live.sh` / `status-all-live.sh` orchestration scripts with a
// focused admin surface that wraps the existing backend endpoints:
//
//   POST  /v1/admin/content/generate            (adminQueueContentGeneration)
//   GET   /v1/admin/content/generation-jobs     (adminListGenerationJobs)
//   GET   /v1/admin/content/generation-jobs/{id} (adminGetGenerationJob)
//
// The parent `/admin/content/generation` page is the long-running creative
// surface (per content type). This `/jobs` page is intentionally focused on
// fleet-style launch + status — the workflow that the shell orchestrators
// owned before this rewrite.

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Loader2, Play, RefreshCw } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { DataTable, type Column } from '@/components/ui/data-table';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  adminListGenerationJobs,
  adminQueueContentGeneration,
} from '@/lib/api';
import type {
  GenerationJobSummary,
  QueueGenerationInput,
} from '@/lib/types/admin/bulk-import';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

interface ModuleOption {
  /** UI label. */
  label: string;
  /** `examTypeCode` + `subtestCode` pair the backend expects. */
  examTypeCode: string;
  subtestCode: string;
}

// Maps the module selector to the (examType, subtest) pair the backend uses.
// These mirror what `launch-all-live.sh` enumerated as it spun up workers.
const MODULES: ModuleOption[] = [
  { label: 'Listening', examTypeCode: 'oet', subtestCode: 'listening' },
  { label: 'Reading', examTypeCode: 'oet', subtestCode: 'reading' },
  { label: 'Speaking', examTypeCode: 'oet', subtestCode: 'speaking' },
  { label: 'Writing', examTypeCode: 'oet', subtestCode: 'writing' },
  { label: 'Grammar', examTypeCode: 'oet', subtestCode: 'grammar' },
  { label: 'Conversation', examTypeCode: 'oet', subtestCode: 'conversation' },
  { label: 'Pronunciation', examTypeCode: 'oet', subtestCode: 'pronunciation' },
  { label: 'Mocks', examTypeCode: 'oet', subtestCode: 'mocks' },
];

const DIFFICULTIES = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'];

function badgeVariantForState(state: string): 'default' | 'muted' | 'danger' {
  const s = state.toLowerCase();
  if (s === 'completed') return 'default';
  if (s === 'failed') return 'danger';
  return 'muted';
}

export default function AdminContentGenerationJobsPage() {
  const { isAuthenticated } = useAdminAuth();
  const [moduleIdx, setModuleIdx] = useState(0);
  const [count, setCount] = useState(5);
  const [difficulty, setDifficulty] = useState<string>('B2');
  const [profession, setProfession] = useState('');
  const [customInstructions, setCustomInstructions] = useState('');
  const [launching, setLaunching] = useState(false);
  const [launchError, setLaunchError] = useState<string | null>(null);
  const [jobs, setJobs] = useState<GenerationJobSummary[]>([]);
  const [loadingJobs, setLoadingJobs] = useState(true);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  const reload = useCallback(async () => {
    setLoadingJobs(true);
    try {
      const result = await adminListGenerationJobs(1, 50);
      setJobs(result.items ?? []);
    } catch (err) {
      setToast({ variant: 'error', message: `Failed to load jobs: ${err instanceof Error ? err.message : String(err)}` });
    } finally {
      setLoadingJobs(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload, reloadNonce]);

  // Lightweight auto-refresh while there are unfinished jobs. Avoids a heavy
  // SignalR subscription for what is a coarse status panel.
  useEffect(() => {
    const hasActive = jobs.some(
      (j) => !['completed', 'failed'].includes(j.state.toLowerCase()),
    );
    if (!hasActive) return;
    const id = window.setInterval(() => setReloadNonce((n) => n + 1), 6_000);
    return () => window.clearInterval(id);
  }, [jobs]);

  const handleLaunch = useCallback(async () => {
    setLaunchError(null);
    const mod = MODULES[moduleIdx];
    const payload: QueueGenerationInput = {
      examTypeCode: mod.examTypeCode,
      subtestCode: mod.subtestCode,
      difficulty,
      count,
      ...(profession.trim() ? { professionId: profession.trim() } : {}),
      ...(customInstructions.trim() ? { customInstructions: customInstructions.trim() } : {}),
    };
    setLaunching(true);
    try {
      const result = await adminQueueContentGeneration(payload);
      setToast({
        variant: 'success',
        message: result.jobId
          ? `Generation job queued (${result.jobId}).`
          : 'Generation job queued.',
      });
      setReloadNonce((n) => n + 1);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setLaunchError(message);
      setToast({ variant: 'error', message });
    } finally {
      setLaunching(false);
    }
  }, [count, customInstructions, difficulty, moduleIdx, profession]);

  const jobColumns: Column<GenerationJobSummary>[] = useMemo(
    () => [
      {
        key: 'jobId',
        header: 'Job',
        render: (j) => <code className="font-mono text-xs">{j.jobId.slice(0, 8)}</code>,
      },
      {
        key: 'module',
        header: 'Module',
        render: (j) => (
          <span className="text-xs">
            {j.examTypeCode.toUpperCase()} · {j.subtestCode}
          </span>
        ),
      },
      {
        key: 'difficulty',
        header: 'Difficulty',
        render: (j) => <Badge variant="muted">{j.difficulty}</Badge>,
      },
      {
        key: 'state',
        header: 'Status',
        render: (j) => <Badge variant={badgeVariantForState(j.state)}>{j.state}</Badge>,
      },
      {
        key: 'progress',
        header: 'Progress',
        render: (j) => (
          <span className="text-xs">
            {j.generatedCount}/{j.requestedCount}
          </span>
        ),
      },
      {
        key: 'started',
        header: 'Started',
        render: (j) => (
          <span className="text-xs text-muted">
            {new Date(j.createdAt).toLocaleString()}
          </span>
        ),
      },
      {
        key: 'completed',
        header: 'Completed',
        render: (j) => (
          <span className="text-xs text-muted">
            {j.completedAt ? new Date(j.completedAt).toLocaleString() : '—'}
          </span>
        ),
      },
      {
        key: 'error',
        header: 'Error',
        render: (j) =>
          j.errorMessage ? (
            <span className="text-xs text-danger" title={j.errorMessage}>
              {j.errorMessage.length > 60 ? `${j.errorMessage.slice(0, 60)}…` : j.errorMessage}
            </span>
          ) : (
            <span className="text-xs text-muted">—</span>
          ),
      },
    ],
    [],
  );

  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        title="Generation jobs"
        description="Queue bulk content-generation jobs across every module, then monitor progress. Replaces the launch-all-live / status-all-live shell scripts."
      />

      <AdminRoutePanel title="Launch generation">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <label className="flex flex-col gap-1 text-xs">
            <span className="font-medium">Module</span>
            <Select
              value={moduleIdx}
              onChange={(e) => setModuleIdx(Number(e.target.value))}
              disabled={launching}
              options={MODULES.map((m, idx) => ({ value: String(idx), label: m.label }))}
            />
          </label>

          <label className="flex flex-col gap-1 text-xs">
            <span className="font-medium">Difficulty</span>
            <Select
              value={difficulty}
              onChange={(e) => setDifficulty(e.target.value)}
              disabled={launching}
              options={DIFFICULTIES.map((d) => ({ value: d, label: d }))}
            />
          </label>

          <label className="flex flex-col gap-1 text-xs">
            <span className="font-medium">Count</span>
            <Input
              type="number"
              min={1}
              max={100}
              value={count}
              onChange={(e) => setCount(Math.max(1, Number(e.target.value) || 1))}
              disabled={launching}
            />
          </label>

          <label className="flex flex-col gap-1 text-xs">
            <span className="font-medium">Profession (optional)</span>
            <Input
              placeholder="e.g. nursing"
              value={profession}
              onChange={(e) => setProfession(e.target.value)}
              disabled={launching}
            />
          </label>
        </div>

        <label className="mt-3 flex flex-col gap-1 text-xs">
          <span className="font-medium">Custom instructions (optional)</span>
          <Textarea
            rows={3}
            value={customInstructions}
            onChange={(e) => setCustomInstructions(e.target.value)}
            disabled={launching}
            placeholder="Extra context passed to the grounded prompt builder."
          />
        </label>

        {launchError && (
          <InlineAlert variant="error" title="Launch failed" className="mt-3">
            {launchError}
          </InlineAlert>
        )}

        <div className="mt-3 flex items-center gap-2">
          <Button variant="primary" onClick={handleLaunch} disabled={launching}>
            {launching ? (
              <>
                <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Queuing…
              </>
            ) : (
              <>
                <Play className="mr-1 h-4 w-4" /> Launch {count} job{count === 1 ? '' : 's'}
              </>
            )}
          </Button>
          <Link
            href="/admin/content/generation"
            className="text-xs text-primary underline"
          >
            Open per-type generator
          </Link>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel
        title="Job history"
        actions={
          <Button variant="outline" size="sm" onClick={() => setReloadNonce((n) => n + 1)} disabled={loadingJobs}>
            <RefreshCw className={`mr-1 h-4 w-4 ${loadingJobs ? 'animate-spin' : ''}`} /> Refresh
          </Button>
        }
      >
        {jobs.length === 0 && !loadingJobs ? (
          <p className="text-sm text-muted">No generation jobs yet. Queue one above to get started.</p>
        ) : (
          <DataTable columns={jobColumns} data={jobs} keyExtractor={(j) => j.jobId} />
        )}
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
