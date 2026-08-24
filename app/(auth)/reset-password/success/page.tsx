import type { Metadata } from 'next';
import { Suspense } from 'react';
import { ResetPasswordSuccessPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Password reset · OET with Dr Ahmed Hesham',
  description: 'Your password has been updated. Sign in to continue practising.',
};

export default function ResetPasswordSuccessPage() {
  return (
    <Suspense>
      <ResetPasswordSuccessPageContent />
    </Suspense>
  );
}