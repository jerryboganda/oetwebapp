import type { Metadata } from 'next';
import { Suspense } from 'react';
import { ResetPasswordPageContent } from './page-content';

export const dynamic = 'force-dynamic';

export const metadata: Metadata = {
  title: 'Reset password · OET with Dr Ahmed Hesham',
  description: 'Choose a new password for your OET with Dr Ahmed Hesham account.',
};



export default function ResetPasswordPage() {
  return (
    <Suspense>
      <ResetPasswordPageContent />
    </Suspense>
  );
}