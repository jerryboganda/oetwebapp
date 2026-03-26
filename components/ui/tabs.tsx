'use client';

import { cn } from '@/lib/utils';
import { useState, type ReactNode } from 'react';

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
}

export function Tabs({ tabs, activeTab, onChange, className }: TabsProps) {
  return (
    <div className={cn('flex border-b border-gray-200 overflow-x-auto', className)} role="tablist">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          id={`tab-${tab.id}`}
          role="tab"
          aria-selected={activeTab === tab.id}
          aria-controls={`tabpanel-${tab.id}`}
          onClick={() => onChange(tab.id)}
          className={cn(
            'flex items-center gap-2 px-4 py-3 text-sm font-semibold whitespace-nowrap border-b-2 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
            activeTab === tab.id
              ? 'border-primary text-primary'
              : 'border-transparent text-muted hover:text-navy hover:border-gray-300',
          )}
        >
          {tab.icon}
          {tab.label}
          {tab.count !== undefined && (
            <span className={cn(
              'px-1.5 py-0.5 rounded-full text-xs',
              activeTab === tab.id ? 'bg-primary/10 text-primary' : 'bg-gray-100 text-muted',
            )}>
              {tab.count}
            </span>
          )}
        </button>
      ))}
    </div>
  );
}

/* ─── Tab Panel ─── */
export function TabPanel({ id, activeTab, children, className }: { id: string; activeTab: string; children: ReactNode; className?: string }) {
  if (id !== activeTab) return null;
  return (
    <div role="tabpanel" id={`tabpanel-${id}`} aria-labelledby={`tab-${id}`} className={className}>
      {children}
    </div>
  );
}
