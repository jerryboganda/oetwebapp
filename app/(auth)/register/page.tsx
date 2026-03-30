'use client';

import { AuthPageGate } from '@/components/auth/auth-page-gate';
import { RegisterForm } from '@/components/auth/register/register-original-form';

export default function RegisterPage() {
  return (
    <AuthPageGate>
      <RegisterForm />
    </AuthPageGate>
  );
}
