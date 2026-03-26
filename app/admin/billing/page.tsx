'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockBillingPlans, mockBillingInvoices, AdminBillingPlan, AdminBillingInvoice } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { DollarSign, Users, TrendingUp, CreditCard, Receipt } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function BillingPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeFilters, setActiveFilters] = useState<Record<string, string[]>>({ status: [] });
  const [pageStatus, setPageStatus] = useState<'loading' | 'success'>('loading');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus('success');
    };
    load();
  }, []);

  // Compute revenue KPIs from mock data
  const totalMRR = mockBillingPlans
    .filter(p => p.status === 'active')
    .reduce((sum, p) => sum + (p.price * p.activeSubscribers), 0);
  const totalSubscribers = mockBillingPlans.reduce((sum, p) => sum + p.activeSubscribers, 0);

  const planColumns: Column<AdminBillingPlan>[] = [
    {
      key: 'name',
      header: 'Plan Name',
      render: (item) => <div className="font-medium text-slate-900">{item.name}</div>,
    },
    {
      key: 'price',
      header: 'Pricing',
      render: (item) => <div className="text-slate-600">${item.price} / {item.interval}</div>,
    },
    {
      key: 'activeSubscribers',
      header: 'Active Subscribers',
      render: (item) => <div className="text-slate-600">{item.activeSubscribers.toLocaleString()}</div>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'active' ? 'success' : 'muted'}>
          {item.status.charAt(0).toUpperCase() + item.status.slice(1)}
        </Badge>
      ),
    },
  ];

  const invoiceColumns: Column<AdminBillingInvoice>[] = [
    {
      key: 'id',
      header: 'Invoice',
      render: (item) => <div className="font-medium text-slate-900 font-mono">{item.id}</div>,
    },
    {
      key: 'userName',
      header: 'User',
      render: (item) => (
        <div>
          <div className="text-sm text-slate-900">{item.userName}</div>
          <div className="text-xs text-slate-500">{item.plan}</div>
        </div>
      ),
    },
    {
      key: 'amount',
      header: 'Amount',
      render: (item) => <div className="text-slate-600 font-medium">${item.amount}</div>,
    },
    {
      key: 'date',
      header: 'Date',
      render: (item) => <div className="text-slate-600">{new Date(item.date).toLocaleDateString()}</div>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'paid' ? 'success' : item.status === 'failed' ? 'danger' : item.status === 'refunded' ? 'warning' : 'muted'}>
          {item.status.charAt(0).toUpperCase() + item.status.slice(1)}
        </Badge>
      ),
    },
  ];

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'legacy', label: 'Legacy' },
      ],
    },
  ];

  const handleFilterChange = (groupId: string, optionId: string) => {
    setActiveFilters((prev) => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId)
        ? current.filter((id) => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  const filteredPlans = mockBillingPlans.filter((item) => {
    if (activeFilters.status.length > 0) {
      return activeFilters.status.includes(item.status);
    }
    return true;
  });

  const handleCreatePlan = () => {
    try {
      analytics.track('admin_billing_action', { action: 'create_plan' });
      setToast({ variant: 'success', message: 'Plan creation flow initiated.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to initiate plan creation.' });
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="Billing & Plans">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Billing & Plans</h1>
          <p className="text-sm text-slate-500 mt-1">Manage subscription plans and revenue metrics.</p>
        </div>
        <Button onClick={handleCreatePlan}>Create Plan</Button>
      </div>

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {/* Revenue KPI Cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-green-50 text-green-600 flex items-center justify-center">
              <DollarSign className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">${totalMRR.toLocaleString()}</div>
              <div className="text-sm text-slate-500">Monthly Revenue</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-blue-50 text-blue-600 flex items-center justify-center">
              <Users className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{totalSubscribers.toLocaleString()}</div>
              <div className="text-sm text-slate-500">Total Subscribers</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-purple-50 text-purple-600 flex items-center justify-center">
              <TrendingUp className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{mockBillingPlans.filter(p => p.status === 'active').length}</div>
              <div className="text-sm text-slate-500">Active Plans</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-amber-50 text-amber-600 flex items-center justify-center">
              <CreditCard className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{mockBillingInvoices.filter(i => i.status === 'failed').length}</div>
              <div className="text-sm text-slate-500">Failed Payments</div>
            </div>
          </div>
        </div>

        {/* Plans Table */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200">
          <div className="px-6 py-4 border-b border-slate-200 bg-slate-50/50">
            <h3 className="font-medium text-slate-900">Subscription Plans</h3>
          </div>
          <FilterBar
            groups={filterGroups}
            selected={activeFilters}
            onChange={handleFilterChange}
            className="border-b border-slate-200 p-4"
          />
          {filteredPlans.length === 0 ? (
            <EmptyState
              icon={<CreditCard className="w-12 h-12 text-muted" />}
              title="No Matching Plans"
              description="Adjust your filters to see billing plans."
            />
          ) : (
            <DataTable columns={planColumns} data={filteredPlans} keyExtractor={(item) => item.id} />
          )}
        </div>

        {/* Invoices Table */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200">
          <div className="px-6 py-4 border-b border-slate-200 bg-slate-50/50 flex items-center gap-2">
            <Receipt className="w-4 h-4 text-slate-500" />
            <h3 className="font-medium text-slate-900">Recent Invoices</h3>
          </div>
          {mockBillingInvoices.length === 0 ? (
            <EmptyState
              icon={<Receipt className="w-12 h-12 text-muted" />}
              title="No Invoices"
              description="Invoices will appear here when payments are processed."
            />
          ) : (
            <DataTable columns={invoiceColumns} data={mockBillingInvoices} keyExtractor={(item) => item.id} />
          )}
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
