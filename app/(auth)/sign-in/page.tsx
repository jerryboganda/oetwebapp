'use client';

import { useSearchParams } from 'next/navigation';
import { AuthPageGate } from '@/components/auth/auth-page-gate';
import { SignInForm } from '@/components/auth/sign-in-form';

export default function SignInPage() {
  const searchParams = useSearchParams();
  const nextHref = searchParams.get('next');
  const initialEmail = searchParams.get('email');
  const externalError = searchParams.get('externalError');

  return (
    <AuthPageGate nextHref={nextHref}>
      <SignInForm nextHref={nextHref} initialEmail={initialEmail} externalError={externalError} />
    </AuthPageGate>
  );
}
