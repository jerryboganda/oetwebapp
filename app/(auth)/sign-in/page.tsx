'use client';

import { useSearchParams } from 'next/navigation';
import { AuthPageGate } from '@/components/auth/auth-page-gate';
import { SignInForm } from '@/components/auth/sign-in-form';
import { resolveWebsitePackageBySlug, resolveWebsitePackageByCode } from '@/lib/catalog-website-packages';

export default function SignInPage() {
  const searchParams = useSearchParams();
  const rawNext = searchParams?.get('next') ?? null;
  const initialEmail = searchParams?.get('email') ?? null;
  const externalError = searchParams?.get('externalError') ?? null;

  // Public website pricing-page CTAs deep-link a specific package via ?package=<slug>.
  // Resolve it to a known package and, when valid, route the learner straight to it on
  // /subscriptions after authentication (a validated internal path — no open redirect).
  const packageParam = searchParams?.get('package') ?? null;
  const deepLinkedPackage = packageParam
    ? resolveWebsitePackageBySlug(packageParam) ?? resolveWebsitePackageByCode(packageParam)
    : null;
  const nextHref = deepLinkedPackage ? `/subscriptions?package=${deepLinkedPackage.slug}` : rawNext;

  return (
    <AuthPageGate nextHref={nextHref}>
      <SignInForm nextHref={nextHref} initialEmail={initialEmail} externalError={externalError} />
    </AuthPageGate>
  );
}
