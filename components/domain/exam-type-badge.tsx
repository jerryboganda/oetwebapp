'use client';

import { cn } from '@/lib/utils';

type ExamType = 'oet' | 'ielts' | 'pte' | string;
type BadgeSize = 'sm' | 'md';

interface ExamTypeBadgeProps {
  examType: ExamType;
  size?: BadgeSize;
  className?: string;
}

const EXAM_CONFIG: Record<string, { label: string; bg: string; text: string; ring: string }> = {
  oet: {
    label: 'OET',
    bg: 'bg-violet-100 dark:bg-violet-900/40',
    text: 'text-violet-700 dark:text-violet-300',
    ring: 'ring-violet-200/60 dark:ring-violet-800/50',
  },
  ielts: {
    label: 'IELTS',
    bg: 'bg-blue-100 dark:bg-blue-900/40',
    text: 'text-blue-700 dark:text-blue-300',
    ring: 'ring-blue-200/60 dark:ring-blue-800/50',
  },
  pte: {
    label: 'PTE',
    bg: 'bg-purple-100 dark:bg-purple-900/40',
    text: 'text-purple-700 dark:text-purple-300',
    ring: 'ring-purple-200/60 dark:ring-purple-800/50',
  },
};

const SIZE_CLASSES: Record<BadgeSize, string> = {
  sm: 'px-2 py-0.5 text-[10px]',
  md: 'px-2.5 py-1 text-xs',
};

export function ExamTypeBadge({ examType, size = 'sm', className }: ExamTypeBadgeProps) {
  const normalized = (examType ?? '').toLowerCase().trim();
  const config = EXAM_CONFIG[normalized] ?? {
    label: examType?.toUpperCase() ?? 'EXAM',
    bg: 'bg-gray-100 dark:bg-gray-800',
    text: 'text-gray-600 dark:text-gray-400',
    ring: 'ring-gray-200/60 dark:ring-gray-700/50',
  };

  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full font-bold uppercase tracking-wider ring-1',
        config.bg,
        config.text,
        config.ring,
        SIZE_CLASSES[size],
        className,
      )}
    >
      {config.label}
    </span>
  );
}
