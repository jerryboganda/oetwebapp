'use client';

import { cn } from '@/lib/utils';
import { FilePenLine, Mic, BookOpen, Headphones } from 'lucide-react';
import { type ReactNode } from 'react';

export type SubTest = 'writing' | 'speaking' | 'reading' | 'listening';

const subtestConfig: Record<SubTest, { label: string; icon: ReactNode; color: string }> = {
  writing: { label: 'Writing', icon: <FilePenLine className="w-4 h-4" />, color: 'text-blue-600 bg-blue-50 border-blue-200' },
  speaking: { label: 'Speaking', icon: <Mic className="w-4 h-4" />, color: 'text-purple-600 bg-purple-50 border-purple-200' },
  reading: { label: 'Reading', icon: <BookOpen className="w-4 h-4" />, color: 'text-emerald-600 bg-emerald-50 border-emerald-200' },
  listening: { label: 'Listening', icon: <Headphones className="w-4 h-4" />, color: 'text-amber-600 bg-amber-50 border-amber-200' },
};

interface SubtestSwitcherProps {
  active: SubTest;
  onChange: (subtest: SubTest) => void;
  className?: string;
}

export function SubtestSwitcher({ active, onChange, className }: SubtestSwitcherProps) {
  return (
    <div className={cn('flex gap-2', className)} role="tablist">
      {(Object.entries(subtestConfig) as [SubTest, typeof subtestConfig[SubTest]][]).map(([key, config]) => (
        <button
          key={key}
          role="tab"
          aria-selected={active === key}
          onClick={() => onChange(key)}
          className={cn(
            'flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold border transition-colors',
            'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
            active === key ? config.color : 'text-muted bg-white border-gray-200 hover:border-gray-300',
          )}
        >
          {config.icon}
          {config.label}
        </button>
      ))}
    </div>
  );
}

export function SubtestIcon({ subtest, className }: { subtest: SubTest; className?: string }) {
  const config = subtestConfig[subtest];
  return <span className={cn('inline-flex items-center', className)}>{config.icon}</span>;
}

export function SubtestLabel({ subtest }: { subtest: SubTest }) {
  return <>{subtestConfig[subtest].label}</>;
}
