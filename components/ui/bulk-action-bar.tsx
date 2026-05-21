'use client';

import { AnimatePresence, motion } from 'motion/react';
import { X } from 'lucide-react';
import { type ReactNode } from 'react';

import { cn } from '@/lib/utils';

export interface BulkAction {
  key: string;
  label: string;
  icon?: ReactNode;
  variant?: 'danger' | 'primary' | 'secondary';
  onClick: () => void;
}

interface BulkActionBarProps {
  selectedCount: number;
  actions: BulkAction[];
  onClearSelection: () => void;
  totalCount?: number;
  onSelectAll?: () => void;
  className?: string;
}

const variantStyles: Record<string, string> = {
  danger: 'bg-red-600 hover:bg-red-700 text-white',
  primary: 'bg-primary hover:bg-primary/90 text-white',
  secondary: 'bg-surface hover:bg-background-light text-navy border border-border',
};

export function BulkActionBar({
  selectedCount,
  actions,
  onClearSelection,
  totalCount,
  onSelectAll,
  className,
}: BulkActionBarProps) {
  return (
    <AnimatePresence>
      {selectedCount > 0 && (
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: 12 }}
          transition={{ duration: 0.2 }}
          className={cn(
            'sticky bottom-4 z-30 mx-auto flex w-fit items-center gap-3 rounded-2xl border border-border bg-surface px-4 py-2.5 shadow-lg',
            className,
          )}
        >
          <span className="text-sm font-medium text-navy">
            {selectedCount} selected
          </span>

          {totalCount && onSelectAll && selectedCount < totalCount && (
            <button
              type="button"
              onClick={onSelectAll}
              className="text-xs font-medium text-primary hover:underline"
            >
              Select all {totalCount}
            </button>
          )}

          <div className="h-5 w-px bg-border" />

          {actions.map((action) => (
            <button
              key={action.key}
              type="button"
              onClick={action.onClick}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold transition-colors',
                variantStyles[action.variant ?? 'secondary'],
              )}
            >
              {action.icon}
              {action.label}
            </button>
          ))}

          <button
            type="button"
            onClick={onClearSelection}
            className="rounded-lg p-1.5 text-muted hover:bg-background-light hover:text-navy transition-colors"
            aria-label="Clear selection"
          >
            <X className="h-4 w-4" />
          </button>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
