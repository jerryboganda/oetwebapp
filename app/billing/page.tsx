'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import {
  CreditCard,
  Calendar,
  Star,
  FileText,
  Download,
  ArrowUpCircle,
  ArrowDownCircle,
  PlusCircle,
  CheckCircle2
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchBilling } from '@/lib/api';
import type { BillingData } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';

export default function Billing() {
  const [data, setData] = useState<BillingData | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    analytics.track('content_view', { page: 'billing' });
    fetchBilling()
      .then(setData)
      .catch(() => setError('Could not load billing data.'));
  }, []);

  if (error) {
    return (
      <AppShell pageTitle="Billing" backHref="/">
        <div className="max-w-3xl mx-auto px-4 py-24 text-center text-muted">{error}</div>
      </AppShell>
    );
  }

  if (!data) {
    return (
      <AppShell pageTitle="Billing" backHref="/">
        <div className="max-w-3xl mx-auto px-4 py-8 space-y-6">
          {[1, 2, 3].map(i => (
            <Skeleton key={i} className="h-32 rounded-2xl" />
          ))}
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell
      pageTitle="Billing"
      subtitle="Manage your subscription, credits, and invoices"
      backHref="/"
    >
      <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">

        {/* 1. Subscription Status */}
        <motion.section
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm"
        >
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-6 mb-8">
            <div className="flex items-start gap-4">
              <div className="w-12 h-12 rounded-2xl bg-blue-100 flex items-center justify-center shrink-0 mt-1">
                <CreditCard className="w-6 h-6 text-blue-600" />
              </div>
              <div>
                <div className="flex items-center gap-2 mb-1">
                  <h2 className="text-xl font-black text-navy">{data.currentPlan}</h2>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-black uppercase tracking-widest bg-green-100 text-green-700">
                    <CheckCircle2 className="w-3 h-3" /> {data.status}
                  </span>
                </div>
                <div className="flex items-baseline gap-1">
                  <span className="text-2xl font-black text-navy">{data.price}</span>
                  <span className="text-sm font-medium text-muted">/{data.interval}</span>
                </div>
              </div>
            </div>
            <div className="bg-background-light rounded-2xl p-4 border border-gray-100 sm:text-right">
              <div className="flex items-center sm:justify-end gap-1.5 text-xs font-bold text-muted uppercase tracking-widest mb-1">
                <Calendar className="w-3.5 h-3.5" /> Next Renewal
              </div>
              <div className="text-base font-black text-navy">{data.nextRenewal}</div>
            </div>
          </div>
          <div className="flex flex-col sm:flex-row gap-3 pt-6 border-t border-gray-100">
            <button
              disabled
              title="Plan changes are coming soon"
              aria-label="Upgrade Plan (coming soon)"
              className="flex-1 flex items-center justify-center gap-2 px-4 py-3 bg-navy text-white text-sm font-bold rounded-xl opacity-50 cursor-not-allowed"
            >
              <ArrowUpCircle className="w-4 h-4" /> Upgrade Plan
            </button>
            <button
              disabled
              title="Plan changes are coming soon"
              aria-label="Downgrade Plan (coming soon)"
              className="flex-1 flex items-center justify-center gap-2 px-4 py-3 bg-white border border-gray-200 text-navy text-sm font-bold rounded-xl opacity-50 cursor-not-allowed"
            >
              <ArrowDownCircle className="w-4 h-4" /> Downgrade Plan
            </button>
          </div>
        </motion.section>

        {/* 2. Review Credits */}
        <motion.section
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1 }}
          className="bg-gradient-to-br from-indigo-50 to-purple-50 rounded-[32px] border border-indigo-100 p-6 sm:p-8 shadow-sm"
        >
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-6">
            <div className="flex items-start gap-4">
              <div className="w-12 h-12 rounded-2xl bg-indigo-600 flex items-center justify-center shrink-0 mt-1 shadow-md shadow-indigo-200">
                <Star className="w-6 h-6 text-white fill-white/20" />
              </div>
              <div>
                <h2 className="text-sm font-black text-indigo-900/60 uppercase tracking-widest mb-1">Available Credits</h2>
                <div className="flex items-baseline gap-2">
                  <span className="text-4xl font-black text-indigo-900">{data.reviewCredits}</span>
                  <span className="text-sm font-bold text-indigo-900/60">Expert Reviews</span>
                </div>
              </div>
            </div>
            <button
              disabled
              title="Credit purchases coming soon"
              aria-label="Purchase Extra Credits (coming soon)"
              className="w-full sm:w-auto flex items-center justify-center gap-2 px-6 py-3 bg-white border border-indigo-200 text-indigo-700 text-sm font-bold rounded-xl opacity-50 cursor-not-allowed shadow-sm"
            >
              <PlusCircle className="w-4 h-4" /> Purchase Extras
            </button>
          </div>
        </motion.section>

        {/* 3. Invoices */}
        <motion.section
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2 }}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4 px-2">Invoices</h2>
          {data.invoices.length > 0 ? (
            <div className="bg-surface rounded-[32px] border border-gray-200 shadow-sm overflow-hidden">
              <div className="divide-y divide-gray-100">
                {data.invoices.map((invoice) => (
                  <div key={invoice.id} className="flex items-center justify-between p-4 sm:p-6 hover:bg-background-light transition-colors">
                    <div className="flex items-center gap-4">
                      <div className="w-10 h-10 rounded-xl bg-gray-100 flex items-center justify-center shrink-0">
                        <FileText className="w-5 h-5 text-muted" />
                      </div>
                      <div>
                        <div className="text-sm font-bold text-navy">{invoice.date}</div>
                        <div className="text-xs font-medium text-muted">{invoice.amount} · {invoice.id}</div>
                      </div>
                    </div>
                    <div className="flex items-center gap-4">
                      <span className="hidden sm:inline-flex items-center px-2 py-0.5 rounded-md text-[10px] font-black uppercase tracking-widest bg-green-100 text-green-700">
                        {invoice.status}
                      </span>
                      <button className="p-2 text-muted hover:text-navy hover:bg-gray-100 rounded-lg transition-colors" aria-label="Download Invoice">
                        <Download className="w-5 h-5" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ) : (
            <div className="bg-surface rounded-[32px] border border-gray-200 p-8 text-center shadow-sm">
              <div className="w-12 h-12 rounded-full bg-background-light flex items-center justify-center mx-auto mb-3">
                <FileText className="w-6 h-6 text-muted" />
              </div>
              <h3 className="text-sm font-bold text-navy mb-1">No invoices yet</h3>
              <p className="text-xs text-muted">Your billing history will appear here once you have an active subscription.</p>
            </div>
          )}
        </motion.section>

      </div>
    </AppShell>
  );
}
