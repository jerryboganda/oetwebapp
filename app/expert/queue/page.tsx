'use client';

import { useState, useEffect, useCallback, useMemo } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, Column } from '@/components/ui/data-table';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { ConfidenceBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { useRouter, useSearchParams } from 'next/navigation';
import { ReviewRequest } from '@/lib/types/expert';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import { fetchReviewQueue } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { RefreshCw, Search, Inbox } from 'lucide-react';

type AsyncStatus = 'loading' | 'error' | 'empty' | 'success';

export default function ReviewQueuePage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [data, setData] = useState<ReviewRequest[]>([]);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [lastRefreshed, setLastRefreshed] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [showStaleWarning, setShowStaleWarning] = useState(false);

  // URL-persisted filters
  const [selectedFilters, setSelectedFilters] = useState<Record<string, string[]>>(() => {
    const initial: Record<string, string[]> = {};
    ['type', 'profession', 'priority', 'status', 'confidence', 'assignment'].forEach(key => {
      const val = searchParams?.get(key);
      if (val) initial[key] = val.split(',');
    });
    return initial;
  });

  const filterGroups: FilterGroup[] = [
    { id: 'type', label: 'Sub-test', options: [{ id: 'writing', label: 'Writing' }, { id: 'speaking', label: 'Speaking' }] },
    { id: 'profession', label: 'Profession', options: [{ id: 'medicine', label: 'Medicine' }, { id: 'nursing', label: 'Nursing' }, { id: 'dentistry', label: 'Dentistry' }, { id: 'pharmacy', label: 'Pharmacy' }, { id: 'physiotherapy', label: 'Physiotherapy' }] },
    { id: 'priority', label: 'Priority', options: [{ id: 'high', label: 'High' }, { id: 'normal', label: 'Normal' }, { id: 'low', label: 'Low' }] },
    { id: 'status', label: 'Status', options: [{ id: 'queued', label: 'Queued' }, { id: 'assigned', label: 'Assigned' }, { id: 'overdue', label: 'Overdue' }, { id: 'in_progress', label: 'In Progress' }] },
    { id: 'confidence', label: 'AI Confidence', options: [{ id: 'high', label: 'High' }, { id: 'medium', label: 'Medium' }, { id: 'low', label: 'Low' }] },
    { id: 'assignment', label: 'Assignment', options: [{ id: 'assigned', label: 'Assigned' }, { id: 'unassigned', label: 'Unassigned' }] },
  ];

  const loadQueue = useCallback(async () => {
    try {
      setStatus('loading');
      setErrorMsg(null);
      const reviews = await fetchReviewQueue();
      setData(reviews);
      setStatus(reviews.length === 0 ? 'empty' : 'success');
      setLastRefreshed(new Date().toLocaleTimeString());
      setShowStaleWarning(false);
    } catch {
      setErrorMsg('Failed to load review queue. Please try again.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    const initialLoad = setTimeout(() => { void loadQueue(); }, 0);
    analytics.track('review_queue_viewed');
    // Auto-refresh queue every 2 minutes (§6.1.4)
    const interval = setInterval(() => { void loadQueue(); }, 2 * 60 * 1000);
    return () => {
      clearTimeout(initialLoad);
      clearInterval(interval);
    };
  }, [loadQueue]);

  // Stale data warning after 5 minutes without successful refresh
  useEffect(() => {
    const timer = setTimeout(() => setShowStaleWarning(true), 5 * 60 * 1000);
    return () => clearTimeout(timer);
  }, [lastRefreshed]);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await loadQueue();
    setIsRefreshing(false);
  };

  const handleFilterChange = (groupId: string, optionId: string) => {
    setSelectedFilters(prev => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId) ? current.filter(id => id !== optionId) : [...current, optionId];
      const next = { ...prev, [groupId]: updated };
      // Update URL
      const params = new URLSearchParams();
      Object.entries(next).forEach(([k, v]) => { if (v.length) params.set(k, v.join(',')); });
      window.history.replaceState(null, '', `?${params.toString()}`);
      return next;
    });
  };

  const clearFilters = () => {
    setSelectedFilters({});
    window.history.replaceState(null, '', window.location.pathname);
  };

  // Apply filters + search + default sort (SLA ascending, priority descending)
  const filteredData = useMemo(() => {
    let result = [...data];

    // Apply filters
    Object.entries(selectedFilters).forEach(([groupId, values]) => {
      if (!values.length) return;
      if (groupId === 'confidence') {
        result = result.filter(r => values.includes(r.aiConfidence));
      } else if (groupId === 'assignment') {
        result = result.filter(r => {
          if (values.includes('assigned') && values.includes('unassigned')) return true;
          if (values.includes('assigned')) return !!r.assignedReviewerId;
          return !r.assignedReviewerId;
        });
      } else {
        result = result.filter(r => values.includes(String(r[groupId as keyof ReviewRequest])));
      }
    });

    // Search by review ID or learner name
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      result = result.filter(r => r.id.toLowerCase().includes(q) || r.learnerName.toLowerCase().includes(q));
    }

    // Default sort: SLA ascending, then priority descending
    const priorityOrder: Record<string, number> = { high: 0, normal: 1, low: 2 };
    result.sort((a, b) => {
      const slaA = new Date(a.slaDue).getTime();
      const slaB = new Date(b.slaDue).getTime();
      if (slaA !== slaB) return slaA - slaB;
      return (priorityOrder[a.priority] ?? 1) - (priorityOrder[b.priority] ?? 1);
    });

    return result;
  }, [data, selectedFilters, searchQuery]);

  const columns: Column<ReviewRequest>[] = [
    { key: 'id', header: 'Review ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    { key: 'learner', header: 'Learner', render: (row) => (
      <button className="font-medium text-primary hover:underline text-left" onClick={(e) => { e.stopPropagation(); router.push(`/expert/learners/${row.learnerId}`); }} aria-label={`View profile for ${row.learnerName}`}>
        {row.learnerName}
      </button>
    )},
    { key: 'profession', header: 'Profession', render: (row) => <span className="capitalize">{row.profession.replace('_', ' ')}</span> },
    { key: 'type', header: 'Sub-test', render: (row) => <span className="capitalize">{row.type}</span> },
    { key: 'aiConfidence', header: 'AI Confidence', render: (row) => row.aiConfidence === 'unknown' ? <span className="text-muted text-xs">N/A</span> : <ConfidenceBadge level={row.aiConfidence} /> },
    { key: 'priority', header: 'Priority', render: (row) => <span className={row.priority === 'high' ? 'text-error font-semibold capitalize' : 'capitalize'}>{row.priority}</span> },
    { key: 'slaDue', header: 'SLA Due', render: (row) => {
      const date = new Date(row.slaDue);
      const now = Date.now();
      const isOverdue = date.getTime() < now;
      const isUrgent = !isOverdue && date.getTime() - now < 3600000;
      const formatted = `${date.toISOString().split('T')[0]} ${date.toISOString().split('T')[1].slice(0, 5)}`;
      return <span className={isOverdue ? 'text-error font-bold' : isUrgent ? 'text-amber-600 font-semibold' : ''}>{formatted} UTC</span>;
    }},
    { key: 'assignedReviewer', header: 'Assigned Reviewer', render: (row) => (
      <span className={row.assignedReviewerName ? 'text-navy' : 'text-muted italic'}>{row.assignedReviewerName ?? 'Unassigned'}</span>
    )},
    { key: 'status', header: 'Status', render: (row) => {
      const v = row.status === 'overdue' ? 'danger' : row.status === 'queued' ? 'muted' : row.status === 'assigned' ? 'info' : 'default';
      return <span className={`inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full ${v === 'danger' ? 'bg-red-100 text-red-800' : v === 'info' ? 'bg-blue-100 text-blue-800' : v === 'muted' ? 'bg-gray-100 text-gray-600' : 'bg-gray-100 text-gray-800'}`}>{row.status.replace('_', ' ')}</span>;
    }},
    { key: 'actions', header: 'Action', render: (row) => (
      <Button size="sm" onClick={() => router.push(`/expert/review/${row.type}/${row.id}`)} aria-label={`Review ${row.id}`}>Review</Button>
    )},
  ];

  return (
    <div className="max-w-7xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Review Queue">
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold text-navy tracking-tight">Review Queue</h1>
          <p className="text-sm text-muted mt-1">Manage and prioritise pending learner submissions.</p>
        </div>
        <div className="flex items-center gap-3">
          {lastRefreshed && <span className="text-xs text-muted">Last updated: {lastRefreshed}</span>}
          <Button variant="outline" onClick={handleRefresh} disabled={isRefreshing} className="bg-surface" aria-label="Refresh queue">
            <RefreshCw className={`w-4 h-4 mr-2 ${isRefreshing ? 'animate-spin' : ''}`} /> Refresh
          </Button>
        </div>
      </div>

      {showStaleWarning && status === 'success' && (
        <InlineAlert variant="warning" dismissible>Queue data may be outdated. Click Refresh to load the latest reviews.</InlineAlert>
      )}

      <div className="bg-surface p-4 rounded-xl border border-gray-200/60 shadow-sm space-y-3">
        <div className="relative max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted" />
          <input
            type="text"
            placeholder="Search by Review ID or Learner Name…"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary/30 bg-white"
            aria-label="Search reviews"
          />
        </div>
        <FilterBar groups={filterGroups} selected={selectedFilters} onChange={handleFilterChange} onClear={clearFilters} />
      </div>

      <AsyncStateWrapper
        status={status}
        onRetry={loadQueue}
        errorMessage={errorMsg ?? undefined}
        emptyContent={
          <EmptyState icon={<Inbox className="w-12 h-12 text-muted" />} title="No reviews in queue" description="New reviews will appear when learners request expert feedback." />
        }
      >
        <Card padding="none" className="overflow-hidden">
          <CardContent className="p-0">
            <DataTable
              data={filteredData}
              columns={columns}
              keyExtractor={(item) => item.id}
              emptyMessage="No reviews match the current filters."
              aria-label="Review queue table"
            />
          </CardContent>
        </Card>
      </AsyncStateWrapper>
    </div>
  );
}
