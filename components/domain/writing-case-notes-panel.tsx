'use client';

import { cn } from '@/lib/utils';
import { SelectionToVocab } from '@/components/domain/vocabulary';

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
  className?: string;
}

export function WritingCaseNotesPanel({
  caseNotes, scratchpad = '', onScratchpadChange,
  checklist, onChecklistChange, activeTab = 'notes', onTabChange, className, taskId,
}: WritingCaseNotesPanelProps) {
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
      <div className="flex-1 overflow-y-auto p-4 text-sm">
        {activeTab === 'notes' && (
          <SelectionToVocab
            source="writing"
            sourceRefPrefix={taskId ? `writing:${taskId}` : 'writing:case-notes'}
          >
            <div className="prose prose-sm max-w-none text-navy whitespace-pre-wrap">{caseNotes}</div>
          </SelectionToVocab>
        )}
        {activeTab === 'scratchpad' && (
          <textarea
            value={scratchpad}
            onChange={(e) => onScratchpadChange?.(e.target.value)}
            placeholder="Type your notes here..."
            aria-label="Scratchpad"
            className="w-full h-full min-h-[200px] text-sm border-0 resize-none focus:outline-none text-navy placeholder:text-muted/50"
          />
        )}
        {activeTab === 'checklist' && checklist && (
          <ul className="space-y-2">
            {checklist.map((item) => (
              <li key={item.id}>
                <label className="flex items-start gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={item.checked}
                    onChange={(e) => onChecklistChange?.(item.id, e.target.checked)}
                    className="mt-0.5 w-4 h-4 rounded border-gray-300 text-primary focus:ring-primary"
                  />
                  <span className={cn('text-sm', item.checked && 'line-through text-muted')}>{item.label}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
