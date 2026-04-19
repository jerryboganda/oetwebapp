'use client';

import type { ProgressRange } from '@/lib/api';

const OPTIONS: { value: ProgressRange; label: string }[] = [
  { value: '14d', label: '14d' },
  { value: '30d', label: '30d' },
  { value: '90d', label: '90d' },
  { value: 'all', label: 'All' },
];

/**
 * Time-range pill toggle. Used at the top of /progress to swap between
 * 14d / 30d / 90d / all-time aggregations.
 */
export function ProgressRangePills({ value, onChange }: { value: ProgressRange; onChange: (next: ProgressRange) => void }) {
  return (
    <div className="inline-flex items-center gap-1 rounded-xl border border-gray-200 bg-background-light p-1" role="radiogroup" aria-label="Time range">
      {OPTIONS.map((opt) => (
        <button
          key={opt.value}
          type="button"
          role="radio"
          aria-checked={value === opt.value}
          onClick={() => onChange(opt.value)}
          className={`px-3 py-1.5 text-xs font-bold rounded-lg transition-colors ${
            value === opt.value ? 'bg-white text-navy shadow-sm' : 'text-muted hover:text-navy'
          }`}
        >
          {opt.label}
        </button>
      ))}
    </div>
  );
}
