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
import { useAddToCart } from '@/lib/cart/use-add-to-cart';
import { CartNavButton } from '@/components/cart';
import {
  resolveWebsitePackageByCode,
  resolveWebsitePackageBySlug,
  type WebsitePackage,
} from '@/lib/catalog-website-packages';

type PackageTab = 'full' | 'separate' | 'mock';
type SeparateKey = 'listening' | 'reading' | 'writing' | 'speaking';

const SEPARATE_SECTIONS: Array<{ key: SeparateKey; label: string; icon: React.ReactNode }> = [
  { key: 'listening', label: 'Separate Listening Packages', icon: <Headphones className="h-4 w-4" /> },
  { key: 'reading', label: 'Separate Reading Packages', icon: <FileText className="h-4 w-4" /> },
  { key: 'writing', label: 'Separate Writing Packages', icon: <ClipboardCheck className="h-4 w-4" /> },
  { key: 'speaking', label: 'Separate Speaking Packages', icon: <Mic2 className="h-4 w-4" /> },
];

function formatAllowance(value: number | null, label: string) {
  return value === null ? `Unlimited ${label}` : `${value} ${label}`;
}

function formatDate(value?: string | null) {
  if (!value) return 'No active expiry';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'No active expiry' : parsed.toLocaleDateString();
}

function canonicalAiPackage(pkg: AiPackage): WebsitePackage | undefined {
  const websitePackage = resolveWebsitePackageByCode(pkg.code);
  return websitePackage && websitePackage.packageNo >= 30 && websitePackage.packageNo <= 47
    ? websitePackage
    : undefined;
}

function canonicalAiRows(rows: AiPackage[]): AiPackage[] {
  return rows
    .filter((pkg) => canonicalAiPackage(pkg) != null)
    .sort(
      (left, right) =>
        (canonicalAiPackage(left)?.packageNo ?? Number.MAX_SAFE_INTEGER) -
        (canonicalAiPackage(right)?.packageNo ?? Number.MAX_SAFE_INTEGER),
    );
}

export default function AiPackagesPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { isAuthenticated, loading: authLoading } = useAuth();
  const { addToCart } = useAddToCart();
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

  const visiblePackages = useMemo<AiPackagesResponse | null>(() => {
    if (!packages) return null;
    return {
      ...packages,
      full: canonicalAiRows(packages.full),
      separate: {
        listening: canonicalAiRows(packages.separate.listening),
        reading: canonicalAiRows(packages.separate.reading),
        writing: canonicalAiRows(packages.separate.writing),
        speaking: canonicalAiRows(packages.separate.speaking),
      },
      mock: canonicalAiRows(packages.mock),
    };
  }, [packages]);

  const visibleCount = useMemo(() => {
    if (!visiblePackages) return 0;
    return visiblePackages.full.length
      + visiblePackages.separate.listening.length
      + visiblePackages.separate.reading.length
      + visiblePackages.separate.writing.length
      + visiblePackages.separate.speaking.length
      + visiblePackages.mock.length;
  }, [visiblePackages]);

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
    addToCart({
      code: pkg.code,
      kind: 'addon',
      name: canonicalAiPackage(pkg)?.name ?? pkg.name,
      price: pkg.price,
      currency: pkg.currency,
    });
    setBusyCode(null);
    submittingRef.current = false;
  }, [authLoading, isAuthenticated, router, addToCart]);

  useEffect(() => {
    const code = searchParams?.get('package');
    if (!code || !visiblePackages || authLoading || !isAuthenticated || submittingRef.current) return;
    const allPackages = [
      ...visiblePackages.full,
      ...visiblePackages.separate.listening,
      ...visiblePackages.separate.reading,
      ...visiblePackages.separate.writing,
      ...visiblePackages.separate.speaking,
      ...visiblePackages.mock,
    ];
    const requestedPackage = resolveWebsitePackageBySlug(code) ?? resolveWebsitePackageByCode(code);
    const selected = allPackages.find(
      (pkg) => (resolveWebsitePackageByCode(pkg.code)?.code ?? pkg.code) === (requestedPackage?.code ?? code),
    );
    if (selected) {
      void startCheckout(selected);
    }
  }, [authLoading, isAuthenticated, searchParams, startCheckout, visiblePackages]);

  const renderCard = (pkg: AiPackage) => {
    const websitePackage = canonicalAiPackage(pkg);
    if (!websitePackage) return null;
    return (
    <article key={pkg.code} className="flex min-h-[320px] flex-col rounded-lg border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">
            Package {websitePackage.packageNo}
          </p>
          <h2 className="mt-1 text-lg font-semibold text-navy">{websitePackage.name}</h2>
        </div>
        <div className="flex flex-wrap justify-end gap-1.5">
          {websitePackage.badges.map((badge) => (
            <span key={badge} className="rounded-full bg-primary/10 px-2 py-1 text-xs font-semibold text-primary">
              {badge}
            </span>
          ))}
        </div>
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">
        {websitePackage.metaChips.map((chip) => (
          <span key={chip} className="rounded-full bg-background-light px-2.5 py-0.5 text-[11px] font-semibold text-muted">
            {chip}
          </span>
        ))}
      </div>
      <p className="mt-2 text-xs text-muted">
        <span className="font-semibold text-navy">Category:</span> {websitePackage.category}
      </p>
      <p className="mt-3 text-sm leading-6 text-muted">{websitePackage.description}</p>
      <p className="mt-3 text-sm text-muted">
        <span className="font-semibold text-navy">Format:</span> {websitePackage.formatLine}
      </p>
      <p className="mt-4 text-3xl font-semibold text-navy">{formatMoney(pkg.price, { currency: pkg.currency })}</p>
      <ul className="mt-4 flex-1 space-y-2 text-sm text-navy">
        {websitePackage.features.map((feature) => (
          <li key={feature} className="flex gap-2">
            <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
            <span>{feature}</span>
          </li>
        ))}
      </ul>
      <p className="mt-4 rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy">
        <span className="font-bold">Best for:</span> {websitePackage.bestFor}
      </p>
      <Button className="mt-5" fullWidth loading={busyCode === pkg.code} onClick={() => startCheckout(pkg)}>
        <ShoppingCart className="h-4 w-4" />
        Add to cart
      </Button>
    </article>
    );
  };

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
            <div className="flex items-center gap-3">
              <Link className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:text-primary-dark" href="/billing">
                <CreditCard className="h-4 w-4" />
                Billing dashboard
              </Link>
              <CartNavButton />
            </div>
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
            { id: 'full', label: 'AI Grading Packages', icon: <PackageCheck className="h-4 w-4" /> },
            { id: 'mock', label: 'Full Mock Exam Packages', icon: <Bot className="h-4 w-4" /> },
            { id: 'separate', label: 'Separate Packages', icon: <ClipboardCheck className="h-4 w-4" /> },
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
              <div className="mt-6 grid gap-4 lg:grid-cols-3">{visiblePackages?.full.map(renderCard)}</div>
            </TabPanel>
            <TabPanel id="separate" activeTab={activeTab}>
              <div className="mt-6 space-y-5 sm:space-y-8">
                {SEPARATE_SECTIONS.map((section) => (
                  <section key={section.key}>
                    <h2 className="flex items-center gap-2 text-xl font-semibold text-navy">{section.icon}{section.label}</h2>
                    <div className="mt-3 grid gap-4 lg:grid-cols-3">{visiblePackages?.separate[section.key].map(renderCard)}</div>
                  </section>
                ))}
              </div>
            </TabPanel>
            <TabPanel id="mock" activeTab={activeTab}>
              <div className="mt-6 grid gap-4 lg:grid-cols-3">{visiblePackages?.mock.map(renderCard)}</div>
            </TabPanel>
          </>
        )}
      </section>
    </main>
  );
}
