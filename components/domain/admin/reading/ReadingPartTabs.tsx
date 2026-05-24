'use client';

import { cn } from '@/lib/utils';
import type { ReadingPartCode } from '@/lib/reading-authoring-api';

interface ReadingPartTabsProps {
  activeTab: ReadingPartCode;
  onTabChange: (tab: ReadingPartCode) => void;
  counts?: { A: number; B: number; C: number };
  /** What the counts represent — drives denominator labels. Default: 'questions' */
  context?: 'questions' | 'texts';
}

const QUESTION_TARGETS: Record<ReadingPartCode, number> = { A: 20, B: 6, C: 16 };
const PART_LABELS: Record<ReadingPartCode, string> = { A: 'Part A', B: 'Part B', C: 'Part C' };
const CODES: ReadingPartCode[] = ['A', 'B', 'C'];

export function ReadingPartTabs({ activeTab, onTabChange, counts, context = 'questions' }: ReadingPartTabsProps) {
  return (
    <div role="tablist" aria-label="Reading paper parts" className="flex gap-1 border-b border-border pb-0">
      {CODES.map((code) => {
        const isActive = activeTab === code;
        const count = counts?.[code] ?? 0;
        const target = context === 'questions' ? QUESTION_TARGETS[code] : undefined;
        return (
          <button
            key={code}
            type="button"
            role="tab"
            aria-selected={isActive}
            aria-controls={`panel-part-${code}`}
            onClick={() => onTabChange(code)}
            className={cn(
              'px-4 py-2 text-sm font-medium border-b-2 transition-colors -mb-px',
              isActive
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground hover:border-muted-foreground/30',
            )}
          >
            {PART_LABELS[code]}
            <span className="ml-1.5 text-xs opacity-70">
              {target != null ? `(${count}/${target})` : `(${count})`}
            </span>
          </button>
        );
      })}
    </div>
  );
}
