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
};

const SIZE_CLASSES: Record<BadgeSize, string> = {
  sm: 'px-2 py-0.5 text-[10px]',
  md: 'px-2.5 py-1 text-xs',
};

export function ExamTypeBadge({ examType, size = 'sm', className }: ExamTypeBadgeProps) {
  const normalized = (examType ?? '').toLowerCase().trim();
  const config = EXAM_CONFIG[normalized] ?? {
    label: 'OET',
    bg: 'bg-background-light dark:bg-gray-800',
    text: 'text-muted dark:text-muted/60',
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
