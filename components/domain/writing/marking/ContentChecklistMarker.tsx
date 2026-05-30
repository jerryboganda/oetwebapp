'use client';

/**
 * ContentChecklistMarker — per-item verdict marking for the task's key and
 * irrelevant content checklists (spec §13/§14).
 *
 * Each checklist item gets a verdict selector (included / missing / inaccurate
 * / irrelevant). Verdicts are collected by the parent into a
 * `contentChecklistVerdict` map (itemId → verdict) for submit.
 *
 * Where the pre-assessment flagged missing or irrelevant content we surface a
 * lightweight hint so the tutor's verdict can be informed by the AI signal
 * (without auto-deciding it).
 */

import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type {
  WritingChecklistVerdict,
  WritingContentChecklistItemDto,
} from '@/lib/writing/types';
import { CHECKLIST_VERDICTS, SEVERITY_STYLE, VERDICT_BADGE_CLASS } from './shared';

export interface ContentChecklistMarkerProps {
  keyItems: WritingContentChecklistItemDto[];
  irrelevantItems: WritingContentChecklistItemDto[];
  verdicts: Record<string, WritingChecklistVerdict>;
  onChange: (itemId: string, verdict: WritingChecklistVerdict) => void;
  /** Items flagged by the AI as missing (matched by itemText), for hinting. */
  aiMissing?: string[];
  aiIrrelevant?: string[];
  readOnly?: boolean;
  className?: string;
}

function VerdictSelector({
  itemId,
  value,
  onChange,
  readOnly,
}: {
  itemId: string;
  value: WritingChecklistVerdict | undefined;
  onChange: (itemId: string, verdict: WritingChecklistVerdict) => void;
  readOnly?: boolean;
}) {
  return (
    <div role="radiogroup" aria-label="Verdict" className="flex flex-wrap gap-1">
      {CHECKLIST_VERDICTS.map((v) => {
        const active = value === v.value;
        return (
          <button
            key={v.value}
            type="button"
            role="radio"
            aria-checked={active}
            disabled={readOnly}
            title={v.hint}
            onClick={() => onChange(itemId, v.value)}
            className={cn(
              'rounded-full border px-2.5 py-0.5 text-xs font-semibold transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:opacity-60',
              active
                ? cn(VERDICT_BADGE_CLASS[v.value], 'border-transparent ring-1 ring-primary/40')
                : 'border-border bg-surface text-muted hover:bg-background-light',
            )}
          >
            {v.label}
          </button>
        );
      })}
    </div>
  );
}

function ChecklistGroup({
  title,
  items,
  verdicts,
  onChange,
  aiFlags,
  flagLabel,
  readOnly,
}: {
  title: string;
  items: WritingContentChecklistItemDto[];
  verdicts: Record<string, WritingChecklistVerdict>;
  onChange: (itemId: string, verdict: WritingChecklistVerdict) => void;
  aiFlags: Set<string>;
  flagLabel: string;
  readOnly?: boolean;
}) {
  if (items.length === 0) return null;
  const decided = items.filter((it) => verdicts[it.id]).length;
  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h4 className="text-xs font-bold uppercase tracking-wider text-muted">{title}</h4>
        <span className="text-xs text-muted">{decided}/{items.length} marked</span>
      </div>
      <ul className="space-y-2">
        {[...items].sort((a, b) => a.ordinal - b.ordinal).map((item) => {
          const severity = SEVERITY_STYLE[item.importance];
          const flagged = aiFlags.has(item.itemText.trim().toLowerCase());
          return (
            <li key={item.id} className="rounded-xl border border-border bg-surface p-3">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="text-sm text-navy">{item.itemText}</p>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <span className={cn('inline-flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide', severity.badgeClass)}>
                      <span aria-hidden="true">{severity.glyph}</span> {severity.tag}
                    </span>
                    {item.category ? (
                      <span className="text-[10px] font-semibold uppercase tracking-wide text-muted">{item.category}</span>
                    ) : null}
                    {flagged ? (
                      <Badge variant="warning" size="sm">AI: {flagLabel}</Badge>
                    ) : null}
                  </div>
                  {item.expectedRepresentation ? (
                    <p className="mt-1 text-xs text-muted">Expected: {item.expectedRepresentation}</p>
                  ) : null}
                </div>
              </div>
              <div className="mt-2">
                <VerdictSelector itemId={item.id} value={verdicts[item.id]} onChange={onChange} readOnly={readOnly} />
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export function ContentChecklistMarker({
  keyItems,
  irrelevantItems,
  verdicts,
  onChange,
  aiMissing = [],
  aiIrrelevant = [],
  readOnly = false,
  className,
}: ContentChecklistMarkerProps) {
  const missingSet = new Set(aiMissing.map((s) => s.trim().toLowerCase()));
  const irrelevantSet = new Set(aiIrrelevant.map((s) => s.trim().toLowerCase()));

  if (keyItems.length === 0 && irrelevantItems.length === 0) {
    return (
      <p className={cn('rounded-lg border border-dashed border-border bg-background-light p-3 text-xs text-muted', className)}>
        This task has no authored content checklist.
      </p>
    );
  }

  return (
    <div className={cn('space-y-4', className)}>
      <ChecklistGroup
        title="Key content"
        items={keyItems}
        verdicts={verdicts}
        onChange={onChange}
        aiFlags={missingSet}
        flagLabel="likely missing"
        readOnly={readOnly}
      />
      <ChecklistGroup
        title="Irrelevant content (should be excluded)"
        items={irrelevantItems}
        verdicts={verdicts}
        onChange={onChange}
        aiFlags={irrelevantSet}
        flagLabel="detected"
        readOnly={readOnly}
      />
    </div>
  );
}
