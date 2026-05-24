'use client';

import { cn } from '@/lib/utils';
import type { ReadingPartCode } from '@/lib/reading-authoring-api';

interface ReadingPartTabsProps {
  activeTab: ReadingPartCode;
  onTabChange: (tab: ReadingPartCode) => void;
  counts?: { A: number; B: number; C: number };
}

const PARTS: Array<{ code: ReadingPartCode; label: string; target: string }> = [
  { code: 'A', label: 'Part A', target: '20 questions' },
  { code: 'B', label: 'Part B', target: '6 questions' },
  { code: 'C', label: 'Part C', target: '16 questions' },
];

export function ReadingPartTabs({ activeTab, onTabChange, counts }: ReadingPartTabsProps) {
  return (
    <div className="flex gap-1 border-b border-border pb-0">
      {PARTS.map(({ code, label, target }) => {
        const isActive = activeTab === code;
        const count = counts?.[code] ?? 0;
        return (
          <button
            key={code}
            type="button"
            onClick={() => onTabChange(code)}
            className={cn(
              'px-4 py-2 text-sm font-medium border-b-2 transition-colors -mb-px',
              isActive
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground hover:border-muted-foreground/30',
            )}
          >
            {label}
            <span className="ml-1.5 text-xs opacity-70">
              ({count}/{target.replace(' questions', '')})
            </span>
          </button>
        );
      })}
    </div>
  );
}
