'use client';

import { type Dispatch, type ReactNode, type SetStateAction, useEffect, useMemo, useRef, useState } from 'react';
import { CreditCard, DollarSign, FileSearch, History as HistoryIcon, Package, Receipt, Search, Ticket, Users } from 'lucide-react';
import { AdminRouteSummaryCard, AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Pagination } from '@/components/ui/pagination';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox, Input, Select, Textarea } from '@/components/ui/form-controls';
import { Drawer, Modal } from '@/components/ui/modal';
import { ContentScopePanel } from '@/components/domain/ContentScopePanel';
import {
  createAdminBillingAddOn,
  createAdminBillingCoupon,
  createAdminBillingPlan,
  updateAdminBillingAddOn,
  updateAdminBillingCoupon,
  updateAdminBillingPlan,
} from '@/lib/api';
import {
  getAdminBillingAddOnData,
  getAdminBillingAddOnVersionHistoryData,
  getAdminBillingCouponData,
  getAdminBillingCouponRedemptionData,
  getAdminBillingCouponVersionHistoryData,
  getAdminBillingInvoiceEvidenceData,
  getAdminBillingInvoiceData,
  getAdminBillingPaymentTransactionData,
  getAdminBillingPlanData,
  getAdminBillingPlanVersionHistoryData,
  getAdminBillingSubscriptionData,
} from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type {
  AdminBillingAddOn,
  AdminBillingCatalogVersionHistory,
  AdminBillingCoupon,
  AdminBillingCouponRedemption,
  AdminBillingInvoice,
  AdminBillingInvoiceEvidence,
  AdminBillingPaymentTransaction,
  AdminBillingPlan,
  AdminBillingSubscription,
} from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type CatalogHistoryKind = 'plan' | 'add_on' | 'coupon';
type CatalogHistoryTarget = { kind: CatalogHistoryKind; id: string; name: string; code: string };
type InvoiceEvidenceTarget = { id: string; userName: string; plan: string };
type CatalogVersionMetadata = {
  activeVersionNumber?: number | null;
  latestVersionNumber?: number | null;
  versionCount?: number;
};

interface BillingPlanFormState {
  code: string;
  name: string;
  description: string;
  price: string;
  currency: string;
  interval: string;
  durationMonths: string;
  includedCredits: string;
  displayOrder: string;
  isVisible: boolean;
  isRenewable: boolean;
  trialDays: string;
  status: string;
  includedSubtestsText: string;
  entitlementsJson: string;
}

interface BillingAddOnFormState {
  code: string;
  name: string;
  description: string;
  price: string;
  currency: string;
  interval: string;
  durationDays: string;
  grantCredits: string;
  displayOrder: string;
  isRecurring: boolean;
  appliesToAllPlans: boolean;
  isStackable: boolean;
  quantityStep: string;
  maxQuantity: string;
  status: string;
  compatiblePlanCodesText: string;
  grantEntitlementsJson: string;
}

interface BillingCouponFormState {
  code: string;
  name: string;
  description: string;
  discountType: 'percentage' | 'fixed';
  discountValue: string;
  currency: string;
  startsAt: string;
  endsAt: string;
  usageLimitTotal: string;
  usageLimitPerUser: string;
  minimumSubtotal: string;
  isStackable: boolean;
  status: string;
  applicablePlanCodesText: string;
  applicableAddOnCodesText: string;
  notes: string;
}

const defaultPlanForm: BillingPlanFormState = {
  code: '',
  name: '',
  price: '0',
  description: '',
  currency: 'AUD',
  interval: 'month',
  durationMonths: '1',
  includedCredits: '0',
  displayOrder: '0',
  isVisible: true,
  isRenewable: true,
  trialDays: '0',
  status: 'active',
  includedSubtestsText: 'writing, speaking',
  entitlementsJson: '{}',
};

const defaultAddOnForm: BillingAddOnFormState = {
  code: '',
  name: '',
  description: '',
  price: '0',
  currency: 'AUD',
  interval: 'one_time',
  durationDays: '0',
  grantCredits: '0',
  displayOrder: '0',
  isRecurring: false,
  appliesToAllPlans: true,
  isStackable: true,
  quantityStep: '1',
  maxQuantity: '',
  status: 'active',
  compatiblePlanCodesText: '',
  grantEntitlementsJson: '{}',
};

const defaultCouponForm: BillingCouponFormState = {
  code: '',
  name: '',
  description: '',
  discountType: 'percentage',
  discountValue: '10',
  currency: 'AUD',
  startsAt: '',
  endsAt: '',
  usageLimitTotal: '',
  usageLimitPerUser: '',
  minimumSubtotal: '',
  isStackable: true,
  status: 'active',
  applicablePlanCodesText: '',
  applicableAddOnCodesText: '',
  notes: '',
};

function formatCurrency(amount: number, currency = 'AUD') {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(amount);
}

function splitList(value: string): string[] {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
}

function toLocalDateTimeValue(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const offset = date.getTimezoneOffset();
  return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 16);
}

function fromLocalDateTimeValue(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function toOptionalNumber(value: string): number | null {
  if (!value.trim()) return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

function toNumber(value: string, fallback = 0): number {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function renderCatalogVersionMeta(item: CatalogVersionMetadata) {
  const versionCount = item.versionCount ?? 0;
  if (versionCount <= 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <Badge variant="info">v{item.activeVersionNumber ?? item.latestVersionNumber ?? versionCount}</Badge>
      <span className="text-xs text-muted">{versionCount} {versionCount === 1 ? 'version' : 'versions'}</span>
    </div>
  );
}

function formatSummaryValue(value: unknown): string {
  if (value == null || value === '') return 'N/A';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'number') return value.toLocaleString();
  if (Array.isArray(value)) return value.length > 0 ? value.join(', ') : 'None';
  if (typeof value === 'object') return `${Object.keys(value).length} fields`;
  return String(value);
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not recorded' : date.toLocaleString();
}

function evidenceGapLabel(value: string): string {
  const labels: Record<string, string> = {
    quote: 'Quote snapshot',
    payment: 'Payment transaction',
    couponRedemption: 'Coupon redemption',
    events: 'Event timeline',
    catalogAnchors: 'Catalog anchors',
  };
  return labels[value] ?? labelSummaryKey(value);
}

function paymentStatusVariant(status: string): 'success' | 'danger' | 'warning' | 'muted' | 'info' {
  if (status === 'completed') return 'success';
  if (status === 'failed' || status === 'disputed') return 'danger';
  if (status === 'pending') return 'warning';
  if (status === 'refunded') return 'info';
  return 'muted';
}

function paymentTypeLabel(value: string): string {
  const labels: Record<string, string> = {
    subscription_payment: 'Subscription payment',
    one_time_purchase: 'One-time purchase',
    wallet_top_up: 'Wallet top-up',
    refund: 'Refund',
  };
  return labels[value] ?? labelSummaryKey(value);
}

function paymentProductLabel(payment: AdminBillingPaymentTransaction): string {
  const productType = payment.productType ? labelSummaryKey(payment.productType) : 'Not recorded';
  return payment.productId ? `${productType}: ${payment.productId}` : productType;
}

function EvidenceSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-3 border-t border-border pt-4">
      <h3 className="text-sm font-semibold text-navy">{title}</h3>
      {children}
    </section>
  );
}

function EvidenceField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0 rounded-lg bg-background-light px-3 py-2">
      <dt className="text-[11px] uppercase tracking-[0.12em] text-muted">{label}</dt>
      <dd className="mt-1 break-words text-sm font-medium text-navy">{children || 'Not recorded'}</dd>
    </div>
  );
}

function labelSummaryKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .replace(/^./, (char) => char.toUpperCase());
}

function jsonList(value: string): string {
  return JSON.stringify(splitList(value));
}

function safeJsonObject(value: string, fallback: string = '{}'): string {
  const trimmed = value.trim();
  if (!trimmed) {
    return fallback;
  }

  const parsed = JSON.parse(trimmed);
  return JSON.stringify(parsed, null, 2);
}

function normalizedCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64);
}

function toPlanForm(plan: AdminBillingPlan): BillingPlanFormState {
  return {
    code: plan.code ?? plan.id,
    name: plan.name,
    description: plan.description ?? '',
    price: String(plan.price),
    currency: plan.currency ?? 'AUD',
    interval: plan.interval,
    durationMonths: String(plan.durationMonths ?? 1),
    includedCredits: String(plan.includedCredits ?? 0),
    displayOrder: String(plan.displayOrder ?? 0),
    isVisible: plan.isVisible ?? true,
    isRenewable: plan.isRenewable ?? true,
    trialDays: String(plan.trialDays ?? 0),
    status: plan.status,
    includedSubtestsText: (plan.includedSubtests ?? []).join(', '),
    entitlementsJson: JSON.stringify(plan.entitlements ?? {}, null, 2),
  };
}

function toAddOnForm(addOn: AdminBillingAddOn): BillingAddOnFormState {
  return {
    code: addOn.code,
    name: addOn.name,
    description: addOn.description ?? '',
    price: String(addOn.price),
    currency: addOn.currency ?? 'AUD',
    interval: addOn.interval,
    durationDays: String(addOn.durationDays ?? 0),
    grantCredits: String(addOn.grantCredits ?? 0),
    displayOrder: String(addOn.displayOrder ?? 0),
    isRecurring: addOn.isRecurring,
    appliesToAllPlans: addOn.appliesToAllPlans,
    isStackable: addOn.isStackable,
    quantityStep: String(addOn.quantityStep ?? 1),
    maxQuantity: addOn.maxQuantity == null ? '' : String(addOn.maxQuantity),
    status: addOn.status,
    compatiblePlanCodesText: (addOn.compatiblePlanCodes ?? []).join(', '),
    grantEntitlementsJson: JSON.stringify(addOn.grantEntitlements ?? {}, null, 2),
  };
}

function toCouponForm(coupon: AdminBillingCoupon): BillingCouponFormState {
  return {
    code: coupon.code,
    name: coupon.name,
    description: coupon.description ?? '',
    discountType: coupon.discountType,
    discountValue: String(coupon.discountValue ?? 0),
    currency: coupon.currency ?? 'AUD',
    startsAt: toLocalDateTimeValue(coupon.startsAt),
    endsAt: toLocalDateTimeValue(coupon.endsAt),
    usageLimitTotal: coupon.usageLimitTotal == null ? '' : String(coupon.usageLimitTotal),
    usageLimitPerUser: coupon.usageLimitPerUser == null ? '' : String(coupon.usageLimitPerUser),
    minimumSubtotal: coupon.minimumSubtotal == null ? '' : String(coupon.minimumSubtotal),
    isStackable: coupon.isStackable,
    status: coupon.status,
    applicablePlanCodesText: (coupon.applicablePlanCodes ?? []).join(', '),
    applicableAddOnCodesText: (coupon.applicableAddOnCodes ?? []).join(', '),
    notes: coupon.notes ?? '',
  };
}

export default function BillingPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [planFilters, setPlanFilters] = useState<Record<string, string[]>>({ status: [] });
  const [addOnFilters, setAddOnFilters] = useState<Record<string, string[]>>({ status: [] });
  const [couponFilters, setCouponFilters] = useState<Record<string, string[]>>({ status: [] });
  const [subscriptionFilters, setSubscriptionFilters] = useState<Record<string, string[]>>({ status: [] });
  const [redemptionFilters, setRedemptionFilters] = useState<Record<string, string[]>>({ status: [] });
  const [invoiceFilters, setInvoiceFilters] = useState<Record<string, string[]>>({ status: [] });
  const [paymentFilters, setPaymentFilters] = useState<Record<string, string[]>>({ status: [], gateway: [], transactionType: [] });
  const [subscriptionSearch, setSubscriptionSearch] = useState('');
  const [redemptionSearch, setRedemptionSearch] = useState('');
  const [invoiceSearch, setInvoiceSearch] = useState('');
  const [paymentSearch, setPaymentSearch] = useState('');
  const [paymentPage, setPaymentPage] = useState(1);
  const [paymentPageSize, setPaymentPageSize] = useState(50);
  const [paymentTotal, setPaymentTotal] = useState(0);
  const [plans, setPlans] = useState<AdminBillingPlan[]>([]);
  const [addOns, setAddOns] = useState<AdminBillingAddOn[]>([]);
  const [coupons, setCoupons] = useState<AdminBillingCoupon[]>([]);
  const [subscriptions, setSubscriptions] = useState<AdminBillingSubscription[]>([]);
  const [redemptions, setRedemptions] = useState<AdminBillingCouponRedemption[]>([]);
  const [invoices, setInvoices] = useState<AdminBillingInvoice[]>([]);
  const [paymentTransactions, setPaymentTransactions] = useState<AdminBillingPaymentTransaction[]>([]);
  const [isPlanModalOpen, setIsPlanModalOpen] = useState(false);
  const [isAddOnModalOpen, setIsAddOnModalOpen] = useState(false);
  const [isCouponModalOpen, setIsCouponModalOpen] = useState(false);
  const [planForm, setPlanForm] = useState<BillingPlanFormState>(defaultPlanForm);
  const [addOnForm, setAddOnForm] = useState<BillingAddOnFormState>(defaultAddOnForm);
  const [couponForm, setCouponForm] = useState<BillingCouponFormState>(defaultCouponForm);
  const [isSavingPlan, setIsSavingPlan] = useState(false);
  const [isSavingAddOn, setIsSavingAddOn] = useState(false);
  const [isSavingCoupon, setIsSavingCoupon] = useState(false);
  const [editingPlanId, setEditingPlanId] = useState<string | null>(null);
  const [editingAddOnId, setEditingAddOnId] = useState<string | null>(null);
  const [editingCouponId, setEditingCouponId] = useState<string | null>(null);
  const [catalogHistoryTarget, setCatalogHistoryTarget] = useState<CatalogHistoryTarget | null>(null);
  const [catalogHistory, setCatalogHistory] = useState<AdminBillingCatalogVersionHistory | null>(null);
  const [catalogHistoryStatus, setCatalogHistoryStatus] = useState<PageStatus>('empty');
  const catalogHistoryRequestRef = useRef(0);
  const [invoiceEvidenceTarget, setInvoiceEvidenceTarget] = useState<InvoiceEvidenceTarget | null>(null);
  const [invoiceEvidence, setInvoiceEvidence] = useState<AdminBillingInvoiceEvidence | null>(null);
  const [invoiceEvidenceStatus, setInvoiceEvidenceStatus] = useState<PageStatus>('empty');
  const invoiceEvidenceRequestRef = useRef(0);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedPlanStatus = planFilters.status?.[0];
  const selectedAddOnStatus = addOnFilters.status?.[0];
  const selectedCouponStatus = couponFilters.status?.[0];
  const selectedSubscriptionStatus = subscriptionFilters.status?.[0];
  const selectedRedemptionStatus = redemptionFilters.status?.[0];
  const selectedInvoiceStatus = invoiceFilters.status?.[0];
  const selectedPaymentStatus = paymentFilters.status?.[0];
  const selectedPaymentGateway = paymentFilters.gateway?.[0];
  const selectedPaymentTransactionType = paymentFilters.transactionType?.[0];

  useEffect(() => {
    setPaymentPage(1);
  }, [selectedPaymentStatus, selectedPaymentGateway, selectedPaymentTransactionType, paymentSearch]);

  useEffect(() => {
    let cancelled = false;

    async function loadBilling() {
      setPageStatus('loading');
      try {
        const [planItems, addOnItems, couponItems, subscriptionResult, redemptionResult, invoiceResult, paymentResult] = await Promise.all([
          getAdminBillingPlanData({ status: selectedPlanStatus }),
          getAdminBillingAddOnData({ status: selectedAddOnStatus }),
          getAdminBillingCouponData({ status: selectedCouponStatus }),
          getAdminBillingSubscriptionData({ status: selectedSubscriptionStatus, search: subscriptionSearch || undefined, pageSize: 100 }),
          getAdminBillingCouponRedemptionData({ couponCode: redemptionSearch || undefined, pageSize: 100 }),
          getAdminBillingInvoiceData({
            status: selectedInvoiceStatus,
            search: invoiceSearch || undefined,
            pageSize: 100,
          }),
          getAdminBillingPaymentTransactionData({
            status: selectedPaymentStatus,
            gateway: selectedPaymentGateway,
            transactionType: selectedPaymentTransactionType,
            search: paymentSearch || undefined,
            page: paymentPage,
            pageSize: paymentPageSize,
          }),
        ]);

        if (cancelled) return;

        setPlans(planItems);
        setAddOns(addOnItems);
        setCoupons(couponItems);
        const redemptionItems = selectedRedemptionStatus ? redemptionResult.items.filter((item) => item.status === selectedRedemptionStatus) : redemptionResult.items;

        setSubscriptions(subscriptionResult.items);
        setRedemptions(redemptionItems);
        setInvoices(invoiceResult.items);
        setPaymentTransactions(paymentResult.items);
        setPaymentTotal(paymentResult.total);
        setPageStatus(
          planItems.length > 0 ||
          addOnItems.length > 0 ||
          couponItems.length > 0 ||
          subscriptionResult.items.length > 0 ||
          redemptionItems.length > 0 ||
          invoiceResult.items.length > 0 ||
          paymentResult.items.length > 0
            ? 'success'
            : 'empty',
        );
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
  }, [selectedPlanStatus, selectedAddOnStatus, selectedCouponStatus, selectedSubscriptionStatus, selectedRedemptionStatus, subscriptionSearch, redemptionSearch, selectedInvoiceStatus, invoiceSearch, selectedPaymentStatus, selectedPaymentGateway, selectedPaymentTransactionType, paymentSearch, paymentPage, paymentPageSize]);

  const metrics = useMemo(() => {
    const totalMRR = plans
      .filter((plan) => plan.status === 'active')
      .reduce((sum, plan) => sum + (plan.price * plan.activeSubscribers), 0);

    const totalSubscribers = plans.reduce((sum, plan) => sum + plan.activeSubscribers, 0);
    const failedInvoices = invoices.filter((invoice) => invoice.status === 'failed').length;
    const activePlans = plans.filter((plan) => plan.status === 'active').length;
    const activeAddOns = addOns.filter((addOn) => addOn.status === 'active').length;
    const activeCoupons = coupons.filter((coupon) => coupon.status === 'active').length;
    const activeSubscriptions = subscriptions.filter((subscription) => subscription.status === 'active' || subscription.status === 'trial').length;

    return { totalMRR, totalSubscribers, failedInvoices, activePlans, activeAddOns, activeCoupons, activeSubscriptions };
  }, [plans, addOns, coupons, subscriptions, invoices]);

  const planColumns: Column<AdminBillingPlan>[] = [
    {
      key: 'name',
      header: 'Plan',
      render: (plan) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{plan.name}</p>
          <p className="text-xs uppercase tracking-[0.12em] text-muted">{plan.code ?? plan.id}</p>
          {renderCatalogVersionMeta(plan)}
          {plan.description ? <p className="text-sm text-muted">{plan.description}</p> : null}
        </div>
      ),
    },
    {
      key: 'price',
      header: 'Pricing',
      render: (plan) => (
        <div className="space-y-1 text-muted">
          <p>{formatCurrency(plan.price, plan.currency)} / {plan.interval}</p>
          <p className="text-xs">{plan.includedCredits ?? 0} included credits</p>
        </div>
      ),
    },
    {
      key: 'activeSubscribers',
      header: 'Subscribers',
      render: (plan) => <span className="text-muted">{plan.activeSubscribers.toLocaleString()}</span>,
    },
    {
      key: 'visibility',
      header: 'Visibility',
      render: (plan) => <Badge variant={plan.isVisible ? 'success' : 'muted'}>{plan.isVisible ? 'Visible' : 'Hidden'}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (plan) => (
        <Badge variant={plan.status === 'active' ? 'success' : plan.status === 'archived' ? 'danger' : 'muted'}>
          {plan.status}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (plan) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" className="gap-2" aria-label={`View version history for ${plan.name}`} onClick={() => openCatalogHistory({ kind: 'plan', id: plan.id, name: plan.name, code: plan.code ?? plan.id })}>
            <HistoryIcon className="h-4 w-4" />
            History
          </Button>
          <Button variant="outline" size="sm" onClick={() => openPlanEditor(plan)}>
            Edit
          </Button>
        </div>
      ),
    },
  ];

  const addOnColumns: Column<AdminBillingAddOn>[] = [
    {
      key: 'name',
      header: 'Add-on',
      render: (addOn) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{addOn.name}</p>
          <p className="text-xs uppercase tracking-[0.12em] text-muted">{addOn.code}</p>
          {renderCatalogVersionMeta(addOn)}
          {addOn.description ? <p className="text-sm text-muted">{addOn.description}</p> : null}
        </div>
      ),
    },
    {
      key: 'price',
      header: 'Pricing',
      render: (addOn) => (
        <div className="space-y-1 text-muted">
          <p>{formatCurrency(addOn.price, addOn.currency)} / {addOn.interval}</p>
          <p className="text-xs">{addOn.grantCredits} credits - qty step {addOn.quantityStep}</p>
        </div>
      ),
    },
    {
      key: 'scope',
      header: 'Scope',
      render: (addOn) => (
        <div className="space-y-1 text-sm text-muted">
          <p>{addOn.appliesToAllPlans ? 'All plans' : addOn.compatiblePlanCodes.join(', ') || 'Restricted'}</p>
          <p>{addOn.isRecurring ? 'Recurring' : 'One-time'}</p>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (addOn) => (
        <Badge variant={addOn.status === 'active' ? 'success' : addOn.status === 'archived' ? 'danger' : 'muted'}>
          {addOn.status}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (addOn) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" className="gap-2" aria-label={`View version history for ${addOn.name}`} onClick={() => openCatalogHistory({ kind: 'add_on', id: addOn.id, name: addOn.name, code: addOn.code })}>
            <HistoryIcon className="h-4 w-4" />
            History
          </Button>
          <Button variant="outline" size="sm" onClick={() => openAddOnEditor(addOn)}>
            Edit
          </Button>
        </div>
      ),
    },
  ];

  const couponColumns: Column<AdminBillingCoupon>[] = [
    {
      key: 'code',
      header: 'Coupon',
      render: (coupon) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{coupon.code}</p>
          <p className="text-sm text-muted">{coupon.name}</p>
          {renderCatalogVersionMeta(coupon)}
        </div>
      ),
    },
    {
      key: 'discount',
      header: 'Discount',
      render: (coupon) => (
        <div className="space-y-1 text-muted">
          <p>{coupon.discountType === 'percentage' ? `${coupon.discountValue}%` : formatCurrency(coupon.discountValue, coupon.currency)}</p>
          <p className="text-xs">Min subtotal {coupon.minimumSubtotal == null ? 'none' : formatCurrency(coupon.minimumSubtotal, coupon.currency)}</p>
        </div>
      ),
    },
    {
      key: 'usage',
      header: 'Usage',
      render: (coupon) => (
        <div className="space-y-1 text-muted">
          <p>{coupon.redemptionCount} redemptions</p>
          <p className="text-xs">{coupon.usageLimitTotal == null ? 'Unlimited' : `${coupon.usageLimitTotal} total`} - {coupon.usageLimitPerUser == null ? 'No per-user limit' : `${coupon.usageLimitPerUser} per user`}</p>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (coupon) => (
        <Badge variant={coupon.status === 'active' ? 'success' : coupon.status === 'archived' ? 'danger' : 'muted'}>
          {coupon.status}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (coupon) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" className="gap-2" aria-label={`View version history for ${coupon.code}`} onClick={() => openCatalogHistory({ kind: 'coupon', id: coupon.id, name: coupon.name, code: coupon.code })}>
            <HistoryIcon className="h-4 w-4" />
            History
          </Button>
          <Button variant="outline" size="sm" onClick={() => openCouponEditor(coupon)}>
            Edit
          </Button>
        </div>
      ),
    },
  ];

  const subscriptionColumns: Column<AdminBillingSubscription>[] = [
    {
      key: 'user',
      header: 'User',
      render: (subscription) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{subscription.userName}</p>
          <p className="text-xs uppercase tracking-[0.12em] text-muted">{subscription.userId}</p>
        </div>
      ),
    },
    {
      key: 'plan',
      header: 'Plan',
      render: (subscription) => (
        <div className="space-y-1 text-muted">
          <p>{subscription.planName}</p>
          <p className="text-xs uppercase tracking-[0.12em] text-muted">{subscription.planId}</p>
        </div>
      ),
    },
    {
      key: 'price',
      header: 'Billing',
      render: (subscription) => (
        <div className="space-y-1 text-muted">
          <p>{formatCurrency(subscription.price, subscription.currency)} / {subscription.interval}</p>
          <p className="text-xs">{subscription.addOnCount} add-ons</p>
        </div>
      ),
    },
    {
      key: 'nextRenewalAt',
      header: 'Renewal',
      render: (subscription) => <span className="text-sm text-muted">{subscription.nextRenewalAt ? new Date(subscription.nextRenewalAt).toLocaleString() : 'N/A'}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (subscription) => <Badge variant={subscription.status === 'active' ? 'success' : subscription.status === 'trial' ? 'info' : 'muted'}>{subscription.status}</Badge>,
    },
  ];

  const redemptionColumns: Column<AdminBillingCouponRedemption>[] = [
    {
      key: 'couponCode',
      header: 'Coupon',
      render: (redemption) => <span className="font-medium text-navy">{redemption.couponCode}</span>,
    },
    {
      key: 'userId',
      header: 'User',
      render: (redemption) => <span className="text-muted">{redemption.userId}</span>,
    },
    {
      key: 'discountAmount',
      header: 'Discount',
      render: (redemption) => <span className="text-muted">{formatCurrency(redemption.discountAmount, redemption.currency)}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (redemption) => <Badge variant={redemption.status === 'applied' ? 'success' : redemption.status === 'rejected' ? 'danger' : 'muted'}>{redemption.status}</Badge>,
    },
    {
      key: 'redeemedAt',
      header: 'Redeemed',
      render: (redemption) => <span className="text-sm text-muted">{new Date(redemption.redeemedAt).toLocaleString()}</span>,
    },
  ];

  const invoiceColumns: Column<AdminBillingInvoice>[] = [
    {
      key: 'id',
      header: 'Invoice',
      render: (invoice) => <span className="font-mono text-xs text-muted">{invoice.id}</span>,
    },
    {
      key: 'userName',
      header: 'User',
      render: (invoice) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{invoice.userName}</p>
          <p className="text-sm text-muted">{invoice.plan}</p>
        </div>
      ),
    },
    {
      key: 'amount',
      header: 'Amount',
      render: (invoice) => <span className="text-muted">{formatCurrency(invoice.amount, invoice.currency)}</span>,
    },
    {
      key: 'date',
      header: 'Issued',
      render: (invoice) => <span className="text-sm text-muted">{new Date(invoice.date).toLocaleString()}</span>,
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
    {
      key: 'actions',
      header: '',
      render: (invoice) => (
        <div className="flex justify-end">
          <Button variant="outline" size="sm" className="gap-2" aria-label={`View evidence for invoice ${invoice.id}`} onClick={() => openInvoiceEvidence(invoice)}>
            <FileSearch className="h-4 w-4" />
            Evidence
          </Button>
        </div>
      ),
    },
  ];

  const paymentColumns: Column<AdminBillingPaymentTransaction>[] = [
    {
      key: 'createdAt',
      header: 'Created',
      render: (payment) => <span className="text-sm text-muted">{formatDateTime(payment.createdAt)}</span>,
    },
    {
      key: 'learner',
      header: 'Learner',
      render: (payment) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{payment.learnerName}</p>
          <p className="text-xs uppercase tracking-[0.12em] text-muted">{payment.learnerUserId}</p>
        </div>
      ),
    },
    {
      key: 'gateway',
      header: 'Gateway',
      render: (payment) => (
        <div className="max-w-[220px] space-y-1">
          <Badge variant="outline">{payment.gateway || 'unknown'}</Badge>
          <p className="truncate font-mono text-xs text-muted" title={payment.gatewayTransactionId}>{payment.gatewayTransactionId}</p>
        </div>
      ),
    },
    {
      key: 'type',
      header: 'Type',
      render: (payment) => (
        <div className="space-y-1 text-muted">
          <p>{paymentTypeLabel(payment.transactionType)}</p>
          <p className="text-xs">{paymentProductLabel(payment)}</p>
        </div>
      ),
    },
    {
      key: 'amount',
      header: 'Amount',
      render: (payment) => <span className="text-muted">{formatCurrency(payment.amount, payment.currency)}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (payment) => <Badge variant={paymentStatusVariant(payment.status)}>{payment.status || 'unknown'}</Badge>,
    },
    {
      key: 'quoteId',
      header: 'Quote',
      render: (payment) => <span className="font-mono text-xs text-muted">{payment.quoteId ?? 'N/A'}</span>,
    },
  ];

  const planMobileCardRender = (plan: AdminBillingPlan) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{plan.name}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{plan.code ?? plan.id}</p>
          <div className="mt-1">{renderCatalogVersionMeta(plan)}</div>
        </div>
        <Badge variant={plan.status === 'active' ? 'success' : plan.status === 'archived' ? 'danger' : 'muted'}>
          {plan.status}
        </Badge>
      </div>

      {plan.description ? <p className="text-sm text-muted">{plan.description}</p> : null}

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Pricing</p>
          <p className="mt-1 font-medium text-navy">{formatCurrency(plan.price, plan.currency)} / {plan.interval}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Subscribers</p>
          <p className="mt-1 font-medium text-navy">{plan.activeSubscribers.toLocaleString()}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Credits</p>
          <p className="mt-1 font-medium text-navy">{(plan.includedCredits ?? 0).toLocaleString()}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Visibility</p>
          <p className="mt-1 font-medium text-navy">{plan.isVisible ? 'Visible' : 'Hidden'}</p>
        </div>
      </div>

      <div className="flex flex-col justify-end gap-2 sm:flex-row">
        <Button variant="outline" size="sm" className="w-full gap-2 sm:w-auto" aria-label={`View version history for ${plan.name}`} onClick={() => openCatalogHistory({ kind: 'plan', id: plan.id, name: plan.name, code: plan.code ?? plan.id })}>
          <HistoryIcon className="h-4 w-4" />
          History
        </Button>
        <Button variant="outline" size="sm" className="w-full sm:w-auto" onClick={() => openPlanEditor(plan)}>
          Edit
        </Button>
      </div>
    </div>
  );

  const addOnMobileCardRender = (addOn: AdminBillingAddOn) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{addOn.name}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{addOn.code}</p>
          <div className="mt-1">{renderCatalogVersionMeta(addOn)}</div>
        </div>
        <Badge variant={addOn.status === 'active' ? 'success' : addOn.status === 'archived' ? 'danger' : 'muted'}>
          {addOn.status}
        </Badge>
      </div>

      {addOn.description ? <p className="text-sm text-muted">{addOn.description}</p> : null}

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Pricing</p>
          <p className="mt-1 font-medium text-navy">{formatCurrency(addOn.price, addOn.currency)} / {addOn.interval}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Credits</p>
          <p className="mt-1 font-medium text-navy">{addOn.grantCredits.toLocaleString()}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Scope</p>
          <p className="mt-1 font-medium text-navy">{addOn.appliesToAllPlans ? 'All plans' : (addOn.compatiblePlanCodes.join(', ') || 'Restricted')}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Type</p>
          <p className="mt-1 font-medium text-navy">{addOn.isRecurring ? 'Recurring' : 'One-time'}</p>
        </div>
      </div>

      <div className="flex flex-col justify-end gap-2 sm:flex-row">
        <Button variant="outline" size="sm" className="w-full gap-2 sm:w-auto" aria-label={`View version history for ${addOn.name}`} onClick={() => openCatalogHistory({ kind: 'add_on', id: addOn.id, name: addOn.name, code: addOn.code })}>
          <HistoryIcon className="h-4 w-4" />
          History
        </Button>
        <Button variant="outline" size="sm" className="w-full sm:w-auto" onClick={() => openAddOnEditor(addOn)}>
          Edit
        </Button>
      </div>
    </div>
  );

  const couponMobileCardRender = (coupon: AdminBillingCoupon) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{coupon.code}</p>
          <p className="truncate text-sm text-muted">{coupon.name}</p>
          <div className="mt-1">{renderCatalogVersionMeta(coupon)}</div>
        </div>
        <Badge variant={coupon.status === 'active' ? 'success' : coupon.status === 'archived' ? 'danger' : 'muted'}>
          {coupon.status}
        </Badge>
      </div>

      {coupon.description ? <p className="text-sm text-muted">{coupon.description}</p> : null}

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Discount</p>
          <p className="mt-1 font-medium text-navy">
            {coupon.discountType === 'percentage'
              ? `${coupon.discountValue}%`
              : formatCurrency(coupon.discountValue, coupon.currency)}
          </p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Redemptions</p>
          <p className="mt-1 font-medium text-navy">{coupon.redemptionCount.toLocaleString()}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Usage limit</p>
          <p className="mt-1 font-medium text-navy">{coupon.usageLimitTotal == null ? 'Unlimited' : coupon.usageLimitTotal.toLocaleString()}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Scope</p>
          <p className="mt-1 font-medium text-navy">{coupon.isStackable ? 'Stackable' : 'Single use'}</p>
        </div>
      </div>

      <div className="flex flex-col justify-end gap-2 sm:flex-row">
        <Button variant="outline" size="sm" className="w-full gap-2 sm:w-auto" aria-label={`View version history for ${coupon.code}`} onClick={() => openCatalogHistory({ kind: 'coupon', id: coupon.id, name: coupon.name, code: coupon.code })}>
          <HistoryIcon className="h-4 w-4" />
          History
        </Button>
        <Button variant="outline" size="sm" className="w-full sm:w-auto" onClick={() => openCouponEditor(coupon)}>
          Edit
        </Button>
      </div>
    </div>
  );

  const subscriptionMobileCardRender = (subscription: AdminBillingSubscription) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{subscription.userName}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{subscription.userId}</p>
        </div>
        <Badge variant={subscription.status === 'active' ? 'success' : subscription.status === 'trial' ? 'info' : 'muted'}>
          {subscription.status}
        </Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Plan</p>
          <p className="mt-1 font-medium text-navy">{subscription.planName}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Billing</p>
          <p className="mt-1 font-medium text-navy">{formatCurrency(subscription.price, subscription.currency)} / {subscription.interval}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Add-ons</p>
          <p className="mt-1 font-medium text-navy">{subscription.addOnCount.toLocaleString()}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Renewal</p>
          <p className="mt-1 font-medium text-navy">{subscription.nextRenewalAt ? new Date(subscription.nextRenewalAt).toLocaleString() : 'N/A'}</p>
        </div>
      </div>
    </div>
  );

  const redemptionMobileCardRender = (redemption: AdminBillingCouponRedemption) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{redemption.couponCode}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{redemption.userId}</p>
        </div>
        <Badge variant={redemption.status === 'applied' ? 'success' : redemption.status === 'rejected' ? 'danger' : 'muted'}>
          {redemption.status}
        </Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Discount</p>
          <p className="mt-1 font-medium text-navy">{formatCurrency(redemption.discountAmount, redemption.currency)}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Redeemed</p>
          <p className="mt-1 font-medium text-navy">{new Date(redemption.redeemedAt).toLocaleString()}</p>
        </div>
      </div>
    </div>
  );

  const invoiceMobileCardRender = (invoice: AdminBillingInvoice) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{invoice.userName}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{invoice.plan}</p>
        </div>
        <Badge variant={invoice.status === 'paid' ? 'success' : invoice.status === 'failed' ? 'danger' : 'warning'}>
          {invoice.status}
        </Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Invoice</p>
          <p className="mt-1 font-mono text-xs text-navy">{invoice.id}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Amount</p>
          <p className="mt-1 font-medium text-navy">{formatCurrency(invoice.amount, invoice.currency)}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Issued</p>
          <p className="mt-1 font-medium text-navy">{new Date(invoice.date).toLocaleString()}</p>
        </div>
      </div>

      <Button variant="outline" size="sm" className="w-full gap-2" aria-label={`View evidence for invoice ${invoice.id}`} onClick={() => openInvoiceEvidence(invoice)}>
        <FileSearch className="h-4 w-4" />
        Evidence
      </Button>
    </div>
  );

  const paymentMobileCardRender = (payment: AdminBillingPaymentTransaction) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-navy">{payment.learnerName}</p>
          <p className="truncate font-mono text-xs text-muted">{payment.gatewayTransactionId}</p>
        </div>
        <Badge variant={paymentStatusVariant(payment.status)}>{payment.status || 'unknown'}</Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Amount</p>
          <p className="mt-1 font-medium text-navy">{formatCurrency(payment.amount, payment.currency)}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Gateway</p>
          <p className="mt-1 font-medium text-navy">{payment.gateway || 'unknown'}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Type</p>
          <p className="mt-1 font-medium text-navy">{paymentTypeLabel(payment.transactionType)}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Created</p>
          <p className="mt-1 font-medium text-navy">{formatDateTime(payment.createdAt)}</p>
        </div>
      </div>

      <div className="space-y-1 text-xs text-muted">
        <p className="break-all">{paymentProductLabel(payment)}</p>
        {payment.quoteId ? <p className="break-all font-mono">Quote {payment.quoteId}</p> : null}
      </div>
    </div>
  );

  const planFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Plan Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'draft', label: 'Draft' },
        { id: 'inactive', label: 'Inactive' },
        { id: 'archived', label: 'Archived' },
        { id: 'legacy', label: 'Legacy' },
      ],
    },
  ];

  const addOnFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Add-on Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'draft', label: 'Draft' },
        { id: 'inactive', label: 'Inactive' },
        { id: 'archived', label: 'Archived' },
      ],
    },
  ];

  const couponFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Coupon Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'draft', label: 'Draft' },
        { id: 'inactive', label: 'Inactive' },
        { id: 'archived', label: 'Archived' },
      ],
    },
  ];

  const subscriptionFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Subscription Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'trial', label: 'Trial' },
        { id: 'pending', label: 'Pending' },
        { id: 'past_due', label: 'Past due' },
        { id: 'suspended', label: 'Suspended' },
        { id: 'cancelled', label: 'Cancelled' },
        { id: 'expired', label: 'Expired' },
      ],
    },
  ];

  const redemptionFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Redemption Status',
      options: [
        { id: 'pending', label: 'Pending' },
        { id: 'applied', label: 'Applied' },
        { id: 'rejected', label: 'Rejected' },
        { id: 'reversed', label: 'Reversed' },
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

  const paymentFilterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Payment Status',
      options: [
        { id: 'pending', label: 'Pending' },
        { id: 'completed', label: 'Completed' },
        { id: 'failed', label: 'Failed' },
        { id: 'refunded', label: 'Refunded' },
        { id: 'disputed', label: 'Disputed' },
      ],
    },
    {
      id: 'gateway',
      label: 'Gateway',
      options: [
        { id: 'stripe', label: 'Stripe' },
        { id: 'paypal', label: 'PayPal' },
      ],
    },
    {
      id: 'transactionType',
      label: 'Type',
      options: [
        { id: 'subscription_payment', label: 'Subscription payment' },
        { id: 'one_time_purchase', label: 'One-time purchase' },
        { id: 'wallet_top_up', label: 'Wallet top-up' },
        { id: 'refund', label: 'Refund' },
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
    const [planItems, addOnItems, couponItems, subscriptionResult, redemptionResult, invoiceResult, paymentResult] = await Promise.all([
      getAdminBillingPlanData({ status: selectedPlanStatus }),
      getAdminBillingAddOnData({ status: selectedAddOnStatus }),
      getAdminBillingCouponData({ status: selectedCouponStatus }),
      getAdminBillingSubscriptionData({ status: selectedSubscriptionStatus, search: subscriptionSearch || undefined, pageSize: 100 }),
      getAdminBillingCouponRedemptionData({ couponCode: redemptionSearch || undefined, pageSize: 100 }),
      getAdminBillingInvoiceData({
        status: selectedInvoiceStatus,
        search: invoiceSearch || undefined,
        pageSize: 100,
      }),
      getAdminBillingPaymentTransactionData({
        status: selectedPaymentStatus,
        gateway: selectedPaymentGateway,
        transactionType: selectedPaymentTransactionType,
        search: paymentSearch || undefined,
        page: paymentPage,
        pageSize: paymentPageSize,
      }),
    ]);

    const redemptionItems = selectedRedemptionStatus ? redemptionResult.items.filter((item) => item.status === selectedRedemptionStatus) : redemptionResult.items;

    setPlans(planItems);
    setAddOns(addOnItems);
    setCoupons(couponItems);
    setSubscriptions(subscriptionResult.items);
    setRedemptions(redemptionItems);
    setInvoices(invoiceResult.items);
    setPaymentTransactions(paymentResult.items);
    setPaymentTotal(paymentResult.total);
    setPageStatus(
      planItems.length > 0 ||
      addOnItems.length > 0 ||
      couponItems.length > 0 ||
      subscriptionResult.items.length > 0 ||
      redemptionItems.length > 0 ||
      invoiceResult.items.length > 0 ||
      paymentResult.items.length > 0
        ? 'success'
        : 'empty',
    );
  }

  function openPlanEditor(plan?: AdminBillingPlan) {
    setEditingPlanId(plan?.id ?? null);
    setPlanForm(plan ? toPlanForm(plan) : defaultPlanForm);
    setIsPlanModalOpen(true);
  }

  function openAddOnEditor(addOn?: AdminBillingAddOn) {
    setEditingAddOnId(addOn?.id ?? null);
    setAddOnForm(addOn ? toAddOnForm(addOn) : defaultAddOnForm);
    setIsAddOnModalOpen(true);
  }

  function openCouponEditor(coupon?: AdminBillingCoupon) {
    setEditingCouponId(coupon?.id ?? null);
    setCouponForm(coupon ? toCouponForm(coupon) : defaultCouponForm);
    setIsCouponModalOpen(true);
  }

  async function openCatalogHistory(target: CatalogHistoryTarget) {
    const requestId = catalogHistoryRequestRef.current + 1;
    catalogHistoryRequestRef.current = requestId;
    setCatalogHistoryTarget(target);
    setCatalogHistory(null);
    setCatalogHistoryStatus('loading');

    try {
      const history = target.kind === 'plan'
        ? await getAdminBillingPlanVersionHistoryData(target.id)
        : target.kind === 'add_on'
          ? await getAdminBillingAddOnVersionHistoryData(target.id)
          : await getAdminBillingCouponVersionHistoryData(target.id);

      if (catalogHistoryRequestRef.current !== requestId) return;

      if (history.subject.id !== target.id || history.subject.kind !== target.kind) {
        setCatalogHistoryStatus('error');
        setToast({ variant: 'error', message: 'Loaded catalog history did not match the selected item.' });
        return;
      }

      setCatalogHistory(history);
      setCatalogHistoryStatus(history.items.length > 0 ? 'success' : 'empty');
    } catch (error) {
      if (catalogHistoryRequestRef.current !== requestId) return;
      console.error(error);
      setCatalogHistoryStatus('error');
      setToast({ variant: 'error', message: 'Unable to load catalog version history.' });
    }
  }

  function closeCatalogHistory() {
    catalogHistoryRequestRef.current += 1;
    setCatalogHistoryTarget(null);
    setCatalogHistory(null);
    setCatalogHistoryStatus('empty');
  }

  async function openInvoiceEvidence(invoice: AdminBillingInvoice) {
    const requestId = invoiceEvidenceRequestRef.current + 1;
    invoiceEvidenceRequestRef.current = requestId;
    setInvoiceEvidenceTarget({ id: invoice.id, userName: invoice.userName, plan: invoice.plan });
    setInvoiceEvidence(null);
    setInvoiceEvidenceStatus('loading');

    try {
      const evidence = await getAdminBillingInvoiceEvidenceData(invoice.id);
      if (invoiceEvidenceRequestRef.current !== requestId) return;

      if (evidence.invoice.id !== invoice.id) {
        setInvoiceEvidenceStatus('error');
        setToast({ variant: 'error', message: 'Loaded invoice evidence did not match the selected invoice.' });
        return;
      }

      setInvoiceEvidence(evidence);
      setInvoiceEvidenceStatus('success');
    } catch (error) {
      if (invoiceEvidenceRequestRef.current !== requestId) return;
      console.error(error);
      setInvoiceEvidenceStatus('error');
      setToast({ variant: 'error', message: 'Unable to load invoice evidence.' });
    }
  }

  function closeInvoiceEvidence() {
    invoiceEvidenceRequestRef.current += 1;
    setInvoiceEvidenceTarget(null);
    setInvoiceEvidence(null);
    setInvoiceEvidenceStatus('empty');
  }

  async function handleSavePlan() {
    setIsSavingPlan(true);
    try {
      const payload = {
        code: normalizedCode(planForm.code || planForm.name),
        name: planForm.name,
        description: planForm.description,
        price: toNumber(planForm.price),
        currency: planForm.currency,
        interval: planForm.interval,
        durationMonths: toNumber(planForm.durationMonths, 1),
        includedCredits: toNumber(planForm.includedCredits),
        displayOrder: toNumber(planForm.displayOrder),
        isVisible: planForm.isVisible,
        isRenewable: planForm.isRenewable,
        trialDays: toNumber(planForm.trialDays),
        status: planForm.status,
        includedSubtestsJson: jsonList(planForm.includedSubtestsText),
        entitlementsJson: safeJsonObject(planForm.entitlementsJson),
      };

      if (editingPlanId) {
        await updateAdminBillingPlan(editingPlanId, payload);
      } else {
        await createAdminBillingPlan(payload);
      }

      await reloadBilling();
      setIsPlanModalOpen(false);
      setPlanForm(defaultPlanForm);
      setEditingPlanId(null);
      setToast({ variant: 'success', message: editingPlanId ? 'Billing plan updated successfully.' : 'Billing plan created successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save this billing plan.' });
    } finally {
      setIsSavingPlan(false);
    }
  }

  async function handleSaveAddOn() {
    setIsSavingAddOn(true);
    try {
      const payload = {
        code: normalizedCode(addOnForm.code || addOnForm.name),
        name: addOnForm.name,
        description: addOnForm.description,
        price: toNumber(addOnForm.price),
        currency: addOnForm.currency,
        interval: addOnForm.interval,
        durationDays: toNumber(addOnForm.durationDays),
        grantCredits: toNumber(addOnForm.grantCredits),
        displayOrder: toNumber(addOnForm.displayOrder),
        isRecurring: addOnForm.isRecurring,
        appliesToAllPlans: addOnForm.appliesToAllPlans,
        isStackable: addOnForm.isStackable,
        quantityStep: toNumber(addOnForm.quantityStep, 1),
        maxQuantity: toOptionalNumber(addOnForm.maxQuantity),
        status: addOnForm.status,
        compatiblePlanCodesJson: jsonList(addOnForm.compatiblePlanCodesText),
        grantEntitlementsJson: safeJsonObject(addOnForm.grantEntitlementsJson),
      };

      if (editingAddOnId) {
        await updateAdminBillingAddOn(editingAddOnId, payload);
      } else {
        await createAdminBillingAddOn(payload);
      }

      await reloadBilling();
      setIsAddOnModalOpen(false);
      setAddOnForm(defaultAddOnForm);
      setEditingAddOnId(null);
      setToast({ variant: 'success', message: editingAddOnId ? 'Billing add-on updated successfully.' : 'Billing add-on created successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save this billing add-on.' });
    } finally {
      setIsSavingAddOn(false);
    }
  }

  async function handleSaveCoupon() {
    setIsSavingCoupon(true);
    try {
      const payload = {
        code: normalizedCode(couponForm.code || couponForm.name),
        name: couponForm.name,
        description: couponForm.description,
        discountType: couponForm.discountType,
        discountValue: toNumber(couponForm.discountValue),
        currency: couponForm.currency,
        startsAt: fromLocalDateTimeValue(couponForm.startsAt),
        endsAt: fromLocalDateTimeValue(couponForm.endsAt),
        usageLimitTotal: toOptionalNumber(couponForm.usageLimitTotal),
        usageLimitPerUser: toOptionalNumber(couponForm.usageLimitPerUser),
        minimumSubtotal: toOptionalNumber(couponForm.minimumSubtotal),
        isStackable: couponForm.isStackable,
        status: couponForm.status,
        applicablePlanCodesJson: jsonList(couponForm.applicablePlanCodesText),
        applicableAddOnCodesJson: jsonList(couponForm.applicableAddOnCodesText),
        notes: couponForm.notes.trim() || null,
      };

      if (editingCouponId) {
        await updateAdminBillingCoupon(editingCouponId, payload);
      } else {
        await createAdminBillingCoupon(payload);
      }

      await reloadBilling();
      setIsCouponModalOpen(false);
      setCouponForm(defaultCouponForm);
      setEditingCouponId(null);
      setToast({ variant: 'success', message: editingCouponId ? 'Billing coupon updated successfully.' : 'Billing coupon created successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save this billing coupon.' });
    } finally {
      setIsSavingCoupon(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Billing operations">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Billing Operations"
        description="Manage subscription plans, add-ons, coupons, subscriptions, and invoices with live backend data."
        actions={
          <>
            <Button onClick={() => openPlanEditor()} className="gap-2">
              <CreditCard className="h-4 w-4" />
              Create Plan
            </Button>
            <Button variant="outline" onClick={() => openAddOnEditor()} className="gap-2">
              <Package className="h-4 w-4" />
              Create Add-on
            </Button>
            <Button variant="outline" onClick={() => openCouponEditor()} className="gap-2">
              <Ticket className="h-4 w-4" />
              Create Coupon
            </Button>
          </>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Receipt className="h-10 w-10 text-muted" />}
            title="No billing records found"
            description="Create a plan or wait for invoices to populate the admin billing workspace."
            action={{ label: 'Create Plan', onClick: () => setIsPlanModalOpen(true) }}
          />
        }
      >
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <AdminRouteSummaryCard label="Monthly Revenue" value={formatCurrency(metrics.totalMRR)} icon={<DollarSign className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Subscribers" value={metrics.totalSubscribers} icon={<Users className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Active Subscriptions" value={metrics.activeSubscriptions} icon={<Users className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Active Plans" value={metrics.activePlans} icon={<CreditCard className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Active Add-ons" value={metrics.activeAddOns} icon={<Package className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Active Coupons" value={metrics.activeCoupons} icon={<Ticket className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Failed Invoices" value={metrics.failedInvoices} icon={<Receipt className="h-5 w-5" />} tone={metrics.failedInvoices > 0 ? 'danger' : 'default'} />
        </div>

        <AdminRoutePanel
          title="Subscription Plans"
          description="Live plan data from the admin billing plan endpoint."
          actions={<Button variant="outline" size="sm" onClick={() => openPlanEditor()}>Create Plan</Button>}
        >
          <FilterBar groups={planFilterGroups} selected={planFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setPlanFilters, groupId, optionId)} onClear={() => setPlanFilters({ status: [] })} />
          <DataTable columns={planColumns} data={plans} keyExtractor={(plan) => plan.id} mobileCardRender={planMobileCardRender} />
        </AdminRoutePanel>

        <AdminRoutePanel
          title="Add-ons"
          description="Manage review-credit packs and other purchasable subscription items."
          actions={<Button variant="outline" size="sm" onClick={() => openAddOnEditor()}>Create Add-on</Button>}
        >
          <FilterBar groups={addOnFilterGroups} selected={addOnFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setAddOnFilters, groupId, optionId)} onClear={() => setAddOnFilters({ status: [] })} />
          <DataTable columns={addOnColumns} data={addOns} keyExtractor={(addOn) => addOn.id} mobileCardRender={addOnMobileCardRender} />
        </AdminRoutePanel>

        <AdminRoutePanel
          title="Coupons"
          description="Create and edit promo codes, limits, validity windows, and scope rules."
          actions={<Button variant="outline" size="sm" onClick={() => openCouponEditor()}>Create Coupon</Button>}
        >
          <FilterBar groups={couponFilterGroups} selected={couponFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setCouponFilters, groupId, optionId)} onClear={() => setCouponFilters({ status: [] })} />
          <DataTable columns={couponColumns} data={coupons} keyExtractor={(coupon) => coupon.id} mobileCardRender={couponMobileCardRender} />
        </AdminRoutePanel>

        <AdminRoutePanel
          title="Subscriptions"
          description="Read-only visibility into active learner subscriptions and attached items."
        >
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Search by user or plan" value={subscriptionSearch} onChange={(event) => setSubscriptionSearch(event.target.value)} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={subscriptionFilterGroups} selected={subscriptionFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setSubscriptionFilters, groupId, optionId)} onClear={() => setSubscriptionFilters({ status: [] })} />
          <DataTable columns={subscriptionColumns} data={subscriptions} keyExtractor={(subscription) => subscription.id} mobileCardRender={subscriptionMobileCardRender} />
        </AdminRoutePanel>

        <AdminRoutePanel
          title="Coupon Redemptions"
          description="Track which coupons were used and how much discount was granted."
        >
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Filter by coupon code" value={redemptionSearch} onChange={(event) => setRedemptionSearch(event.target.value)} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={redemptionFilterGroups} selected={redemptionFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setRedemptionFilters, groupId, optionId)} onClear={() => setRedemptionFilters({ status: [] })} />
          <DataTable columns={redemptionColumns} data={redemptions} keyExtractor={(redemption) => redemption.id} mobileCardRender={redemptionMobileCardRender} />
        </AdminRoutePanel>

        <AdminRoutePanel title="Invoices" description="Search and filter real invoice records by status and learner reference.">
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Search by user or plan description" value={invoiceSearch} onChange={(event) => setInvoiceSearch(event.target.value)} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={invoiceFilterGroups} selected={invoiceFilters} onChange={(groupId, optionId) => handleSingleFilterChange(setInvoiceFilters, groupId, optionId)} onClear={() => { setInvoiceFilters({ status: [] }); setInvoiceSearch(''); }} />
          <DataTable columns={invoiceColumns} data={invoices} keyExtractor={(invoice) => invoice.id} mobileCardRender={invoiceMobileCardRender} />
        </AdminRoutePanel>

        <AdminRoutePanel title="Payment Transactions" description="Read-only payment activity from checkout and wallet top-up attempts.">
          <div className="max-w-md">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Search learner, quote, product, or gateway ID" value={paymentSearch} onChange={(event) => setPaymentSearch(event.target.value)} className="pl-9" />
            </div>
          </div>
          <FilterBar
            groups={paymentFilterGroups}
            selected={paymentFilters}
            onChange={(groupId, optionId) => handleSingleFilterChange(setPaymentFilters, groupId, optionId)}
            onClear={() => { setPaymentFilters({ status: [], gateway: [], transactionType: [] }); setPaymentSearch(''); }}
          />
          <DataTable
            aria-label="Payment transactions"
            columns={paymentColumns}
            data={paymentTransactions}
            keyExtractor={(payment) => payment.id}
            mobileCardRender={paymentMobileCardRender}
            emptyMessage={paymentSearch || selectedPaymentStatus || selectedPaymentGateway || selectedPaymentTransactionType ? 'No payment transactions match the current filters.' : 'No payment transactions recorded yet.'}
          />
          <Pagination
            page={paymentPage}
            pageSize={paymentPageSize}
            total={paymentTotal}
            onPageChange={setPaymentPage}
            onPageSizeChange={setPaymentPageSize}
            pageSizeOptions={[20, 50, 100]}
            itemLabel="payment transaction"
            itemLabelPlural="payment transactions"
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Drawer
        open={catalogHistoryTarget !== null}
        onClose={closeCatalogHistory}
        title="Catalog Version History"
        className="sm:max-w-2xl"
      >
        {catalogHistoryTarget ? (
          <div className="space-y-5">
            <div className="border-b border-border pb-4">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="outline">{catalogHistory?.subject.kind.replace('_', ' ') ?? catalogHistoryTarget.kind.replace('_', ' ')}</Badge>
                {catalogHistory?.subject.activeVersionNumber ? <Badge variant="info">Active v{catalogHistory.subject.activeVersionNumber}</Badge> : null}
                {catalogHistory?.subject.versionCount ? <Badge variant="muted">{catalogHistory.subject.versionCount} versions</Badge> : null}
              </div>
              <h2 className="mt-3 text-lg font-semibold text-navy">{catalogHistory?.subject.name ?? catalogHistoryTarget.name}</h2>
              <p className="mt-1 text-xs uppercase tracking-[0.12em] text-muted">{catalogHistory?.subject.code ?? catalogHistoryTarget.code}</p>
            </div>

            {catalogHistoryStatus === 'loading' ? (
              <div className="space-y-3" role="status" aria-live="polite">
                {[0, 1, 2].map((item) => (
                  <div key={item} className="h-24 animate-pulse rounded-lg bg-background-light" />
                ))}
              </div>
            ) : null}

            {catalogHistoryStatus === 'error' ? (
              <EmptyState
                icon={<HistoryIcon className="h-10 w-10 text-muted" />}
                title="Version history unavailable"
                description="Refresh the page or try the selected catalog item again."
              />
            ) : null}

            {catalogHistoryStatus === 'empty' ? (
              <EmptyState
                icon={<HistoryIcon className="h-10 w-10 text-muted" />}
                title="No versions found"
                description="This catalog item does not have recorded version rows yet."
              />
            ) : null}

            {catalogHistoryStatus === 'success' && catalogHistory ? (
              <div className="space-y-4">
                {catalogHistory.items.map((version) => {
                  const summaryEntries = Object.entries(version.summary)
                    .filter(([, value]) => value !== null && value !== undefined && value !== '')
                    .slice(0, 6);

                  return (
                    <div key={version.id} className="relative border-l border-border pl-4">
                      <span className="absolute -left-1.5 top-2 h-3 w-3 rounded-full border-2 border-white bg-primary" aria-hidden="true" />
                      <div className="space-y-3 rounded-lg border border-border bg-white p-4 shadow-sm">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div>
                            <div className="flex flex-wrap items-center gap-2">
                              <Badge variant={version.isActive ? 'success' : 'muted'}>v{version.versionNumber}</Badge>
                              {version.isLatest ? <Badge variant="info">Latest</Badge> : null}
                              <Badge variant={version.status === 'active' ? 'success' : version.status === 'archived' ? 'danger' : 'muted'}>{version.status}</Badge>
                            </div>
                            <p className="mt-2 font-medium text-navy">{version.name}</p>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">{version.code}</p>
                          </div>
                          <div className="text-right text-xs text-muted">
                            <p>{new Date(version.createdAt).toLocaleString()}</p>
                            <p>{version.createdByAdminName ?? version.createdByAdminId ?? 'System'}</p>
                          </div>
                        </div>

                        {version.description ? <p className="text-sm text-muted">{version.description}</p> : null}

                        {summaryEntries.length > 0 ? (
                          <dl className="grid gap-2 text-sm sm:grid-cols-2">
                            {summaryEntries.map(([key, value]) => (
                              <div key={key} className="rounded-lg bg-background-light px-3 py-2">
                                <dt className="text-[11px] uppercase tracking-[0.12em] text-muted">{labelSummaryKey(key)}</dt>
                                <dd className="mt-1 break-words font-medium text-navy">{formatSummaryValue(value)}</dd>
                              </div>
                            ))}
                          </dl>
                        ) : null}
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : null}
          </div>
        ) : null}
      </Drawer>

      <Drawer
        open={invoiceEvidenceTarget !== null}
        onClose={closeInvoiceEvidence}
        title="Invoice Evidence"
        className="sm:max-w-3xl"
      >
        {invoiceEvidenceTarget ? (
          <div className="space-y-5">
            <div className="border-b border-border pb-4">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="outline">read only</Badge>
                {invoiceEvidence ? <Badge variant={invoiceEvidence.invoice.status === 'paid' ? 'success' : invoiceEvidence.invoice.status === 'failed' ? 'danger' : 'warning'}>{invoiceEvidence.invoice.status}</Badge> : null}
                {invoiceEvidence?.payments.length ? <Badge variant="info">{invoiceEvidence.payments.length} payment {invoiceEvidence.payments.length === 1 ? 'record' : 'records'}</Badge> : null}
                {invoiceEvidence?.notRecorded.length ? <Badge variant="muted">partial local evidence</Badge> : null}
              </div>
              <h2 className="mt-3 text-lg font-semibold text-navy">{invoiceEvidence?.invoice.userName ?? invoiceEvidenceTarget.userName}</h2>
              <p className="mt-1 break-words font-mono text-xs text-muted">{invoiceEvidenceTarget.id}</p>
              <p className="mt-1 text-sm text-muted">{invoiceEvidence?.invoice.description ?? invoiceEvidenceTarget.plan}</p>
            </div>

            {invoiceEvidenceStatus === 'loading' ? (
              <div className="space-y-3" role="status" aria-live="polite">
                {[0, 1, 2, 3].map((item) => (
                  <div key={item} className="h-24 animate-pulse rounded-lg bg-background-light" />
                ))}
              </div>
            ) : null}

            {invoiceEvidenceStatus === 'error' ? (
              <EmptyState
                icon={<FileSearch className="h-10 w-10 text-muted" />}
                title="Invoice evidence unavailable"
                description="Refresh the page or try the selected invoice again."
              />
            ) : null}

            {invoiceEvidenceStatus === 'success' && invoiceEvidence ? (
              <div className="space-y-5">
                {invoiceEvidence.notRecorded.length > 0 ? (
                  <div className="rounded-lg border border-border bg-background-light px-4 py-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Not recorded</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {invoiceEvidence.notRecorded.map((item) => <Badge key={item} variant="muted">{evidenceGapLabel(item)}</Badge>)}
                    </div>
                  </div>
                ) : null}

                {invoiceEvidence.integrityFlags.length > 0 ? (
                  <div className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Integrity flags</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {invoiceEvidence.integrityFlags.map((item) => <Badge key={item} variant="warning">{labelSummaryKey(item)}</Badge>)}
                    </div>
                  </div>
                ) : null}

                <EvidenceSection title="Invoice">
                  <dl className="grid gap-3 sm:grid-cols-2">
                    <EvidenceField label="Invoice ID">{invoiceEvidence.invoice.id}</EvidenceField>
                    <EvidenceField label="Learner">{invoiceEvidence.invoice.userName}</EvidenceField>
                    <EvidenceField label="Issued">{formatDateTime(invoiceEvidence.invoice.issuedAt)}</EvidenceField>
                    <EvidenceField label="Amount">{formatCurrency(invoiceEvidence.invoice.amount, invoiceEvidence.invoice.currency)}</EvidenceField>
                    <EvidenceField label="Quote ID">{invoiceEvidence.invoice.quoteId ?? 'Not recorded'}</EvidenceField>
                    <EvidenceField label="Checkout Session">{invoiceEvidence.invoice.checkoutSessionId ?? 'Not recorded'}</EvidenceField>
                  </dl>
                </EvidenceSection>

                <EvidenceSection title="Quote Snapshot">
                  {invoiceEvidence.quote ? (
                    <div className="space-y-3">
                      <dl className="grid gap-3 sm:grid-cols-2">
                        <EvidenceField label="Quote ID">{invoiceEvidence.quote.id}</EvidenceField>
                        <EvidenceField label="Status">{invoiceEvidence.quote.status}</EvidenceField>
                        <EvidenceField label="Subtotal">{formatCurrency(invoiceEvidence.quote.subtotalAmount, invoiceEvidence.quote.currency)}</EvidenceField>
                        <EvidenceField label="Discount">{formatCurrency(invoiceEvidence.quote.discountAmount, invoiceEvidence.quote.currency)}</EvidenceField>
                        <EvidenceField label="Total">{formatCurrency(invoiceEvidence.quote.totalAmount, invoiceEvidence.quote.currency)}</EvidenceField>
                        <EvidenceField label="Expires">{formatDateTime(invoiceEvidence.quote.expiresAt)}</EvidenceField>
                      </dl>
                      {invoiceEvidence.quote.items.length > 0 ? (
                        <div className="space-y-2">
                          {invoiceEvidence.quote.items.map((item) => (
                            <div key={`${item.kind}-${item.code}`} className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-background-light px-3 py-2 text-sm">
                              <div className="min-w-0">
                                <p className="font-medium text-navy">{item.name}</p>
                                <p className="text-xs uppercase tracking-[0.12em] text-muted">{item.kind} - {item.code}</p>
                              </div>
                              <p className="font-medium text-navy">{formatCurrency(item.amount, item.currency)} x {item.quantity}</p>
                            </div>
                          ))}
                        </div>
                      ) : <p className="text-sm text-muted">Not recorded</p>}
                    </div>
                  ) : <p className="text-sm text-muted">Not recorded</p>}
                </EvidenceSection>

                <EvidenceSection title="Payment">
                  {invoiceEvidence.payments.length > 0 ? (
                    <div className="space-y-3">
                      {invoiceEvidence.payments.map((payment) => (
                        <dl key={payment.id} className="grid gap-3 rounded-lg border border-border p-3 sm:grid-cols-2">
                          <EvidenceField label="Gateway">{payment.gateway}</EvidenceField>
                          <EvidenceField label="Gateway Transaction">{payment.gatewayTransactionId}</EvidenceField>
                          <EvidenceField label="Status">{payment.status}</EvidenceField>
                          <EvidenceField label="Amount">{formatCurrency(payment.amount, payment.currency)}</EvidenceField>
                          <EvidenceField label="Product">{payment.productType || 'Not recorded'} / {payment.productId || 'Not recorded'}</EvidenceField>
                          <EvidenceField label="Updated">{formatDateTime(payment.updatedAt)}</EvidenceField>
                        </dl>
                      ))}
                    </div>
                  ) : <p className="text-sm text-muted">Not recorded</p>}
                </EvidenceSection>

                <EvidenceSection title="Coupon">
                  {invoiceEvidence.redemptions.length > 0 ? (
                    <div className="space-y-3">
                      {invoiceEvidence.redemptions.map((redemption) => (
                        <dl key={redemption.id} className="grid gap-3 rounded-lg border border-border p-3 sm:grid-cols-2">
                          <EvidenceField label="Coupon">{redemption.couponCode}</EvidenceField>
                          <EvidenceField label="Status">{redemption.status}</EvidenceField>
                          <EvidenceField label="Discount">{formatCurrency(redemption.discountAmount, redemption.currency)}</EvidenceField>
                          <EvidenceField label="Redeemed">{formatDateTime(redemption.redeemedAt)}</EvidenceField>
                          <EvidenceField label="Coupon Version">{redemption.couponVersionId ?? 'Not recorded'}</EvidenceField>
                          <EvidenceField label="Subscription">{redemption.subscriptionId ?? 'Not recorded'}</EvidenceField>
                        </dl>
                      ))}
                    </div>
                  ) : <p className="text-sm text-muted">Not recorded</p>}
                </EvidenceSection>

                <EvidenceSection title="Subscription Items">
                  {invoiceEvidence.subscriptionItems.length > 0 ? (
                    <div className="space-y-2">
                      {invoiceEvidence.subscriptionItems.map((item) => (
                        <div key={item.id} className="grid gap-2 rounded-lg bg-background-light px-3 py-2 text-sm sm:grid-cols-[1fr_auto]">
                          <div className="min-w-0">
                            <p className="font-medium text-navy">{item.itemCode}</p>
                            <p className="text-xs uppercase tracking-[0.12em] text-muted">{item.itemType} - {item.addOnVersionId ?? 'Not recorded'}</p>
                          </div>
                          <div className="flex flex-wrap items-center gap-2 sm:justify-end">
                            <Badge variant={item.status === 'active' ? 'success' : 'muted'}>{item.status}</Badge>
                            <span className="text-muted">qty {item.quantity}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : <p className="text-sm text-muted">Not recorded</p>}
                </EvidenceSection>

                <EvidenceSection title="Catalog Anchors">
                  <dl className="grid gap-3 sm:grid-cols-2">
                    <EvidenceField label="Source">{invoiceEvidence.catalogAnchors.source === 'not_recorded' ? 'Not recorded' : invoiceEvidence.catalogAnchors.source}</EvidenceField>
                    <EvidenceField label="Plan Version">{invoiceEvidence.catalogAnchors.planVersionId ?? 'Not recorded'}</EvidenceField>
                    <EvidenceField label="Coupon Version">{invoiceEvidence.catalogAnchors.couponVersionId ?? 'Not recorded'}</EvidenceField>
                    <EvidenceField label="Add-on Versions">
                      {Object.entries(invoiceEvidence.catalogAnchors.addOnVersionIds).length > 0
                        ? Object.entries(invoiceEvidence.catalogAnchors.addOnVersionIds).map(([code, version]) => `${code}: ${version}`).join(', ')
                        : 'Not recorded'}
                    </EvidenceField>
                  </dl>
                </EvidenceSection>

                <EvidenceSection title="Events">
                  {invoiceEvidence.events.length > 0 ? (
                    <div className="space-y-3">
                      {invoiceEvidence.events.map((event) => (
                        <div key={event.id} className="relative border-l border-border pl-4">
                          <span className="absolute -left-1.5 top-2 h-3 w-3 rounded-full border-2 border-white bg-primary" aria-hidden="true" />
                          <div className="rounded-lg bg-background-light px-3 py-2">
                            <div className="flex flex-wrap items-start justify-between gap-3">
                              <div className="min-w-0">
                                <p className="break-words font-medium text-navy">{event.eventType}</p>
                                <p className="break-words text-xs uppercase tracking-[0.12em] text-muted">{event.entityType} - {event.entityId || 'Not recorded'}</p>
                              </div>
                              <p className="text-xs text-muted">{formatDateTime(event.occurredAt)}</p>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : <p className="text-sm text-muted">Not recorded</p>}
                </EvidenceSection>
              </div>
            ) : null}
          </div>
        ) : null}
      </Drawer>

      <Modal
        open={isPlanModalOpen}
        onClose={() => {
          setIsPlanModalOpen(false);
          setEditingPlanId(null);
          setPlanForm(defaultPlanForm);
        }}
        title={editingPlanId ? 'Edit Billing Plan' : 'Create Billing Plan'}
      >
        <div className="space-y-4 py-2">
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Code" value={planForm.code} onChange={(event) => setPlanForm((current) => ({ ...current, code: event.target.value }))} />
            <Input label="Name" value={planForm.name} onChange={(event) => setPlanForm((current) => ({ ...current, name: event.target.value }))} />
          </div>
          <Textarea label="Description" value={planForm.description} onChange={(event) => setPlanForm((current) => ({ ...current, description: event.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Input label="Price" type="number" min={0} step="0.01" value={planForm.price} onChange={(event) => setPlanForm((current) => ({ ...current, price: event.target.value }))} />
            <Input label="Currency" value={planForm.currency} onChange={(event) => setPlanForm((current) => ({ ...current, currency: event.target.value.toUpperCase() }))} />
            <Select
              label="Interval"
              value={planForm.interval}
              onChange={(event) => setPlanForm((current) => ({ ...current, interval: event.target.value }))}
              options={[
                { value: 'month', label: 'Monthly' },
                { value: 'year', label: 'Yearly' },
              ]}
            />
          </div>
          <div className="grid gap-4 md:grid-cols-4">
            <Input label="Duration months" type="number" min={1} value={planForm.durationMonths} onChange={(event) => setPlanForm((current) => ({ ...current, durationMonths: event.target.value }))} />
            <Input label="Included credits" type="number" min={0} value={planForm.includedCredits} onChange={(event) => setPlanForm((current) => ({ ...current, includedCredits: event.target.value }))} />
            <Input label="Display order" type="number" min={0} value={planForm.displayOrder} onChange={(event) => setPlanForm((current) => ({ ...current, displayOrder: event.target.value }))} />
            <Input label="Trial days" type="number" min={0} value={planForm.trialDays} onChange={(event) => setPlanForm((current) => ({ ...current, trialDays: event.target.value }))} />
          </div>
          <div className="flex flex-wrap gap-4">
            <Checkbox label="Visible" checked={planForm.isVisible} onChange={(event) => setPlanForm((current) => ({ ...current, isVisible: event.target.checked }))} />
            <Checkbox label="Renewable" checked={planForm.isRenewable} onChange={(event) => setPlanForm((current) => ({ ...current, isRenewable: event.target.checked }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <Select
              label="Status"
              value={planForm.status}
              onChange={(event) => setPlanForm((current) => ({ ...current, status: event.target.value }))}
              options={[
                { value: 'active', label: 'Active' },
                { value: 'draft', label: 'Draft' },
                { value: 'inactive', label: 'Inactive' },
                { value: 'archived', label: 'Archived' },
                { value: 'legacy', label: 'Legacy' },
              ]}
            />
            <Input label="Included subtests" value={planForm.includedSubtestsText} onChange={(event) => setPlanForm((current) => ({ ...current, includedSubtestsText: event.target.value }))} hint="Comma-separated codes like writing, speaking" />
          </div>

          <ContentScopePanel
            entitlementsJson={planForm.entitlementsJson}
            onChange={(next) => setPlanForm((current) => ({ ...current, entitlementsJson: next }))}
          />

          <Textarea label="Entitlements JSON" value={planForm.entitlementsJson} onChange={(event) => setPlanForm((current) => ({ ...current, entitlementsJson: event.target.value }))} hint="Advanced. Edit the panel above for the standard content-gating fields." />

          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button
              variant="outline"
              onClick={() => {
                setIsPlanModalOpen(false);
                setEditingPlanId(null);
                setPlanForm(defaultPlanForm);
              }}
            >
              Cancel
            </Button>
            <Button onClick={handleSavePlan} loading={isSavingPlan}>
              {editingPlanId ? 'Update Plan' : 'Save Plan'}
            </Button>
          </div>
        </div>
      </Modal>

      <Modal
        open={isAddOnModalOpen}
        onClose={() => {
          setIsAddOnModalOpen(false);
          setEditingAddOnId(null);
          setAddOnForm(defaultAddOnForm);
        }}
        title={editingAddOnId ? 'Edit Add-on' : 'Create Add-on'}
      >
        <div className="space-y-4 py-2">
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Code" value={addOnForm.code} onChange={(event) => setAddOnForm((current) => ({ ...current, code: event.target.value }))} />
            <Input label="Name" value={addOnForm.name} onChange={(event) => setAddOnForm((current) => ({ ...current, name: event.target.value }))} />
          </div>
          <Textarea label="Description" value={addOnForm.description} onChange={(event) => setAddOnForm((current) => ({ ...current, description: event.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Input label="Price" type="number" min={0} step="0.01" value={addOnForm.price} onChange={(event) => setAddOnForm((current) => ({ ...current, price: event.target.value }))} />
            <Input label="Currency" value={addOnForm.currency} onChange={(event) => setAddOnForm((current) => ({ ...current, currency: event.target.value.toUpperCase() }))} />
            <Select
              label="Interval"
              value={addOnForm.interval}
              onChange={(event) => setAddOnForm((current) => ({ ...current, interval: event.target.value }))}
              options={[
                { value: 'one_time', label: 'One-time' },
                { value: 'month', label: 'Monthly' },
                { value: 'year', label: 'Yearly' },
              ]}
            />
          </div>
          <div className="grid gap-4 md:grid-cols-4">
            <Input label="Duration days" type="number" min={0} value={addOnForm.durationDays} onChange={(event) => setAddOnForm((current) => ({ ...current, durationDays: event.target.value }))} />
            <Input label="Grant credits" type="number" min={0} value={addOnForm.grantCredits} onChange={(event) => setAddOnForm((current) => ({ ...current, grantCredits: event.target.value }))} />
            <Input label="Display order" type="number" min={0} value={addOnForm.displayOrder} onChange={(event) => setAddOnForm((current) => ({ ...current, displayOrder: event.target.value }))} />
            <Input label="Quantity step" type="number" min={1} value={addOnForm.quantityStep} onChange={(event) => setAddOnForm((current) => ({ ...current, quantityStep: event.target.value }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Max quantity" type="number" min={0} value={addOnForm.maxQuantity} onChange={(event) => setAddOnForm((current) => ({ ...current, maxQuantity: event.target.value }))} />
            <Select
              label="Status"
              value={addOnForm.status}
              onChange={(event) => setAddOnForm((current) => ({ ...current, status: event.target.value }))}
              options={[
                { value: 'active', label: 'Active' },
                { value: 'draft', label: 'Draft' },
                { value: 'inactive', label: 'Inactive' },
                { value: 'archived', label: 'Archived' },
              ]}
            />
          </div>
          <div className="flex flex-wrap gap-4">
            <Checkbox label="Recurring" checked={addOnForm.isRecurring} onChange={(event) => setAddOnForm((current) => ({ ...current, isRecurring: event.target.checked }))} />
            <Checkbox label="Applies to all plans" checked={addOnForm.appliesToAllPlans} onChange={(event) => setAddOnForm((current) => ({ ...current, appliesToAllPlans: event.target.checked }))} />
            <Checkbox label="Stackable" checked={addOnForm.isStackable} onChange={(event) => setAddOnForm((current) => ({ ...current, isStackable: event.target.checked }))} />
          </div>
          <Input label="Compatible plan codes" value={addOnForm.compatiblePlanCodesText} onChange={(event) => setAddOnForm((current) => ({ ...current, compatiblePlanCodesText: event.target.value }))} hint="Comma-separated codes" />
          <Textarea label="Grant entitlements JSON" value={addOnForm.grantEntitlementsJson} onChange={(event) => setAddOnForm((current) => ({ ...current, grantEntitlementsJson: event.target.value }))} />

          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button
              variant="outline"
              onClick={() => {
                setIsAddOnModalOpen(false);
                setEditingAddOnId(null);
                setAddOnForm(defaultAddOnForm);
              }}
            >
              Cancel
            </Button>
            <Button onClick={handleSaveAddOn} loading={isSavingAddOn}>
              {editingAddOnId ? 'Update Add-on' : 'Save Add-on'}
            </Button>
          </div>
        </div>
      </Modal>

      <Modal
        open={isCouponModalOpen}
        onClose={() => {
          setIsCouponModalOpen(false);
          setEditingCouponId(null);
          setCouponForm(defaultCouponForm);
        }}
        title={editingCouponId ? 'Edit Coupon' : 'Create Coupon'}
      >
        <div className="space-y-4 py-2">
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Code" value={couponForm.code} onChange={(event) => setCouponForm((current) => ({ ...current, code: event.target.value }))} />
            <Input label="Name" value={couponForm.name} onChange={(event) => setCouponForm((current) => ({ ...current, name: event.target.value }))} />
          </div>
          <Textarea label="Description" value={couponForm.description} onChange={(event) => setCouponForm((current) => ({ ...current, description: event.target.value }))} />
          <div className="grid gap-4 md:grid-cols-3">
            <Select
              label="Discount type"
              value={couponForm.discountType}
              onChange={(event) => setCouponForm((current) => ({ ...current, discountType: event.target.value as 'percentage' | 'fixed' }))}
              options={[
                { value: 'percentage', label: 'Percentage' },
                { value: 'fixed', label: 'Fixed amount' },
              ]}
            />
            <Input label="Discount value" type="number" min={0} step="0.01" value={couponForm.discountValue} onChange={(event) => setCouponForm((current) => ({ ...current, discountValue: event.target.value }))} />
            <Input label="Currency" value={couponForm.currency} onChange={(event) => setCouponForm((current) => ({ ...current, currency: event.target.value.toUpperCase() }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Starts at" type="datetime-local" value={couponForm.startsAt} onChange={(event) => setCouponForm((current) => ({ ...current, startsAt: event.target.value }))} />
            <Input label="Ends at" type="datetime-local" value={couponForm.endsAt} onChange={(event) => setCouponForm((current) => ({ ...current, endsAt: event.target.value }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-3">
            <Input label="Usage limit total" type="number" min={0} value={couponForm.usageLimitTotal} onChange={(event) => setCouponForm((current) => ({ ...current, usageLimitTotal: event.target.value }))} />
            <Input label="Usage limit per user" type="number" min={0} value={couponForm.usageLimitPerUser} onChange={(event) => setCouponForm((current) => ({ ...current, usageLimitPerUser: event.target.value }))} />
            <Input label="Minimum subtotal" type="number" min={0} step="0.01" value={couponForm.minimumSubtotal} onChange={(event) => setCouponForm((current) => ({ ...current, minimumSubtotal: event.target.value }))} />
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <Select
              label="Status"
              value={couponForm.status}
              onChange={(event) => setCouponForm((current) => ({ ...current, status: event.target.value }))}
              options={[
                { value: 'active', label: 'Active' },
                { value: 'draft', label: 'Draft' },
                { value: 'inactive', label: 'Inactive' },
                { value: 'archived', label: 'Archived' },
              ]}
            />
            <Input label="Notes" value={couponForm.notes} onChange={(event) => setCouponForm((current) => ({ ...current, notes: event.target.value }))} />
          </div>
          <div className="flex flex-wrap gap-4">
            <Checkbox label="Stackable" checked={couponForm.isStackable} onChange={(event) => setCouponForm((current) => ({ ...current, isStackable: event.target.checked }))} />
          </div>
          <Input label="Applicable plan codes" value={couponForm.applicablePlanCodesText} onChange={(event) => setCouponForm((current) => ({ ...current, applicablePlanCodesText: event.target.value }))} hint="Comma-separated codes" />
          <Input label="Applicable add-on codes" value={couponForm.applicableAddOnCodesText} onChange={(event) => setCouponForm((current) => ({ ...current, applicableAddOnCodesText: event.target.value }))} hint="Comma-separated codes" />

          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button
              variant="outline"
              onClick={() => {
                setIsCouponModalOpen(false);
                setEditingCouponId(null);
                setCouponForm(defaultCouponForm);
              }}
            >
              Cancel
            </Button>
            <Button onClick={handleSaveCoupon} loading={isSavingCoupon}>
              {editingCouponId ? 'Update Coupon' : 'Save Coupon'}
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
