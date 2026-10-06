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
import { fetchAiPackages, fetchMyAiPackageCredits, fetchPublicCatalog } from '@/lib/api';
import type { AiPackage, AiPackageCreditSnapshot, AiPackagesResponse } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';
import { useAddToCart } from '@/lib/cart/use-add-to-cart';
import { useRevalidateOnResume } from '@/hooks/use-revalidate-on-resume';
import { CartNavButton } from '@/components/cart';
import type { CatalogPresentation } from '@/lib/catalog-presentation';
import {
  resolveWebsitePackageByCode,
  resolveWebsitePackageBySlug,
  resolveWebsitePackageWithOverlay,
  resolveWebsiteSections,
  websitePackageNumber,
  SEPARATE_AI_PACKAGES_GROUP,
  type WebsitePackage,
  type WebsiteSectionKey,
} from '@/lib/catalog-website-packages';

type PackageTab = 'full' | 'separate' | 'mock';
type SeparateKey = 'listening' | 'reading' | 'writing' | 'speaking';

// `sectionKey` is the website section whose admin-edited title replaces `label`.
const SEPARATE_SECTIONS: Array<{
  key: SeparateKey;
  sectionKey: WebsiteSectionKey;
  label: string;
  icon: React.ReactNode;
}> = [
  { key: 'listening', sectionKey: 'listening', label: 'Separate Listening Packages', icon: <Headphones className="h-4 w-4" /> },
  { key: 'reading', sectionKey: 'reading', label: 'Separate Reading Packages', icon: <FileText className="h-4 w-4" /> },
  { key: 'writing', sectionKey: 'writing-ai', label: 'Separate Writing Packages', icon: <ClipboardCheck className="h-4 w-4" /> },
  { key: 'speaking', sectionKey: 'speaking-ai', label: 'Separate Speaking Packages', icon: <Mic2 className="h-4 w-4" /> },
];

function formatAllowance(
  unlimited: boolean | undefined,
  value: number | null | undefined,
  label: string,
) {
  return unlimited ? `Unlimited ${label}` : `${value ?? 0} ${label}`;
}

function formatDate(value?: string | null) {
  if (!value) return 'No active expiry';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'No active expiry' : parsed.toLocaleDateString();
}

// Static package: decides which rows belong on this page and in what order.
function canonicalAiPackage(pkg: AiPackage): WebsitePackage | undefined {
  const websitePackage = resolveWebsitePackageByCode(pkg.code);
  return websitePackage && websitePackage.packageNo >= 30 && websitePackage.packageNo <= 50
    ? websitePackage
    : undefined;
}

// The same package with the admin overlay applied: the copy actually shown.
function displayAiPackage(pkg: AiPackage, presentation?: CatalogPresentation | null): WebsitePackage | undefined {
  const websitePackage = canonicalAiPackage(pkg);
  return websitePackage ? resolveWebsitePackageWithOverlay(websitePackage, presentation) : undefined;
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
  const [presentation, setPresentation] = useState<CatalogPresentation | null>(null);
  const submittingRef = useRef(false);
  // Read at click time so a late presentation reply never changes startCheckout's identity,
  // which would re-run the ?package= auto-checkout effect below.
  const presentationRef = useRef<CatalogPresentation | null>(null);
  // The ?package= code already added to the cart, so a refetch of the rows never adds it again.
  const autoAddedCodeRef = useRef<string | null>(null);
  const packagesRequestRef = useRef(0);
  const hasPackagesRef = useRef(false);

  useEffect(() => {
    presentationRef.current = presentation;
  }, [presentation]);

  // Admin package copy rides on the public catalogue. It never blocks the page: until it
  // arrives (or if it cannot be loaded) the static package copy is shown, and a failed refresh
  // keeps the copy already loaded.
  const loadPresentation = useCallback(async () => {
    try {
      const catalog = await fetchPublicCatalog();
      setPresentation(catalog.presentation ?? null);
    } catch {
      // Keep the static defaults or the copy already loaded.
    }
  }, []);

  useEffect(() => {
    void loadPresentation();
  }, [loadPresentation]);

  // First load and the silent refresh on resume: `loading` never flips back to true and a
  // failed refresh keeps the rows already on screen. A newer request supersedes an older one.
  const loadPackages = useCallback(async () => {
    packagesRequestRef.current += 1;
    const requestId = packagesRequestRef.current;
    try {
      const result = await fetchAiPackages();
      if (requestId !== packagesRequestRef.current) return;
      hasPackagesRef.current = true;
      setPackages(result);
      setMessage((current) => (current?.variant === 'error' ? null : current));
    } catch (error) {
      if (requestId !== packagesRequestRef.current) return;
      if (!hasPackagesRef.current) {
        setMessage({ variant: 'error', text: error instanceof Error ? error.message : 'Could not load AI packages.' });
      }
    } finally {
      if (requestId === packagesRequestRef.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadPackages();
    return () => {
      // Drop the reply of any request still in flight once this page is gone.
      packagesRequestRef.current += 1;
    };
  }, [loadPackages]);

  useRevalidateOnResume(() => {
    void loadPresentation();
    void loadPackages();
  });

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

  // Tab and sub-section headings follow the admin's section title overrides.
  const sections = useMemo(() => resolveWebsiteSections(presentation?.websitePackages?.sections), [presentation]);
  const sectionTitle = (key: WebsiteSectionKey, fallback: string) =>
    sections.find((section) => section.key === key)?.title ?? fallback;

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
      name: displayAiPackage(pkg, presentationRef.current)?.name ?? pkg.name,
      price: pkg.price,
      currency: pkg.currency,
    });
    setBusyCode(null);
    submittingRef.current = false;
  }, [authLoading, isAuthenticated, router, addToCart]);

  useEffect(() => {
    const code = searchParams?.get('package');
    if (!code || !visiblePackages || authLoading || !isAuthenticated || submittingRef.current) return;
    if (autoAddedCodeRef.current === code) return;
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
      autoAddedCodeRef.current = code;
      void startCheckout(selected);
    }
  }, [authLoading, isAuthenticated, searchParams, startCheckout, visiblePackages]);

  const renderCard = (pkg: AiPackage) => {
    const websitePackage = displayAiPackage(pkg, presentation);
    if (!websitePackage) return null;
    const packageNo = websitePackageNumber(websitePackage);
    return (
    <article key={pkg.code} className="flex min-h-[320px] flex-col rounded-lg border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div>
          {packageNo != null ? (
            <p className="text-2xs font-bold uppercase tracking-[0.14em] text-muted">
              Package {packageNo}
            </p>
          ) : null}
          <h2 className="mt-1 text-lg font-semibold text-navy">{websitePackage.name}</h2>
        </div>
        <div className="flex flex-wrap justify-end gap-1.5">
          {websitePackage.badges.map((badge, badgeIndex) => (
            <span key={`${badgeIndex}-${badge}`} className="rounded-full bg-primary/10 px-2 py-1 text-xs font-semibold text-primary">
              {badge}
            </span>
          ))}
        </div>
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">
        {websitePackage.metaChips.map((chip, chipIndex) => (
          <span key={`${chipIndex}-${chip}`} className="rounded-full bg-background-light px-2.5 py-0.5 text-2xs font-semibold text-muted">
            {chip}
          </span>
        ))}
      </div>
      <p className="mt-2 text-xs text-muted">
        <span className="font-semibold text-navy">Category:</span> {websitePackage.category}
      </p>
      <p className="mt-3 whitespace-pre-line text-sm leading-6 text-muted">{websitePackage.description}</p>
      <p className="mt-3 text-sm text-muted">
        <span className="font-semibold text-navy">Format:</span> {websitePackage.formatLine}
      </p>
      <p className="mt-4 text-3xl font-semibold text-navy">{formatMoney(pkg.price, { currency: pkg.currency })}</p>
      <ul className="mt-4 flex-1 space-y-2 text-sm text-navy">
        {websitePackage.features.map((feature, featureIndex) => (
          <li key={`${featureIndex}-${feature}`} className="flex gap-2">
            <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success-strong" />
            <span>{feature}</span>
          </li>
        ))}
      </ul>
      <p className="mt-4 whitespace-pre-line rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy">
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
            <div className="grid gap-3 rounded-lg border border-border bg-background-light p-4 text-sm md:grid-cols-5">
              <div><span className="text-muted">Shared</span><p className="font-semibold">{credits.sharedCredits ?? 0}</p></div>
              <div><span className="text-muted">Flexible W/S</span><p className="font-semibold">{credits.flexibleCredits}</p></div>
              <div><span className="text-muted">Writing / Speaking</span><p className="font-semibold">{credits.writingUnlimited ? 'Unlimited' : credits.writingOnlyCredits} / {credits.speakingUnlimited ? 'Unlimited' : credits.speakingOnlyCredits}</p></div>
              <div><span className="text-muted">Listening / Reading</span><p className="font-semibold">{formatAllowance(credits.listeningUnlimited, credits.listeningTestsRemaining, 'L')} / {formatAllowance(credits.readingUnlimited, credits.readingTestsRemaining, 'R')}</p></div>
              <div><span className="text-muted">Mocks / Expiry</span><p className="font-semibold">{credits.mockExamsRemaining} / {formatDate(credits.expiresAt)}</p></div>
            </div>
          ) : null}

          {message ? <InlineAlert variant={message.variant}>{message.text}</InlineAlert> : null}
        </div>
      </section>

      <section className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        <Tabs
          tabs={[
            { id: 'full', label: sectionTitle('ai', 'AI Grading Packages'), icon: <PackageCheck className="h-4 w-4" /> },
            { id: 'separate', label: 'Separate Packages', icon: <ClipboardCheck className="h-4 w-4" /> },
            { id: 'mock', label: sectionTitle('mock', 'Full Mock Exam Packages'), icon: <Bot className="h-4 w-4" /> },
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
                <div>
                  <h2 className="text-2xl font-semibold tracking-tight text-navy">
                    {SEPARATE_AI_PACKAGES_GROUP.title}
                  </h2>
                  <p className="mt-1 text-sm text-muted">{SEPARATE_AI_PACKAGES_GROUP.description}</p>
                </div>
                {SEPARATE_SECTIONS.map((section) => (
                  <section key={section.key}>
                    <h2 className="flex items-center gap-2 text-xl font-semibold text-navy">{section.icon}{sectionTitle(section.sectionKey, section.label)}</h2>
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
