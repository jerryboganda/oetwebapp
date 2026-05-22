'use client';

import { X } from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import { type ReactNode } from 'react';

import { cn } from '@/lib/utils';

import { Button } from './button';

export type BulkAction = {
  key: string;
  label: string;
  icon?: ReactNode;
  variant?: 'danger' | 'primary' | 'secondary';
  disabled?: boolean;
  loading?: boolean;
  onClick: () => void;
};

interface BulkActionBarProps {
  selectedCount: number;
  actions: BulkAction[];
  onClearSelection: () => void;
  totalCount?: number;
  onSelectAll?: () => void;
  className?: string;
}

const variantMap: Record<NonNullable<BulkAction['variant']>, 'destructive' | 'primary' | 'secondary'> = {
  danger: 'destructive',
  primary: 'primary',
  secondary: 'secondary',
};

export function BulkActionBar({
  selectedCount,
  actions,
  onClearSelection,
  totalCount,
  onSelectAll,
  className,
}: BulkActionBarProps) {
  const canSelectAll = Boolean(onSelectAll && totalCount && selectedCount < totalCount);

  return (
    <AnimatePresence initial={false}>
      {selectedCount > 0 ? (
        <motion.div
          key="bulk-action-bar"
          data-testid="bulk-action-bar"
          initial={{ opacity: 0, y: 18, scale: 0.98 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: 18, scale: 0.98 }}
          transition={{ duration: 0.18, ease: 'easeOut' }}
          className={cn('sticky bottom-4 z-30 mt-4 px-2 sm:px-0', className)}
        >
          <div className="mx-auto flex max-w-4xl flex-col gap-3 rounded-xl border border-border bg-surface/95 p-3 shadow-xl shadow-navy/10 backdrop-blur sm:flex-row sm:items-center sm:justify-between">
            <div className="flex items-center gap-3">
              <span className="inline-flex h-8 min-w-8 items-center justify-center rounded-full bg-primary px-2 text-sm font-bold text-white">
                {selectedCount}
              </span>
              <div className="min-w-0">
                <p className="text-sm font-semibold text-navy">
                  {selectedCount === 1 ? '1 item selected' : `${selectedCount} items selected`}
                </p>
                {totalCount ? (
                  <p className="text-xs text-muted">
                    {selectedCount} of {totalCount} visible items
                  </p>
                ) : null}
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              {canSelectAll ? (
                <Button type="button" variant="ghost" size="sm" onClick={onSelectAll}>
                  Select all {totalCount}
                </Button>
              ) : null}
              {actions.map((action) => (
                <Button
                  key={action.key}
                  type="button"
                  size="sm"
                  variant={variantMap[action.variant ?? 'secondary']}
                  disabled={action.disabled}
                  loading={action.loading}
                  onClick={action.onClick}
                >
                  {action.icon}
                  {action.label}
                </Button>
              ))}
              <button
                type="button"
                onClick={onClearSelection}
                className="inline-flex h-9 w-9 items-center justify-center rounded-lg text-muted transition-colors hover:bg-background-light hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                aria-label="Clear selection"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          </div>
        </motion.div>
      ) : null}
    </AnimatePresence>
  );
}
