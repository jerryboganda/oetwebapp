'use client';

import { cn } from '@/lib/utils';
import { X, ChevronDown, Check } from 'lucide-react';
import * as Popover from '@radix-ui/react-popover';
import { MobileFilterSheet } from './mobile-filter-sheet';

export interface FilterOption {
  id: string;
  label: string;
  count?: number;
}

export interface FilterGroup {
  id: string;
  label: string;
  options: FilterOption[];
}

interface FilterBarProps {
  groups: FilterGroup[];
  selected: Record<string, string[]>;
  onChange: (groupId: string, optionId: string) => void;
  onClear?: () => void;
  className?: string;
}

export function FilterBar({ groups, selected, onChange, onClear, className }: FilterBarProps) {
  const totalSelected = Object.values(selected).reduce((sum, arr) => sum + arr.length, 0);
  return (
    <>
      <div className="md:hidden">
        <MobileFilterSheet groups={groups} selected={selected} onChange={onChange} onClear={onClear} className={className} />
      </div>

      <div
        className={cn(
          'hidden flex-wrap items-center gap-3 rounded-[20px] border border-gray-200 bg-background-light px-4 py-3 shadow-sm md:flex',
          className,
        )}
        role="toolbar"
        aria-label="Filters"
      >
        <div className="flex items-center gap-2">
          <span className="mr-2 text-sm font-semibold text-navy">Filters:</span>
          {groups.map((group) => {
            const selectedCount = selected[group.id]?.length || 0;
            const isActive = selectedCount > 0;

            return (
              <Popover.Root key={group.id}>
                <Popover.Trigger asChild>
                  <button
                    className={cn(
                      'flex items-center gap-2 rounded-xl border px-3.5 py-2 text-sm font-medium shadow-sm transition-all duration-200 active:scale-95',
                      isActive
                        ? 'border-primary/25 bg-primary/6 text-primary'
                        : 'border-gray-200 bg-surface text-navy hover:border-gray-300 hover:bg-white',
                    )}
                    aria-label={`Filter by ${group.label}${isActive ? `, ${selectedCount} selected` : ''}`}
                    aria-expanded={undefined}
                  >
                    {group.label}
                    {isActive && (
                      <span className="ml-1 inline-flex h-4 w-4 items-center justify-center rounded-full bg-primary text-[10px] text-white">
                        {selectedCount}
                      </span>
                    )}
                    <ChevronDown className="h-4 w-4 shrink-0 text-muted" />
                  </button>
                </Popover.Trigger>
                <Popover.Portal>
                  <Popover.Content
                    className="z-50 w-60 rounded-2xl border border-gray-200 bg-surface p-2 shadow-xl data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-[0.98] data-[state=open]:slide-in-from-top-1 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-[0.98] data-[state=closed]:slide-out-to-top-1 duration-200"
                    sideOffset={4}
                    align="start"
                  >
                    <div className="flex flex-col gap-1">
                      {group.options.map((option) => {
                        const isOptionSelected = selected[group.id]?.includes(option.id);
                        return (
                          <button
                            key={option.id}
                            onClick={() => onChange(group.id, option.id)}
                            role="checkbox"
                            aria-checked={isOptionSelected}
                            className={cn(
                              'flex w-full items-center justify-between rounded-xl px-3 py-2 text-sm transition-colors',
                              isOptionSelected ? 'bg-primary/6 font-semibold text-primary' : 'text-navy hover:bg-gray-50',
                            )}
                          >
                            <div className="flex items-center gap-2">
                              <div
                                className={cn(
                                  'flex h-4 w-4 items-center justify-center rounded border transition-colors',
                                  isOptionSelected ? 'border-primary bg-primary text-white' : 'border-gray-300',
                                )}
                              >
                                {isOptionSelected && <Check className="h-3 w-3" />}
                              </div>
                              {option.label}
                            </div>
                            {option.count !== undefined && <span className="text-xs text-muted">{option.count}</span>}
                          </button>
                        );
                      })}
                    </div>
                  </Popover.Content>
                </Popover.Portal>
              </Popover.Root>
            );
          })}
        </div>

        {totalSelected > 0 && onClear && <div className="mx-1 hidden h-6 w-px bg-gray-200 sm:block" />}

        {totalSelected > 0 && onClear && (
          <button
            type="button"
            onClick={onClear}
            className="flex items-center gap-1.5 rounded-xl px-3 py-2 text-sm font-medium text-muted transition-colors hover:bg-white hover:text-navy"
            aria-label={`Clear all ${totalSelected} filters`}
          >
            <X className="h-4 w-4" /> Clear filters
          </button>
        )}
      </div>
    </>
  );
}
