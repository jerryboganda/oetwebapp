'use client';

import React, { useEffect, useState } from 'react';
import { GitCompare, X, ChevronDown } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { Submission } from '@/lib/mock-data';
import { fetchSubmissionsPage } from '@/lib/api';

/**
 * Dual-slot compare picker at the top of `/submissions/compare`.
 *
 * Each slot holds one submission; slots are interchangeable (leftId /
 * rightId). When the user picks a sub-test filter, both slots reload the
 * candidate dropdown to attempts in that sub-test only. Cross-subtest
 * comparison is disallowed by contract — the UI prevents it at source.
 */
export interface CompareSelectorProps {
  leftId: string | undefined;
  rightId: string | undefined;
  onChange: (next: { leftId?: string; rightId?: string }) => void;
}

export function CompareSelector({ leftId, rightId, onChange }: CompareSelectorProps) {
  const [subtest, setSubtest] = useState<string>('');
  const [options, setOptions] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchSubmissionsPage({ limit: 50, subtest: subtest || undefined, sort: 'date-desc' })
      .then((page) => { if (!cancelled) setOptions(page.items); })
      .catch(() => { if (!cancelled) setOptions([]); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [subtest]);

  const leftOption = options.find((o) => o.id === leftId);
  const rightOption = options.find((o) => o.id === rightId);
  const subtestLock = leftOption?.subTest ?? rightOption?.subTest ?? '';

  // If a slot is locked to a sub-test, filter the other slot's options to match.
  const filtered = subtestLock
    ? options.filter((o) => o.subTest === subtestLock)
    : options;

  return (
    <section className="rounded-[24px] border border-gray-200 bg-surface p-5 shadow-sm">
      <header className="mb-4 flex items-center gap-2">
        <GitCompare className="w-5 h-5 text-primary" />
        <div>
          <p className="text-xs font-black uppercase tracking-widest text-muted">Compare picker</p>
          <h3 className="text-sm font-bold text-navy">Select two attempts in the same sub-test to compare</h3>
        </div>
      </header>

      <div className="flex items-center gap-2 flex-wrap mb-4">
        <span className="text-xs font-black uppercase tracking-widest text-muted">Sub-test:</span>
        {['', 'Writing', 'Speaking', 'Reading', 'Listening'].map((opt) => (
          <button
            key={opt || 'all'}
            type="button"
            disabled={Boolean(subtestLock) && opt !== subtestLock && opt !== ''}
            onClick={() => setSubtest(opt.toLowerCase())}
            className={cn(
              'px-3 py-1 rounded-full text-xs font-semibold border transition-colors',
              (subtest || '').toLowerCase() === opt.toLowerCase()
                ? 'bg-primary text-white border-primary'
                : 'bg-white text-navy border-gray-200 hover:border-primary/50 disabled:opacity-50 disabled:cursor-not-allowed',
            )}
          >
            {opt || 'All'}
          </button>
        ))}
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <Slot
          slotLabel="Baseline attempt"
          value={leftId}
          onClear={() => onChange({ leftId: undefined, rightId })}
          options={filtered}
          disabled={rightOption ? (o) => o.id === rightOption.id || o.subTest !== rightOption.subTest : undefined}
          loading={loading}
          onSelect={(id) => onChange({ leftId: id, rightId })}
        />
        <Slot
          slotLabel="Comparison attempt"
          value={rightId}
          onClear={() => onChange({ leftId, rightId: undefined })}
          options={filtered}
          disabled={leftOption ? (o) => o.id === leftOption.id || o.subTest !== leftOption.subTest : undefined}
          loading={loading}
          onSelect={(id) => onChange({ leftId, rightId: id })}
        />
      </div>
    </section>
  );
}

function Slot({
  slotLabel,
  value,
  options,
  disabled,
  loading,
  onSelect,
  onClear,
}: {
  slotLabel: string;
  value: string | undefined;
  options: Submission[];
  disabled?: (o: Submission) => boolean;
  loading: boolean;
  onSelect: (id: string) => void;
  onClear: () => void;
}) {
  const current = options.find((o) => o.id === value);
  return (
    <div className="rounded-2xl border border-gray-200 bg-background-light p-3">
      <div className="flex items-center justify-between mb-2">
        <span className="text-xs font-black uppercase tracking-widest text-muted">{slotLabel}</span>
        {value ? (
          <button type="button" onClick={onClear} className="text-xs text-muted hover:text-navy flex items-center gap-1">
            <X className="w-3 h-3" /> Clear
          </button>
        ) : null}
      </div>
      <div className="relative">
        <select
          value={value ?? ''}
          onChange={(e) => onSelect(e.target.value)}
          className="w-full py-2 pl-3 pr-8 rounded-xl border border-gray-200 bg-white text-sm appearance-none focus:outline-none focus:ring-2 focus:ring-primary/30"
          aria-label={slotLabel}
        >
          <option value="">{loading ? 'Loading attempts…' : 'Choose an attempt'}</option>
          {options.map((o) => (
            <option key={o.id} value={o.id} disabled={disabled?.(o) ?? false}>
              {o.subTest} · {formatShortDate(o.attemptDate)} · {o.taskName}
            </option>
          ))}
        </select>
        <ChevronDown className="w-4 h-4 absolute right-3 top-1/2 -translate-y-1/2 text-muted pointer-events-none" aria-hidden />
      </div>
      {current ? (
        <div className="mt-3 text-sm">
          <div className="font-bold text-navy">{current.taskName}</div>
          <div className="text-xs text-muted mt-1">
            {current.subTest} · {formatShortDate(current.attemptDate)} · Score {current.scoreEstimate}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function formatShortDate(value: string): string {
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric' }).format(d);
}
