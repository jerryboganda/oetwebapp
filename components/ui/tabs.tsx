'use client';

import { cn } from '@/lib/utils';
import { type KeyboardEvent, type ReactNode } from 'react';

export interface Tab {
  id: string;
  label: string;
  icon?: ReactNode;
  count?: number;
}

interface TabsProps {
  tabs: Tab[];
  activeTab: string;
  onChange: (id: string) => void;
  className?: string;
  scrollable?: boolean;
}

export function Tabs({ tabs, activeTab, onChange, className, scrollable = true }: TabsProps) {
  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      onChange(tabs[(index + 1) % tabs.length].id);
    }

    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      onChange(tabs[(index - 1 + tabs.length) % tabs.length].id);
    }

    if (event.key === 'Home') {
      event.preventDefault();
      onChange(tabs[0].id);
    }

    if (event.key === 'End') {
      event.preventDefault();
      onChange(tabs[tabs.length - 1].id);
    }
  };

  return (
    <div
      className={cn(
        'inline-flex w-full items-center gap-2 rounded-[20px] border border-gray-200 bg-background-light p-2',
        scrollable ? 'flex-nowrap overflow-x-auto scrollbar-hide [-webkit-overflow-scrolling:touch]' : 'flex-wrap',
        className,
      )}
      role="tablist"
    >
      {tabs.map((tab, index) => {
        const isActive = activeTab === tab.id;
        return (
          <button
            key={tab.id}
            id={`tab-${tab.id}`}
            role="tab"
            aria-selected={isActive}
            aria-controls={`tabpanel-${tab.id}`}
            tabIndex={isActive ? 0 : -1}
            onClick={() => onChange(tab.id)}
            onKeyDown={(event) => handleKeyDown(event, index)}
            className={cn(
              'relative flex min-h-11 items-center gap-2 whitespace-nowrap rounded-2xl px-4 py-3 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
              'motion-safe:transition-all motion-safe:duration-200 motion-safe:ease-out',
              isActive ? 'bg-surface text-primary shadow-sm' : 'text-muted hover:bg-white hover:text-navy',
            )}
          >
            <span className="relative z-10 flex items-center gap-2">
              {tab.icon}
              {tab.label}
              {tab.count !== undefined && (
                <span
                  className={cn(
                    'rounded-full px-1.5 py-0.5 text-xs',
                    isActive ? 'bg-primary/10 text-primary' : 'bg-white text-muted',
                  )}
                >
                  {tab.count}
                </span>
              )}
            </span>
          </button>
        );
      })}
    </div>
  );
}

export function TabPanel({
  id,
  activeTab,
  children,
  className,
}: {
  id: string;
  activeTab: string;
  children: ReactNode;
  className?: string;
}) {
  if (id !== activeTab) return null;

  return (
    <div
      role="tabpanel"
      id={`tabpanel-${id}`}
      aria-labelledby={`tab-${id}`}
      className={className}
    >
      {children}
    </div>
  );
}
