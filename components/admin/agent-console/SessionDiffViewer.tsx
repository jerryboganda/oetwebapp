'use client';

import { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, RefreshCw } from 'lucide-react';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { SessionDiff, SessionDiffFileStatus } from '@/lib/owner-agent/types';
import { VisibleText } from './VisibleText';

const MAX_LINES_PER_FILE = 4_000;

const STATUS_VARIANT: Record<SessionDiffFileStatus, NonNullable<BadgeProps['variant']>> = {
  added: 'success',
  modified: 'info',
  deleted: 'danger',
  renamed: 'violet',
  untracked: 'warning',
};

export interface PatchChunk {
  /** Path from the `diff --git a/… b/…` header (b side). */
  path: string;
  lines: string[];
}

/** Split a unified `git diff` into per-file chunks. */
export function splitPatchByFile(patch: string): PatchChunk[] {
  if (!patch) return [];
  const chunks: PatchChunk[] = [];
  let current: PatchChunk | null = null;
  for (const line of patch.replace(/\r\n/g, '\n').split('\n')) {
    if (line.startsWith('diff --git ')) {
      const match = line.match(/^diff --git a\/(.+?) b\/(.+)$/);
      current = { path: match ? match[2] : line.slice('diff --git '.length), lines: [line] };
      chunks.push(current);
      continue;
    }
    if (!current) {
      current = { path: '(preamble)', lines: [] };
      chunks.push(current);
    }
    current.lines.push(line);
  }
  return chunks;
}

function lineClass(line: string): string | false {
  if (line.startsWith('+++') || line.startsWith('---') || line.startsWith('diff --git') || line.startsWith('index ')) {
    return 'text-admin-fg-muted';
  }
  if (line.startsWith('+')) return 'bg-emerald-500/10 text-emerald-800 dark:text-emerald-300';
  if (line.startsWith('-')) return 'bg-red-500/10 text-red-800 dark:text-red-300';
  if (line.startsWith('@@')) return 'text-sky-700 dark:text-sky-300';
  return false;
}

function FilePatch({ chunk, defaultOpen }: { chunk: PatchChunk; defaultOpen: boolean }) {
  const [open, setOpen] = useState(defaultOpen);
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? chunk.lines : chunk.lines.slice(0, MAX_LINES_PER_FILE);
  return (
    <div className="rounded-lg border border-admin-border">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        className="flex w-full items-center gap-2 px-3 py-2 text-left font-mono text-xs"
      >
        {open ? <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" /> : <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />}
        <span className="min-w-0 truncate"><VisibleText text={chunk.path} className="whitespace-nowrap" /></span>
      </button>
      {open ? (
        <pre className="max-h-[32rem] overflow-auto border-t border-admin-border bg-admin-bg-subtle py-2 font-mono text-xs leading-5">
          {visible.map((line, index) => (
            <span key={index} className={cn('block px-3', lineClass(line))}>
              <VisibleText text={line || ' '} level="prose" />
            </span>
          ))}
          {!showAll && chunk.lines.length > MAX_LINES_PER_FILE ? (
            <span className="block px-3 py-2">
              <Button variant="ghost" size="sm" onClick={() => setShowAll(true)}>
                Show remaining {chunk.lines.length - MAX_LINES_PER_FILE} lines
              </Button>
            </span>
          ) : null}
        </pre>
      ) : null}
    </div>
  );
}

export interface SessionDiffViewerProps {
  diff: SessionDiff | null;
  loading?: boolean;
  error?: string | null;
  onRefresh?: () => void;
}

/** `git diff origin/main...HEAD` + untracked for the session worktree. */
export function SessionDiffViewer({ diff, loading = false, error, onRefresh }: SessionDiffViewerProps) {
  const chunks = useMemo(() => splitPatchByFile(diff?.patch ?? ''), [diff?.patch]);
  const totals = useMemo(
    () => (diff?.files ?? []).reduce((acc, file) => ({ add: acc.add + file.additions, del: acc.del + file.deletions }), { add: 0, del: 0 }),
    [diff?.files],
  );

  return (
    <div className="space-y-3" data-testid="session-diff-viewer">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-admin-fg-muted">
          {diff ? (
            <>
              <span className="font-mono">{diff.baseRef}</span> … <span className="font-mono">{diff.branch}</span> @{' '}
              <span className="font-mono">{diff.head.slice(0, 12)}</span> · {diff.files.length} file(s) ·{' '}
              <span className="text-emerald-700 dark:text-emerald-300">+{totals.add}</span>{' '}
              <span className="text-red-700 dark:text-red-300">−{totals.del}</span>
            </>
          ) : loading ? (
            'Loading diff…'
          ) : (
            'No diff loaded.'
          )}
        </p>
        {onRefresh ? (
          <Button variant="outline" size="sm" onClick={onRefresh} loading={loading}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" /> Refresh
          </Button>
        ) : null}
      </div>
      {error ? (
        <p className="text-xs text-red-700 dark:text-red-300" role="alert">
          {error}
        </p>
      ) : null}
      {diff?.truncated ? (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:bg-amber-950 dark:text-amber-200">
          The patch was truncated at 2 MB. The file list is complete.
        </p>
      ) : null}
      {diff && diff.files.length > 0 ? (
        <ul className="divide-y divide-admin-border rounded-lg border border-admin-border text-xs">
          {diff.files.map((file) => (
            <li key={`${file.status}:${file.path}`} className="flex items-center gap-2 px-3 py-1.5">
              <Badge variant={STATUS_VARIANT[file.status] ?? 'muted'}>{file.status}</Badge>
              <span className="min-w-0 flex-1 truncate font-mono"><VisibleText text={file.path} className="whitespace-nowrap" /></span>
              <span className="text-emerald-700 dark:text-emerald-300">+{file.additions}</span>
              <span className="text-red-700 dark:text-red-300">−{file.deletions}</span>
            </li>
          ))}
        </ul>
      ) : diff ? (
        <p className="text-sm text-admin-fg-muted">No changes against {diff.baseRef}.</p>
      ) : null}
      {chunks.length > 0 ? (
        <div className="space-y-2">
          {chunks.map((chunk, index) => (
            <FilePatch key={`${chunk.path}-${index}`} chunk={chunk} defaultOpen={chunks.length <= 3} />
          ))}
        </div>
      ) : null}
    </div>
  );
}
