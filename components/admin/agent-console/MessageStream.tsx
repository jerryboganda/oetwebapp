'use client';

import { memo, useEffect, useMemo, useRef } from 'react';
import { AlertTriangle, Brain, Camera, FileText, Flag, ShieldCheck, Siren, User } from 'lucide-react';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { ConsoleItem } from '@/lib/owner-agent/event-reducer';
import { toSafeConsoleMarkdown } from '@/lib/owner-agent/text-safety';
import type { ApprovalDecision, ApprovalRequest } from '@/lib/owner-agent/types';
import { ApprovalCard } from './ApprovalCard';
import { ToolCallCard } from './ToolCallCard';
import { VisibleText } from './VisibleText';

export interface MessageStreamProps {
  items: readonly ConsoleItem[];
  pendingApprovals: readonly ApprovalRequest[];
  onDecide?: (approval: ApprovalRequest, decision: ApprovalDecision, note?: string) => Promise<void>;
  emptyHint?: string;
  className?: string;
}

const MODE_LABEL: Record<string, string> = {
  read_only: 'Read-only',
  guarded: 'Guarded',
  autopilot: 'Autopilot',
};

function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '';
  if (ms < 1000) return `${ms} ms`;
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${seconds}s`;
  return `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

function AssistantMessage({ text, streaming }: { text: string; streaming: boolean }) {
  // Untrusted markdown: hidden chars revealed, links/images neutralised to
  // text, raw HTML rendered as escaped text by MarkdownContent.
  const safe = useMemo(() => toSafeConsoleMarkdown(text), [text]);
  return (
    <div className="rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default" data-testid="assistant-message">
      {safe ? <MarkdownContent markdown={safe} className="space-y-2 break-words" /> : null}
      {streaming ? (
        <span className="ml-0.5 inline-block h-4 w-1.5 animate-pulse bg-admin-fg-muted align-middle motion-reduce:animate-none" aria-label="Streaming" />
      ) : null}
    </div>
  );
}

function ThinkingBlock({ text, streaming }: { text: string; streaming: boolean }) {
  return (
    <details className="group rounded-lg border border-dashed border-admin-border px-3 py-2 text-xs text-admin-fg-muted" open={streaming || undefined}>
      <summary className="flex cursor-pointer list-none items-center gap-2 font-medium">
        <Brain className="h-3.5 w-3.5" aria-hidden="true" />
        {streaming ? 'Thinking…' : 'Thinking (summarized)'}
      </summary>
      <p className="mt-2 whitespace-pre-wrap break-words leading-5">
        <VisibleText text={text} level="prose" />
      </p>
    </details>
  );
}

const StreamItem = memo(function StreamItem({
  item,
  pendingIds,
  pendingToolCallIds,
  onDecide,
}: {
  item: ConsoleItem;
  pendingIds: ReadonlySet<string>;
  pendingToolCallIds: ReadonlySet<string>;
  onDecide?: MessageStreamProps['onDecide'];
}) {
  switch (item.kind) {
    case 'turn':
      return (
        <div className="flex items-center gap-2 pt-2 text-[11px] uppercase tracking-wide text-admin-fg-muted" role="separator">
          <span className="h-px flex-1 bg-admin-border" aria-hidden="true" />
          <span className="font-mono normal-case">
            {item.model || 'model'}
            {item.effort ? ` · ${item.effort}` : ''}
            {item.mode ? ` · ${MODE_LABEL[item.mode] ?? item.mode}` : ''}
          </span>
          <span className="h-px flex-1 bg-admin-border" aria-hidden="true" />
        </div>
      );
    case 'user':
      return (
        <div className="ml-auto max-w-[85%] rounded-lg bg-[var(--admin-primary-tint)] px-3 py-2 text-sm text-admin-fg-strong" data-testid="user-message">
          <p className="mb-1 flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-admin-fg-muted">
            <User className="h-3 w-3" aria-hidden="true" /> You
          </p>
          <p className="whitespace-pre-wrap break-words">
            <VisibleText text={item.text} level="prose" />
          </p>
        </div>
      );
    case 'assistant':
      return <AssistantMessage text={item.text} streaming={item.streaming} />;
    case 'thinking':
      return <ThinkingBlock text={item.text} streaming={item.streaming} />;
    case 'tool':
      return <ToolCallCard tool={item} awaitingApproval={pendingToolCallIds.has(item.toolCallId)} />;
    case 'file_change':
      return (
        <details className="rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-xs">
          <summary className="flex cursor-pointer list-none items-center gap-2">
            <FileText className="h-3.5 w-3.5 text-admin-fg-muted" aria-hidden="true" />
            <Badge variant={item.changeKind === 'delete' ? 'danger' : item.changeKind === 'add' ? 'success' : 'info'}>{item.changeKind}</Badge>
            <span className="min-w-0 truncate font-mono"><VisibleText text={item.path} /></span>
          </summary>
          {item.diff ? (
            <pre className="mt-2 max-h-64 overflow-auto rounded bg-admin-bg-subtle p-2 font-mono leading-5">
              {item.diff.split('\n').slice(0, 400).map((line, index) => (
                <span
                  key={index}
                  className={cn(
                    'block',
                    line.startsWith('+') && !line.startsWith('+++') && 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-300',
                    line.startsWith('-') && !line.startsWith('---') && 'bg-red-500/10 text-red-700 dark:text-red-300',
                    line.startsWith('@@') && 'text-sky-700 dark:text-sky-300',
                  )}
                >
                  <VisibleText text={line || ' '} level="prose" />
                </span>
              ))}
            </pre>
          ) : (
            <p className="mt-2 text-admin-fg-muted">No inline diff; see the Diff tab.</p>
          )}
        </details>
      );
    case 'approval': {
      const pending = pendingIds.has(item.request.approvalId) && !item.resolution;
      return (
        <ApprovalCard
          approval={item.request}
          resolution={item.resolution ? { decision: item.resolution.decision, by: item.resolution.by } : null}
          onDecide={pending && onDecide ? (decision, note) => onDecide(item.request, decision, note) : undefined}
        />
      );
    }
    case 'snapshot':
      return (
        <p className={cn('flex items-center gap-2 text-xs', item.ok ? 'text-admin-fg-muted' : 'text-red-700 dark:text-red-300')}>
          <Camera className="h-3.5 w-3.5" aria-hidden="true" />
          {item.ok ? 'Pre-snapshot saved' : 'Pre-snapshot failed'}: <span className="font-mono">{item.label}</span>
          {item.error ? <span>— <VisibleText text={item.error} level="prose" /></span> : null}
        </p>
      );
    case 'taint':
      return (
        <p className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-1.5 text-xs text-red-800 dark:bg-red-950 dark:text-red-200" role="status">
          <Siren className="h-3.5 w-3.5" aria-hidden="true" />
          Turn tainted by <span className="font-mono">{item.source}</span>: <VisibleText text={item.reason} level="prose" />
        </p>
      );
    case 'mode_changed':
      return (
        <p className="flex items-center gap-2 text-xs text-admin-fg-muted" role="status">
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
          Mode → <strong>{MODE_LABEL[item.mode] ?? item.mode}</strong>
          {item.reason ? <span>({item.reason})</span> : null}
        </p>
      );
    case 'turn_complete':
      return (
        <p className="flex items-center gap-2 text-[11px] text-admin-fg-muted">
          <Flag className="h-3 w-3" aria-hidden="true" />
          Turn {item.status === 'ok' ? 'finished' : item.status.replace('_', ' ')}
          {formatDuration(item.durationMs) ? ` in ${formatDuration(item.durationMs)}` : ''}
        </p>
      );
    case 'error':
      return (
        <div className="flex items-start gap-2 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-xs text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200" role="alert">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span>
            <span className="font-mono">{item.code}</span>: <VisibleText text={item.message} level="prose" />
          </span>
        </div>
      );
    default:
      return null;
  }
});

/**
 * Conversation transcript. Every string is rendered through React (escaped);
 * assistant markdown goes through `toSafeConsoleMarkdown` first. Sticks to the
 * bottom while new output streams unless the owner scrolled up.
 */
export function MessageStream({ items, pendingApprovals, onDecide, emptyHint, className }: MessageStreamProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const stickRef = useRef(true);

  const pendingIds = useMemo(() => new Set(pendingApprovals.map((a) => a.approvalId)), [pendingApprovals]);
  const pendingToolCallIds = useMemo(() => new Set(pendingApprovals.map((a) => a.toolCallId)), [pendingApprovals]);

  const lastItem = items[items.length - 1];
  const lastSignature = lastItem
    ? `${items.length}:${lastItem.key}:${lastItem.kind === 'assistant' || lastItem.kind === 'thinking' ? lastItem.text.length : lastItem.kind === 'tool' ? lastItem.streamedOutput.length : 0}`
    : '0';

  useEffect(() => {
    const element = containerRef.current;
    if (!element || !stickRef.current) return;
    element.scrollTop = element.scrollHeight;
  }, [lastSignature]);

  return (
    <div
      ref={containerRef}
      className={cn('flex flex-col gap-3 overflow-y-auto', className)}
      onScroll={(event) => {
        const element = event.currentTarget;
        stickRef.current = element.scrollHeight - element.scrollTop - element.clientHeight < 80;
      }}
      role="log"
      aria-live="polite"
      aria-relevant="additions"
      data-testid="message-stream"
    >
      {items.length === 0 ? (
        <p className="py-8 text-center text-sm text-admin-fg-muted">{emptyHint ?? 'No activity yet.'}</p>
      ) : (
        items.map((item) => (
          <StreamItem key={item.key} item={item} pendingIds={pendingIds} pendingToolCallIds={pendingToolCallIds} onDecide={onDecide} />
        ))
      )}
    </div>
  );
}
