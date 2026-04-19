'use client';

import React from 'react';
import { Search, Filter, X, ArrowDownNarrowWide, ArrowUpWideNarrow, Eye, EyeOff } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { SubmissionListQuery } from '@/lib/mock-data';

/**
 * Header control bar for the Submission History page.
 *
 * Renders search, sub-test tabs, context chips, review-status chips, a
 * date-range popover (native date inputs for portability), a sort select,
 * and include-hidden toggle. Emits a single `onChange` with a partial
 * query delta — the parent keeps full state and syncs it to the URL.
 */
export interface SubmissionFilterBarProps {
  query: SubmissionListQuery;
  facets?: {
    bySubtest: Record<string, number>;
    byContext: Record<string, number>;
    byReviewStatus: Record<string, number>;
  };
  total: number;
  onChange: (delta: Partial<SubmissionListQuery>) => void;
  onClear: () => void;
  onExportCsv?: () => void;
}

const SUBTEST_OPTIONS = [
  { value: '', label: 'All' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'reading', label: 'Reading' },
  { value: 'listening', label: 'Listening' },
];

const CONTEXT_OPTIONS = [
  { value: '', label: 'All' },
  { value: 'practice', label: 'Practice' },
  { value: 'mock', label: 'Mock' },
  { value: 'diagnostic', label: 'Diagnostic' },
  { value: 'revision', label: 'Revision' },
];

const REVIEW_STATUS_OPTIONS = [
  { value: '', label: 'Any' },
  { value: 'not_requested', label: 'Not requested' },
  { value: 'pending', label: 'Pending' },
  { value: 'reviewed', label: 'Reviewed' },
];

const SORT_OPTIONS = [
  { value: 'date-desc', label: 'Newest first' },
  { value: 'date-asc', label: 'Oldest first' },
  { value: 'score-desc', label: 'Highest score' },
  { value: 'score-asc', label: 'Lowest score' },
];

export function SubmissionFilterBar({
  query,
  facets,
  total,
  onChange,
  onClear,
  onExportCsv,
}: SubmissionFilterBarProps) {
  const hasFilters = Boolean(
    query.subtest || query.context || query.reviewStatus || query.from || query.to
    || query.passOnly || query.q || query.includeHidden,
  );

  return (
    <div className="rounded-[24px] border border-gray-200 bg-surface p-4 sm:p-5 shadow-sm space-y-4">
      {/* Search + Sort row */}
      <div className="flex flex-col md:flex-row gap-3 md:items-center">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted" aria-hidden />
          <input
            type="search"
            value={query.q ?? ''}
            placeholder="Search by task name…"
            onChange={(e) => onChange({ q: e.target.value || undefined })}
            className="w-full pl-10 pr-3 py-2.5 rounded-xl border border-gray-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-primary/30 focus:border-primary"
            aria-label="Search submissions"
          />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-xs font-bold text-muted uppercase tracking-widest" htmlFor="submissions-sort">Sort</label>
          <select
            id="submissions-sort"
            value={query.sort ?? 'date-desc'}
            onChange={(e) => onChange({ sort: e.target.value as SubmissionListQuery['sort'] })}
            className="py-2 px-3 rounded-xl border border-gray-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
          >
            {SORT_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
          {query.sort === 'date-desc' || query.sort === 'score-desc' ? (
            <ArrowDownNarrowWide className="w-4 h-4 text-muted hidden sm:block" aria-hidden />
          ) : (
            <ArrowUpWideNarrow className="w-4 h-4 text-muted hidden sm:block" aria-hidden />
          )}
        </div>
      </div>

      {/* Sub-test tabs */}
      <div className="flex items-center gap-2 flex-wrap" role="tablist" aria-label="Filter by sub-test">
        {SUBTEST_OPTIONS.map((opt) => {
          const isActive = (query.subtest ?? '') === opt.value;
          const count = opt.value ? facets?.bySubtest?.[opt.value] : undefined;
          return (
            <button
              key={opt.value || 'all'}
              type="button"
              role="tab"
              aria-selected={isActive}
              onClick={() => onChange({ subtest: opt.value || undefined })}
              className={cn(
                'px-3.5 py-1.5 rounded-full text-xs font-bold border transition-colors',
                isActive
                  ? 'bg-primary text-white border-primary'
                  : 'bg-white text-navy border-gray-200 hover:border-primary/50',
              )}
            >
              {opt.label}
              {count !== undefined ? <span className="ml-1.5 opacity-70">· {count}</span> : null}
            </button>
          );
        })}
      </div>

      {/* Chips: context + review-status + pass-only */}
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-xs font-black uppercase tracking-widest text-muted">Context:</span>
        {CONTEXT_OPTIONS.map((opt) => {
          const isActive = (query.context ?? '') === opt.value;
          const count = opt.value ? facets?.byContext?.[opt.value] : undefined;
          return (
            <button
              key={opt.value || 'ctx-all'}
              type="button"
              onClick={() => onChange({ context: opt.value || undefined })}
              className={cn(
                'px-3 py-1 rounded-full text-xs font-semibold border transition-colors',
                isActive
                  ? 'bg-navy text-white border-navy'
                  : 'bg-white text-navy border-gray-200 hover:border-navy/40',
              )}
            >
              {opt.label}{count !== undefined ? <span className="ml-1 opacity-70">· {count}</span> : null}
            </button>
          );
        })}
      </div>

      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-xs font-black uppercase tracking-widest text-muted">Review:</span>
        {REVIEW_STATUS_OPTIONS.map((opt) => {
          const isActive = (query.reviewStatus ?? '') === opt.value;
          const count = opt.value ? facets?.byReviewStatus?.[opt.value] : undefined;
          return (
            <button
              key={opt.value || 'rs-all'}
              type="button"
              onClick={() => onChange({ reviewStatus: opt.value || undefined })}
              className={cn(
                'px-3 py-1 rounded-full text-xs font-semibold border transition-colors',
                isActive
                  ? 'bg-navy text-white border-navy'
                  : 'bg-white text-navy border-gray-200 hover:border-navy/40',
              )}
            >
              {opt.label}{count !== undefined ? <span className="ml-1 opacity-70">· {count}</span> : null}
            </button>
          );
        })}
        <button
          type="button"
          onClick={() => onChange({ passOnly: !query.passOnly })}
          className={cn(
            'px-3 py-1 rounded-full text-xs font-semibold border transition-colors',
            query.passOnly
              ? 'bg-emerald-600 text-white border-emerald-600'
              : 'bg-white text-emerald-700 border-emerald-200 hover:border-emerald-400',
          )}
        >
          Pass only
        </button>
      </div>

      {/* Date range + hidden toggle + actions */}
      <div className="flex flex-col md:flex-row gap-3 md:items-center md:justify-between">
        <div className="flex items-center gap-3 flex-wrap text-sm">
          <label className="flex items-center gap-2">
            <span className="text-xs font-bold uppercase tracking-widest text-muted">From</span>
            <input
              type="date"
              value={query.from ? query.from.slice(0, 10) : ''}
              onChange={(e) => onChange({ from: e.target.value ? new Date(e.target.value).toISOString() : undefined })}
              className="px-2 py-1.5 rounded-lg border border-gray-200 bg-white text-sm"
              aria-label="Filter from date"
            />
          </label>
          <label className="flex items-center gap-2">
            <span className="text-xs font-bold uppercase tracking-widest text-muted">To</span>
            <input
              type="date"
              value={query.to ? query.to.slice(0, 10) : ''}
              onChange={(e) => onChange({ to: e.target.value ? new Date(e.target.value).toISOString() : undefined })}
              className="px-2 py-1.5 rounded-lg border border-gray-200 bg-white text-sm"
              aria-label="Filter to date"
            />
          </label>
          <button
            type="button"
            onClick={() => onChange({ includeHidden: !query.includeHidden })}
            className={cn(
              'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold border transition-colors',
              query.includeHidden
                ? 'bg-gray-900 text-white border-gray-900'
                : 'bg-white text-navy border-gray-200 hover:border-gray-400',
            )}
          >
            {query.includeHidden ? <Eye className="w-3.5 h-3.5" /> : <EyeOff className="w-3.5 h-3.5" />}
            {query.includeHidden ? 'Hidden shown' : 'Show hidden'}
          </button>
        </div>
        <div className="flex items-center gap-3 text-sm">
          <span className="text-muted">{total} result{total === 1 ? '' : 's'}</span>
          {hasFilters ? (
            <button type="button" onClick={onClear} className="inline-flex items-center gap-1 text-sm font-bold text-navy hover:underline">
              <X className="w-4 h-4" />
              Clear filters
            </button>
          ) : null}
          {onExportCsv ? (
            <button
              type="button"
              onClick={onExportCsv}
              className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-bold border border-gray-200 bg-white text-navy hover:border-primary/50 hover:text-primary transition-colors"
            >
              <Filter className="w-3.5 h-3.5" />
              Export CSV
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}
