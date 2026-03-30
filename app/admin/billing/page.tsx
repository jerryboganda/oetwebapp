'use client';

import { type Dispatch, type SetStateAction, useEffect, useMemo, useState } from 'react';
import { CreditCard, DollarSign, Receipt, Search, Users } from 'lucide-react';
import { AdminMetricCard, AdminPageHeader, AdminSectionPanel } from '@/components/domain/admin-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { createAdminBillingPlan } from '@/lib/api';
import { getAdminBillingInvoiceData, getAdminBillingPlanData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminBillingInvoice, AdminBillingPlan } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface BillingPlanFormState {
  name: string;
  price: string;
  interval: string;
}

const defaultPlanForm: BillingPlanFormState = {
  name: '',
  price: '0',
  interval: 'month',
};

function formatCurrency(amount: number, currency = 'AUD') {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(amount);
}

export default function BillingPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [planFilters, setPlanFilters] = useState<Record<string, string[]>>({ status: [] });
  const [invoiceFilters, setInvoiceFilters] = useState<Record<string, string[]>>({ status: [] });
  const [invoiceSearch, setInvoiceSearch] = useState('');
  const [plans, setPlans] = useState<AdminBillingPlan[]>([]);
  const [invoices, setInvoices] = useState<AdminBillingInvoice[]>([]);
  const [isPlanModalOpen, setIsPlanModalOpen] = useState(false);
  const [planForm, setPlanForm] = useState<BillingPlanFormState>(defaultPlanForm);
  const [isSavingPlan, setIsSavingPlan] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedPlanStatus = planFilters.status?.[0];
  const selectedInvoiceStatus = invoiceFilters.status?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadBilling() {
      setPageStatus('loading');
      try {
        const [planItems, invoiceResult] = await Promise.all([
          getAdminBillingPlanData({ status: selectedPlanStatus }),
          getAdminBillingInvoiceData({
            status: selectedInvoiceStatus,
            search: invoiceSearch || undefined,
            pageSize: 100,
          }),
        ]);

        if (cancelled) return;

        setPlans(planItems);
        setInvoices(invoiceResult.items);
        setPageStatus(planItems.length > 0 || invoiceResult.items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load billing operations.' });
        }
      }
    }

    const handle = window.setTimeout(loadBilling, 200);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [selectedPlanStatus, selectedInvoiceStatus, invoiceSearch]);

  const metrics = useMemo(() => {
    const totalMRR = plans
      .filter((plan) => plan.status === 'active')
      .reduce((sum, plan) => sum + (plan.price * plan.activeSubscribers), 0);

    const totalSubscribers = plans.reduce((sum, plan) => sum + plan.activeSubscribers, 0);
    const failedInvoices = invoices.filter((invoice) => invoice.status === 'failed').length;
    const activePlans = plans.filter((plan) => plan.status === 'active').length;

    return { totalMRR, totalSubscribers, failedInvoices, activePlans };
  }, [plans, invoices]);

  const planColumns: Column<AdminBillingPlan>[] = [
    {
      key: 'name',
      header: 'Plan',
      render: (plan) => <span className="font-medium text-slate-900">{plan.name}</span>,
    },
    {
      key: 'price',
      header: 'Pricing',
      render: (plan) => <span className="text-slate-600">{formatCurrency(plan.price)} / {plan.interval}</span>,
    },
    {
      key: 'activeSubscribers',
      header: 'Subscribers',
      render: (plan) => <span className="text-slate-600">{plan.activeSubscribers.toLocaleString()}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (plan) => (
        <Badge variant={plan.status === 'active' ? 'success' : 'muted'}>
          {plan.status}
        </Badge>
      ),
    },
  ];

  const invoiceColumns: Column<AdminBillingInvoice>[] = [
    {
      key: 'id',
      header: 'Invoice',
      render: (invoice) => <span className="font-mono text-xs text-slate-600">{invoice.id}</span>,
    },
    {
      key: 'userName',
      header: 'User',
      render: (invoice) => (
        <div className="space-y-1">
          <p className="font-medium text-slate-900">{invoice.userName}</p>
          <p className="text-sm text-slate-500">{invoice.plan}</p>
        </div>
      ),
    },
    {
      key: 'amount',
      header: 'Amount',
      render: (invoice) => <span className="text-slate-600">{formatCurrency(invoice.amount, invoice.currency)}</span>,
    },
    {
      key: 'date',
      header: 'Issued',
      render: (invoice) => <span className="text-sm text-slate-500">{new Date(invoice.date).toLocaleString()}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (invoice) => (
        <Badge variant={invoice.status === 'paid' ? 'success' : invoice.status === 'failed' ? 'danger' : 'warning'}>
          {invoice.status}
        </Badge>
      ),
    },
  ];

  const planFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Plan Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'legacy', label: 'Legacy' },
      ],
    },
  ];

  const invoiceFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Invoice Status',
      options: [
        { id: 'paid', label: 'Paid' },
        { id: 'pending', label: 'Pending' },
        { id: 'failed', label: 'Failed' },
      ],
    },
  ];

  function handleSingleFilterChange(
    setter: Dispatch<SetStateAction<Record<string, string[]>>>,
    groupId: string,
    optionId: string,
  ) {
    setter((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  async function reloadBilling() {
    const [planItems, invoiceResult] = await Promise.all([
      getAdminBillingPlanData({ status: selectedPlanStatus }),
      getAdminBillingInvoiceData({
        status: selectedInvoiceStatus,
        search: invoiceSearch || undefined,
        pageSize: 100,
      }),
    ]);

    setPlans(planItems);
    setInvoices(invoiceResult.items);
    setPageStatus(planItems.length > 0 || invoiceResult.items.length > 0 ? 'success' : 'empty');
  }

  async function handleCreatePlan() {
    setIsSavingPlan(true);
    try {
      await createAdminBillingPlan({
        name: planForm.name,
        price: Number(planForm.price || 0),
        interval: planForm.interval,
      });

      await reloadBilling();
      setIsPlanModalOpen(false);
      setPlanForm(defaultPlanForm);
      setToast({ variant: 'success', message: 'Billing plan created successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to create this billing plan.' });
    } finally {
      setIsSavingPlan(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="max-w-7xl space-y-6">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminPageHeader
        title="Billing Operations"
        description="Manage subscription plans, invoice visibility, and revenue risk with live plan and invoice data."
        actions={
          <Button onClick={() => setIsPlanModalOpen(true)} className="gap-2">
            <CreditCard className="h-4 w-4" />
            Create Plan
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Receipt className="h-10 w-10 text-slate-400" />}
            title="No billing records found"
            description="Create a plan or wait for invoices to populate the admin billing workspace."
            action={{ label: 'Create Plan', onClick: () => setIsPlanModalOpen(true) }}
          />
        }
      >
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <AdminMetricCard label="Monthly Revenue" value={formatCurrency(metrics.totalMRR)} icon={<DollarSign className="h-5 w-5" />} />
          <AdminMetricCard label="Subscribers" value={metrics.totalSubscribers} icon={<Users className="h-5 w-5" />} />
          <AdminMetricCard label="Active Plans" value={metrics.activePlans} icon={<CreditCard className="h-5 w-5" />} />
          <AdminMetricCard label="Failed Invoices" value={metrics.failedInvoices} icon={<Receipt className="h-5 w-5" />} tone={metrics.failedInvoices > 0 ? 'danger' : 'default'} />
        </div>

        <AdminSectionPanel title="Subscription Plans" description="Live plan data from the admin billing plan endpoint.">
          <FilterBar groups={planFilterGroups} selected={planFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setPlanFilters, groupId, optionId)} onClear={() => setPlanFilters({ status: [] })} />
          <DataTable columns={planColumns} data={plans} keyExtractor={(plan) => plan.id} />
        </AdminSectionPanel>

        <AdminSectionPanel title="Invoices" description="Search and filter real invoice records by status and learner reference.">
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <Input placeholder="Search by user or plan description" value={invoiceSearch} onChange={(event) => setInvoiceSearch(event.target.value)} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={invoiceFilterGroups} selected={invoiceFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setInvoiceFilters, groupId, optionId)} onClear={() => { setInvoiceFilters({ status: [] }); setInvoiceSearch(''); }} />
          <DataTable columns={invoiceColumns} data={invoices} keyExtractor={(invoice) => invoice.id} />
        </AdminSectionPanel>
      </AsyncStateWrapper>

      <Modal open={isPlanModalOpen} onClose={() => setIsPlanModalOpen(false)} title="Create Billing Plan">
        <div className="space-y-4 py-2">
          <Input label="Plan Name" value={planForm.name} onChange={(event) => setPlanForm((current) => ({ ...current, name: event.target.value }))} />
          <Input label="Price" type="number" min={0} step="0.01" value={planForm.price} onChange={(event) => setPlanForm((current) => ({ ...current, price: event.target.value }))} />
          <Select
            label="Interval"
            value={planForm.interval}
            onChange={(event) => setPlanForm((current) => ({ ...current, interval: event.target.value }))}
            options={[
              { value: 'month', label: 'Monthly' },
              { value: 'year', label: 'Yearly' },
            ]}
          />

          <div className="flex justify-end gap-3 border-t border-slate-100 pt-4">
            <Button variant="outline" onClick={() => setIsPlanModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleCreatePlan} loading={isSavingPlan}>
              Save Plan
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
