'use client';

import { getSharedLayoutId, motionTokens } from '@/lib/motion';
import { cn } from '@/lib/utils';
import { motion, useReducedMotion } from 'motion/react';
import { type KeyboardEvent, type ReactNode, useRef } from 'react';

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
  const reducedMotion = useReducedMotion() ?? false;
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);

  const selectTab = (index: number) => {
    onChange(tabs[index].id);
    requestAnimationFrame(() => tabRefs.current[index]?.focus());
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      selectTab((index + 1) % tabs.length);
    }

    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      selectTab((index - 1 + tabs.length) % tabs.length);
    }

    if (event.key === 'Home') {
      event.preventDefault();
      selectTab(0);
    }

    if (event.key === 'End') {
      event.preventDefault();
      selectTab(tabs.length - 1);
    }
  };

  return (
    <div
      className={cn(
        'inline-flex w-full items-center gap-2 rounded-2xl border border-border bg-background-light p-2',
        scrollable ? 'flex-nowrap overflow-x-auto scrollbar-hide [-webkit-overflow-scrolling:touch]' : 'flex-wrap',
        className,
      )}
      role="tablist"
    >
      {tabs.map((tab, index) => (
        <button
          key={tab.id}
          ref={(node) => {
            tabRefs.current[index] = node;
          }}
          id={`tab-${tab.id}`}
          role="tab"
          aria-selected={activeTab === tab.id}
          aria-controls={`tabpanel-${tab.id}`}
          tabIndex={activeTab === tab.id ? 0 : -1}
          onClick={() => onChange(tab.id)}
          onKeyDown={(event) => handleKeyDown(event, index)}
          className={cn(
            'relative flex min-h-11 items-center gap-2 whitespace-nowrap rounded-2xl px-4 py-3 text-sm font-semibold transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
            activeTab === tab.id ? 'text-primary shadow-sm' : 'text-muted hover:bg-white hover:text-navy',
          )}
        >
          {activeTab === tab.id && (
            <motion.span
              layoutId={getSharedLayoutId('tabs-active-pill', 'default')}
              className="absolute inset-0 rounded-2xl bg-surface shadow-sm"
              transition={reducedMotion ? { duration: motionTokens.duration.instant } : motionTokens.spring.item}
            />
          )}
          <span className="relative z-10 flex items-center gap-2">
            {tab.icon}
            {tab.label}
            {tab.count !== undefined && (
              <span
                className={cn(
                  'rounded-full px-1.5 py-0.5 text-xs',
                  activeTab === tab.id ? 'bg-primary/10 text-primary' : 'bg-white text-muted',
                )}
              >
                {tab.count}
              </span>
            )}
          </span>
        </button>
      ))}
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
  const reducedMotion = useReducedMotion() ?? false;

  if (id !== activeTab) return null;

  return (
    <motion.div
      role="tabpanel"
      id={`tabpanel-${id}`}
      aria-labelledby={`tab-${id}`}
      className={className}
      initial={reducedMotion ? { opacity: 0 } : { opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={
        reducedMotion
          ? { duration: motionTokens.duration.instant }
          : {
              duration: motionTokens.duration.fast,
              ease: motionTokens.ease.entrance,
            }
      }
    >
      {children}
    </motion.div>
  );
}
