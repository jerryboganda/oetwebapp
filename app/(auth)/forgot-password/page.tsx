import type { Metadata } from 'next';
import { Suspense } from 'react';
import { ForgotPasswordPageContent } from './page-content';

export const dynamic = 'force-dynamic';

export const metadata: Metadata = {
  title: 'Forgot password · OET with Dr Ahmed Hesham',
  description: 'Reset your OET with Dr Ahmed Hesham password using a one-time verification code.',
};



export default function ForgotPasswordPage() {
  return (
    <Suspense>
      <ForgotPasswordPageContent />
    </Suspense>
  );
}