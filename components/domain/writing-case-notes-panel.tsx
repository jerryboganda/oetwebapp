'use client';

import { SelectionToVocab } from "@/components/domain/vocabulary/SelectionToVocab";
import { cn } from '@/lib/utils';

interface WritingCaseNotesPanelProps {
  caseNotes: string;
  scratchpad?: string;
  onScratchpadChange?: (val: string) => void;
  checklist?: { id: string; label: string; checked: boolean }[];
  onChecklistChange?: (id: string, checked: boolean) => void;
  activeTab?: 'notes' | 'scratchpad' | 'checklist';
  onTabChange?: (tab: 'notes' | 'scratchpad' | 'checklist') => void;
  /** Optional identifier used to tag saved terms with a source ref. */
  taskId?: string;
  /**
   * When true, the reading-window lock is active (first 5 minutes of an OET
   * Writing attempt): case notes remain readable but text selection, copy,
   * context-menu and vocabulary highlighting are blocked. Scratchpad is
   * disabled, and checklist interactions are disabled too.
   *
   * Source: Dr. Ahmed Hesham corrections — reading window rule applies to
   * ALL professions regardless of sub-test.
   */
  readingWindowLocked?: boolean;
  className?: string;
}

export function WritingCaseNotesPanel({
  caseNotes, scratchpad = '', onScratchpadChange,
  checklist, onChecklistChange, activeTab = 'notes', onTabChange, className, taskId,
  readingWindowLocked = false,
}: WritingCaseNotesPanelProps) {
  const blockInteractionProps = readingWindowLocked
    ? {
        onCopy: (e: React.ClipboardEvent) => e.preventDefault(),
        onCut: (e: React.ClipboardEvent) => e.preventDefault(),
        onContextMenu: (e: React.MouseEvent) => e.preventDefault(),
        onDragStart: (e: React.DragEvent) => e.preventDefault(),
      }
    : {};

  return (
    <div className={cn('flex flex-col h-full bg-surface border-r border-gray-200', className)}>
      {/* Tabs */}
      <div className="flex border-b border-gray-200 shrink-0" role="tablist" aria-label="Case notes tabs">
        {(['notes', 'scratchpad', 'checklist'] as const).map((tab) => (
          <button
            key={tab}
            onClick={() => onTabChange?.(tab)}
            role="tab"
            aria-selected={activeTab === tab}
            className={cn(
              'flex-1 py-3 text-xs font-semibold capitalize transition-colors',
              activeTab === tab ? 'text-primary border-b-2 border-primary' : 'text-muted hover:text-navy',
            )}
          >
            {tab === 'notes' ? 'Case Notes' : tab === 'scratchpad' ? 'Scratchpad' : 'Checklist'}
          </button>
        ))}
      </div>

      {/* Content */}
      <div
        className={cn('flex-1 overflow-y-auto p-4 text-sm', readingWindowLocked && 'select-none')}
        {...blockInteractionProps}
      >
        {activeTab === 'notes' && (
          readingWindowLocked ? (
            <div className="prose prose-sm max-w-none text-navy whitespace-pre-wrap" aria-describedby="writing-reading-lock-hint">
              {caseNotes}
            </div>
          ) : (
            <SelectionToVocab
              source="writing"
              sourceRefPrefix={taskId ? `writing:${taskId}` : 'writing:case-notes'}
            >
              <div className="prose prose-sm max-w-none text-navy whitespace-pre-wrap">{caseNotes}</div>
            </SelectionToVocab>
          )
        )}
        {activeTab === 'scratchpad' && (
          <textarea
            value={scratchpad}
            onChange={(e) => onScratchpadChange?.(e.target.value)}
            placeholder={readingWindowLocked ? 'Scratchpad is locked during the 5-minute reading window.' : 'Type your notes here...'}
            aria-label="Scratchpad"
            readOnly={readingWindowLocked}
            disabled={readingWindowLocked}
            className={cn(
              'w-full h-full min-h-[200px] text-sm border-0 resize-none focus:outline-none text-navy placeholder:text-muted/50',
              readingWindowLocked && 'opacity-60 cursor-not-allowed bg-gray-50',
            )}
          />
        )}
        {activeTab === 'checklist' && checklist && (
          <ul className="space-y-2">
            {checklist.map((item) => (
              <li key={item.id}>
                <label className={cn('flex items-start gap-2', readingWindowLocked ? 'cursor-not-allowed opacity-60' : 'cursor-pointer')}>
                  <input
                    type="checkbox"
                    checked={item.checked}
                    disabled={readingWindowLocked}
                    onChange={(e) => onChecklistChange?.(item.id, e.target.checked)}
                    className="mt-0.5 w-4 h-4 rounded border-gray-300 text-primary focus:ring-primary disabled:opacity-60"
                  />
                  <span className={cn('text-sm', item.checked && 'line-through text-muted')}>{item.label}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
        {readingWindowLocked ? (
          <p id="writing-reading-lock-hint" className="sr-only">
            Reading window active: case notes are read-only. Typing, highlighting, and annotation are blocked until the 5-minute reading window ends.
          </p>
        ) : null}
      </div>
    </div>
  );
}
