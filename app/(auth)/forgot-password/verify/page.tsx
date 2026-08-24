import type { Metadata } from 'next';
import { Suspense } from 'react';
import { ForgotPasswordVerifyPageContent } from './page-content';

export const dynamic = 'force-dynamic';

export const metadata: Metadata = {
  title: 'Verify reset code · OET with Dr Ahmed Hesham',
  description: 'Enter the one-time code sent to your email to continue resetting your password.',
};



export default function ForgotPasswordVerifyPage() {
  return (
    <Suspense>
      <ForgotPasswordVerifyPageContent />
    </Suspense>
  );
}