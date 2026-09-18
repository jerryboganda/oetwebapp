'use client';

import { useSearchParams } from 'next/navigation';
import { AuthPageGate } from '@/components/auth/auth-page-gate';
import { RegisterForm } from '@/components/auth/register/register-original-form';
import { RegisterPlacementForm } from '@/components/auth/register/register-placement-form';

export default function RegisterPage() {
  const searchParams = useSearchParams();
  // Minimal signup for the free placement test: identity + consent only —
  // healthcare-enrollment fields are deferred until the learner enrolls.
  const isPlacementSignup = searchParams?.get('purpose') === 'placement';

  return (
    <AuthPageGate>
      {isPlacementSignup ? <RegisterPlacementForm /> : <RegisterForm />}
    </AuthPageGate>
  );
}
