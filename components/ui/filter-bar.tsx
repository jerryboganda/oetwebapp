'use client';

import { cn } from '@/lib/utils';
import { X, ChevronDown, Check } from 'lucide-react';
import * as Popover from '@radix-ui/react-popover';

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
    <div className={cn('flex flex-wrap items-center gap-3', className)} role="toolbar" aria-label="Filters">
      <div className="flex items-center gap-2">
        <span className="text-sm font-semibold text-navy mr-2">Filters:</span>
        {groups.map((group) => {
          const selectedCount = selected[group.id]?.length || 0;
          const isActive = selectedCount > 0;

          return (
            <Popover.Root key={group.id}>
              <Popover.Trigger asChild>
                <button
                  className={cn(
                    'flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm font-medium border transition-all duration-200 active:scale-95 shadow-sm',
                    isActive
                      ? 'bg-primary/5 border-primary/30 text-primary'
                      : 'bg-surface text-navy border-gray-200 hover:border-gray-300 hover:bg-gray-50'
                  )}
                  aria-label={`Filter by ${group.label}${isActive ? `, ${selectedCount} selected` : ''}`}
                  aria-expanded={undefined}
                >
                  {group.label}
                  {isActive && (
                    <span className="inline-flex items-center justify-center bg-primary text-white text-[10px] w-4 h-4 rounded-full ml-1">
                      {selectedCount}
                    </span>
                  )}
                  <ChevronDown className="w-4 h-4 text-muted shrink-0" />
                </button>
              </Popover.Trigger>
              <Popover.Portal>
                <Popover.Content 
                  className="z-50 w-56 p-1 bg-surface border border-gray-200 rounded-xl shadow-lg animate-in fade-in zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out data-[state=closed]:zoom-out-95"
                  sideOffset={4}
                  align="start"
                >
                  <div className="flex flex-col">
                    {group.options.map((option) => {
                      const isOptionSelected = selected[group.id]?.includes(option.id);
                      return (
                        <button
                          key={option.id}
                          onClick={() => onChange(group.id, option.id)}
                          role="checkbox"
                          aria-checked={isOptionSelected}
                          className={cn(
                            'flex items-center justify-between w-full px-3 py-2 text-sm rounded-lg transition-colors',
                            isOptionSelected ? 'bg-primary/5 text-primary font-semibold' : 'text-navy hover:bg-gray-50'
                          )}
                        >
                          <div className="flex items-center gap-2">
                            <div className={cn(
                              "w-4 h-4 rounded border flex items-center justify-center transition-colors",
                              isOptionSelected ? "bg-primary border-primary text-white" : "border-gray-300"
                            )}>
                              {isOptionSelected && <Check className="w-3 h-3" />}
                            </div>
                            {option.label}
                          </div>
                          {option.count !== undefined && (
                            <span className="text-xs text-muted">
                              {option.count}
                            </span>
                          )}
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

      {totalSelected > 0 && onClear && (
        <div className="h-6 w-px bg-gray-200 mx-1 hidden sm:block" />
      )}

      {totalSelected > 0 && onClear && (
        <button
          type="button"
          onClick={onClear}
          className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-muted hover:text-navy transition-colors rounded-lg hover:bg-gray-100"
          aria-label={`Clear all ${totalSelected} filters`}
        >
          <X className="w-4 h-4" /> Clear filters
        </button>
      )}
    </div>
  );
}
