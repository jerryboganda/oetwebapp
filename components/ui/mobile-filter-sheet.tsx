'use client';

import { cn } from '@/lib/utils';
import { useState } from 'react';
import { Check, ChevronDown, SlidersHorizontal, X } from 'lucide-react';
import { Drawer } from './modal';
import type { FilterGroup } from './filter-bar';

interface MobileFilterSheetProps {
  groups: FilterGroup[];
  selected: Record<string, string[]>;
  onChange: (groupId: string, optionId: string) => void;
  onClear?: () => void;
  className?: string;
}

function FilterOptionButton({
  label,
  count,
  selected,
  onClick,
}: {
  label: string;
  count?: number;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      role="checkbox"
      aria-checked={selected}
      className={cn(
        'flex w-full items-center justify-between rounded-2xl px-4 py-3 text-sm transition-colors',
        selected ? 'bg-primary/6 text-primary font-semibold' : 'text-navy hover:bg-gray-50',
      )}
    >
      <span className="flex items-center gap-3 text-left">
        <span
          className={cn(
            'flex h-5 w-5 shrink-0 items-center justify-center rounded border transition-colors',
            selected ? 'border-primary bg-primary text-white' : 'border-gray-300 bg-white',
          )}
        >
          {selected ? <Check className="h-3.5 w-3.5" aria-hidden="true" /> : null}
        </span>
        <span>{label}</span>
      </span>
      {count !== undefined && <span className="text-xs text-muted">{count}</span>}
    </button>
  );
}

export function MobileFilterSheet({ groups, selected, onChange, onClear, className }: MobileFilterSheetProps) {
  const [open, setOpen] = useState(false);
  const totalSelected = Object.values(selected).reduce((sum, arr) => sum + arr.length, 0);

  return (
    <div
      className={cn(
        'flex items-center gap-3 rounded-[20px] border border-gray-200 bg-background-light px-4 py-3 shadow-sm',
        className,
      )}
      role="toolbar"
      aria-label="Filters"
    >
      <button
        type="button"
        onClick={() => setOpen(true)}
        className={cn(
          'pressable flex flex-1 items-center justify-between gap-3 rounded-2xl border px-4 py-3 text-sm font-semibold shadow-sm transition-colors',
          totalSelected > 0
            ? 'border-primary/25 bg-primary/6 text-primary'
            : 'border-gray-200 bg-surface text-navy hover:border-gray-300 hover:bg-white',
        )}
        aria-label={`Open filters${totalSelected > 0 ? `, ${totalSelected} selected` : ''}`}
      >
        <span className="flex items-center gap-2">
          <SlidersHorizontal className="h-4 w-4" aria-hidden="true" />
          Filters
        </span>
        <span className="flex items-center gap-2">
          {totalSelected > 0 && (
            <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-primary text-[10px] font-bold text-white">
              {totalSelected}
            </span>
          )}
          <ChevronDown className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
        </span>
      </button>

      {totalSelected > 0 && onClear && (
        <button
          type="button"
          onClick={onClear}
          className="pressable flex items-center gap-2 rounded-2xl px-3 py-3 text-sm font-semibold text-muted transition-colors hover:bg-white hover:text-navy"
          aria-label={`Clear all ${totalSelected} filters`}
        >
          <X className="h-4 w-4" aria-hidden="true" />
          Clear
        </button>
      )}

      <Drawer open={open} onClose={() => setOpen(false)} title="Filters" className="max-w-none sm:max-w-md">
        <div className="space-y-5">
          <div className="rounded-2xl border border-border/60 bg-background-light px-4 py-3 text-sm text-muted">
            Adjust the filter groups below to narrow the current list.
          </div>

          {groups.map((group) => {
            const selectedCount = selected[group.id]?.length || 0;

            return (
              <section key={group.id} className="space-y-3">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-bold text-navy">{group.label}</h3>
                  {selectedCount > 0 && (
                    <span className="rounded-full bg-primary/10 px-2.5 py-1 text-[11px] font-semibold text-primary">
                      {selectedCount} selected
                    </span>
                  )}
                </div>

                <div className="space-y-2">
                  {group.options.map((option) => {
                    const isSelected = selected[group.id]?.includes(option.id);
                    return (
                      <FilterOptionButton
                        key={option.id}
                        label={option.label}
                        count={option.count}
                        selected={Boolean(isSelected)}
                        onClick={() => onChange(group.id, option.id)}
                      />
                    );
                  })}
                </div>
              </section>
            );
          })}

          {totalSelected > 0 && onClear && (
            <button
              type="button"
              onClick={onClear}
              className="pressable flex w-full items-center justify-center gap-2 rounded-2xl border border-border/60 bg-surface px-4 py-3 text-sm font-semibold text-navy shadow-sm hover:bg-white"
            >
              <X className="h-4 w-4" aria-hidden="true" />
              Clear all filters
            </button>
          )}
        </div>
      </Drawer>
    </div>
  );
}