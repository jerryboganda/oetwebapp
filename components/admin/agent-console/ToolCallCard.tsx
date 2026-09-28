'use client';

import { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, Loader2, Wrench } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { ConsoleItem } from '@/lib/owner-agent/event-reducer';
import { VisibleText } from './VisibleText';

type ToolItem = Extract<ConsoleItem, { kind: 'tool' }>;

const PREVIEW_LIMIT = 64 * 1024;

function stringifyInput(input: unknown): string {
  if (input === undefined || input === null) return '';
  if (typeof input === 'string') return input;
  try {
    return JSON.stringify(input, null, 2);
  } catch {
    return String(input);
  }
}

const STATUS_BADGE: Record<ToolItem['status'], { label: string; variant: 'info' | 'success' | 'danger' | 'muted' }> = {
  running: { label: 'Running', variant: 'info' },
  ok: { label: 'Done', variant: 'success' },
  error: { label: 'Failed', variant: 'danger' },
  no_result: { label: 'No result', variant: 'muted' },
};

export interface ToolCallCardProps {
  tool: ToolItem;
  /** An approval for this call is waiting on the owner. */
  awaitingApproval?: boolean;
}

/**
 * One engine tool call: name, Guard classification, command/cwd, input, and
 * the streamed output replaced by the final result when it arrives. All text
 * is rendered escaped with hidden characters revealed.
 */
export function ToolCallCard({ tool, awaitingApproval = false }: ToolCallCardProps) {
  const [open, setOpen] = useState(tool.status === 'running' || tool.status === 'error');
  const inputText = useMemo(() => stringifyInput(tool.input), [tool.input]);
  const output = tool.result ? tool.result.output : tool.streamedOutput;
  const outputPreview = output.length > PREVIEW_LIMIT ? output.slice(output.length - PREVIEW_LIMIT) : output;
  const status = STATUS_BADGE[tool.status];
  const classification = tool.classification;

  return (
    <div
      className={cn(
        'rounded-lg border bg-admin-bg-surface text-xs',
        tool.status === 'error' ? 'border-red-300 dark:border-red-800' : 'border-admin-border',
      )}
      data-testid="tool-call-card"
    >
      <button
        type="button"
        className="flex w-full items-center gap-2 px-3 py-2 text-left"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        {open ? <ChevronDown className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> : <ChevronRight className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />}
        <Wrench className="h-3.5 w-3.5 shrink-0 text-admin-fg-muted" aria-hidden="true" />
        <span className="min-w-0 flex-1 truncate font-mono font-semibold text-admin-fg-strong">
          {tool.name}
          {tool.command ? (
            <span className="ml-2 font-normal text-admin-fg-muted">
              <VisibleText text={tool.command.slice(0, 120)} className="whitespace-nowrap" />
            </span>
          ) : null}
        </span>
        {tool.status === 'running' ? <Loader2 className="h-3.5 w-3.5 animate-spin motion-reduce:animate-none" aria-hidden="true" /> : null}
        {awaitingApproval ? <Badge variant="warning">Awaiting approval</Badge> : null}
        {classification?.destructive ? <Badge variant="danger">Destructive</Badge> : null}
        {classification?.unparseable ? <Badge variant="danger">Unparseable</Badge> : null}
        <Badge variant={status.variant}>
          {status.label}
          {tool.result?.exitCode !== undefined ? ` · exit ${tool.result.exitCode}` : ''}
        </Badge>
      </button>
      {open ? (
        <div className="space-y-2 border-t border-admin-border px-3 py-2">
          {tool.command ? (
            <div>
              <p className="text-3xs font-semibold uppercase tracking-wide text-admin-fg-muted">Command</p>
              <pre className="mt-1 max-h-48 overflow-auto rounded bg-admin-bg-subtle p-2 font-mono">
                <VisibleText text={tool.command} />
              </pre>
            </div>
          ) : null}
          {tool.cwd ? (
            <p className="text-admin-fg-muted">
              cwd <span className="font-mono text-admin-fg-default"><VisibleText text={tool.cwd} /></span>
            </p>
          ) : null}
          {classification && classification.reasons.length > 0 ? (
            <ul className="list-disc pl-5 text-admin-fg-muted">
              {classification.reasons.map((reason, index) => (
                <li key={`${reason}-${index}`}>{reason}</li>
              ))}
            </ul>
          ) : null}
          {inputText && !tool.command ? (
            <div>
              <p className="text-3xs font-semibold uppercase tracking-wide text-admin-fg-muted">Input</p>
              <pre className="mt-1 max-h-48 overflow-auto rounded bg-admin-bg-subtle p-2 font-mono">
                <VisibleText text={inputText.slice(0, PREVIEW_LIMIT)} />
              </pre>
            </div>
          ) : null}
          {outputPreview ? (
            <div>
              <p className="text-3xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                {tool.result ? 'Result' : 'Output (streaming)'}
                {tool.outputTruncated || output.length > PREVIEW_LIMIT ? ' · showing the tail' : ''}
              </p>
              <pre className="mt-1 max-h-72 overflow-auto rounded bg-admin-bg-subtle p-2 font-mono" data-testid="tool-output">
                <VisibleText text={outputPreview} level="prose" />
              </pre>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
