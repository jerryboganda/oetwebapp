'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Bot, CheckCircle2, ClipboardCheck, CreditCard, FileText, Headphones, Mic2, PackageCheck, ShoppingCart } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { useAuth } from '@/contexts/auth-context';
import { fetchAiPackages, fetchMyAiPackageCredits } from '@/lib/api';
import type { AiPackage, AiPackageCreditSnapshot, AiPackagesResponse } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';

type PackageTab = 'full' | 'separate' | 'mock';
type SeparateKey = 'listening' | 'reading' | 'writing' | 'speaking';

const SEPARATE_SECTIONS: Array<{ key: SeparateKey; label: string; icon: React.ReactNode }> = [
  { key: 'listening', label: 'Listening', icon: <Headphones className="h-4 w-4" /> },
  { key: 'reading', label: 'Reading', icon: <FileText className="h-4 w-4" /> },
  { key: 'writing', label: 'Writing', icon: <ClipboardCheck className="h-4 w-4" /> },
  { key: 'speaking', label: 'Speaking', icon: <Mic2 className="h-4 w-4" /> },
];

function formatAllowance(value: number | null, label: string) {
  return value === null ? `Unlimited ${label}` : `${value} ${label}`;
}

function formatDate(value?: string | null) {
  if (!value) return 'No active expiry';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'No active expiry' : parsed.toLocaleDateString();
}

function packageHeadline(pkg: AiPackage) {
  if (pkg.group === 'mock') return `${pkg.mocks} full mock${pkg.mocks === 1 ? '' : 's'}`;
  if (pkg.group === 'writing') return `${pkg.writingCredits} Writing credits`;
  if (pkg.group === 'speaking') return `${pkg.speakingCredits} Speaking credits`;
  if (pkg.credits > 0) return `${pkg.credits} flexible credits`;
  return 'Practice allowance';
}

export default function AiPackagesPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [activeTab, setActiveTab] = useState<PackageTab>('full');
  const [packages, setPackages] = useState<AiPackagesResponse | null>(null);
  const [credits, setCredits] = useState<AiPackageCreditSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyCode, setBusyCode] = useState<string | null>(null);
  const [message, setMessage] = useState<{ variant: 'success' | 'error' | 'info'; text: string } | null>(null);
  const submittingRef = useRef(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchAiPackages()
      .then((result) => {
        if (!cancelled) setPackages(result);
      })
      .catch((error) => {
        if (!cancelled) setMessage({ variant: 'error', text: error instanceof Error ? error.message : 'Could not load AI packages.' });
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!isAuthenticated) {
      setCredits(null);
      return;
    }

    let cancelled = false;
    fetchMyAiPackageCredits()
      .then((result) => {
        if (!cancelled) setCredits(result);
      })
      .catch(() => {
        if (!cancelled) setCredits(null);
      });
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated]);

  const visibleCount = useMemo(() => {
    if (!packages) return 0;
    return packages.full.length
      + packages.separate.listening.length
      + packages.separate.reading.length
      + packages.separate.writing.length
      + packages.separate.speaking.length
      + packages.mock.length;
  }, [packages]);

  const startCheckout = useCallback(async (pkg: AiPackage) => {
    if (authLoading) return;
    if (!isAuthenticated) {
      router.push(`/sign-in?next=${encodeURIComponent(`/ai-packages?package=${pkg.code}`)}`);
      return;
    }
    if (submittingRef.current) return;
    submittingRef.current = true;
    setBusyCode(pkg.code);
    setMessage(null);
    router.push(`/checkout/review?productType=addon_purchase&priceId=${encodeURIComponent(pkg.code)}&quantity=1`);
    setBusyCode(null);
    submittingRef.current = false;
  }, [authLoading, isAuthenticated, router]);

  useEffect(() => {
    const code = searchParams?.get('package');
    if (!code || !packages || authLoading || !isAuthenticated || submittingRef.current) return;
    const allPackages = [
      ...packages.full,
      ...packages.separate.listening,
      ...packages.separate.reading,
      ...packages.separate.writing,
      ...packages.separate.speaking,
      ...packages.mock,
    ];
    const selected = allPackages.find((pkg) => pkg.code === code);
    if (selected) {
      void startCheckout(selected);
    }
  }, [authLoading, isAuthenticated, packages, searchParams, startCheckout]);

  const renderCard = (pkg: AiPackage) => (
    <article key={pkg.code} className="flex min-h-[320px] flex-col rounded-lg border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-navy">{pkg.name}</h2>
          <p className="mt-1 text-sm font-medium text-primary">{packageHeadline(pkg)}</p>
        </div>
        {pkg.priorityQueue ? <span className="rounded-full bg-amber-100 px-2 py-1 text-xs font-semibold text-amber-800">Priority</span> : null}
      </div>
      <p className="mt-3 text-sm leading-6 text-muted">{pkg.description}</p>
      <p className="mt-4 text-3xl font-semibold text-navy">{formatMoney(pkg.price, { currency: pkg.currency })}</p>
      <ul className="mt-4 flex-1 space-y-2 text-sm text-navy">
        {pkg.features.map((feature) => (
          <li key={feature} className="flex gap-2">
            <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
            <span>{feature}</span>
          </li>
        ))}
      </ul>
      <Button className="mt-5" fullWidth loading={busyCode === pkg.code} onClick={() => startCheckout(pkg)}>
        <ShoppingCart className="h-4 w-4" />
        Buy now
      </Button>
    </article>
  );

  return (
    <main className="min-h-screen bg-background text-navy">
      <section className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8 sm:px-6 lg:px-8">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <p className="flex items-center gap-2 text-sm font-semibold uppercase text-primary">
                <Bot className="h-4 w-4" />
                AI Packages
              </p>
              <h1 className="mt-2 text-3xl font-semibold tracking-tight text-navy sm:text-4xl">Choose your OET AI package</h1>
              <p className="mt-3 max-w-3xl text-sm leading-6 text-muted">
                Full packages, separate subtest packages, and mock packages are sold as one-time GBP purchases.
              </p>
            </div>
            <Link className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:text-primary-dark" href="/billing?tab=ai-credits">
              <CreditCard className="h-4 w-4" />
              Billing dashboard
            </Link>
          </div>

          {credits ? (
            <div className="grid gap-3 rounded-lg border border-border bg-background-light p-4 text-sm md:grid-cols-4">
              <div><span className="text-muted">Flexible</span><p className="font-semibold">{credits.flexibleCredits}</p></div>
              <div><span className="text-muted">Writing / Speaking</span><p className="font-semibold">{credits.writingOnlyCredits} / {credits.speakingOnlyCredits}</p></div>
              <div><span className="text-muted">Listening / Reading</span><p className="font-semibold">{formatAllowance(credits.listeningTestsRemaining, 'L')} / {formatAllowance(credits.readingTestsRemaining, 'R')}</p></div>
              <div><span className="text-muted">Mocks / Expiry</span><p className="font-semibold">{credits.mockExamsRemaining} / {formatDate(credits.expiresAt)}</p></div>
            </div>
          ) : null}

          {message ? <InlineAlert variant={message.variant}>{message.text}</InlineAlert> : null}
        </div>
      </section>

      <section className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        <Tabs
          tabs={[
            { id: 'full', label: 'Full Packages', icon: <PackageCheck className="h-4 w-4" /> },
            { id: 'separate', label: 'Separate Packages', icon: <ClipboardCheck className="h-4 w-4" /> },
            { id: 'mock', label: 'Mock Packages', icon: <Bot className="h-4 w-4" /> },
          ]}
          activeTab={activeTab}
          onChange={(tab: string) => setActiveTab(tab as PackageTab)}
        />

        {loading ? (
          <div className="mt-6 grid gap-4 lg:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-80 rounded-lg" />)}
          </div>
        ) : visibleCount === 0 ? (
          <InlineAlert className="mt-6" variant="info">No AI package catalogue rows are active yet.</InlineAlert>
        ) : (
          <>
            <TabPanel id="full" activeTab={activeTab}>
              <div className="mt-6 grid gap-4 lg:grid-cols-3">{packages?.full.map(renderCard)}</div>
            </TabPanel>
            <TabPanel id="separate" activeTab={activeTab}>
              <div className="mt-6 space-y-8">
                {SEPARATE_SECTIONS.map((section) => (
                  <section key={section.key}>
                    <h2 className="flex items-center gap-2 text-xl font-semibold text-navy">{section.icon}{section.label}</h2>
                    <div className="mt-3 grid gap-4 lg:grid-cols-3">{packages?.separate[section.key].map(renderCard)}</div>
                  </section>
                ))}
              </div>
            </TabPanel>
            <TabPanel id="mock" activeTab={activeTab}>
              <div className="mt-6 grid gap-4 lg:grid-cols-3">{packages?.mock.map(renderCard)}</div>
            </TabPanel>
          </>
        )}
      </section>
    </main>
  );
}
