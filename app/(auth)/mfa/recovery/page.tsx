import type { Metadata } from 'next';
import { Suspense } from 'react';
import { MfaRecoveryPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Recovery code · OET with Dr Ahmed Hesham',
  robots: { index: false, follow: false },
};

export default function MfaRecoveryPage() {
  return (
    <Suspense>
      <MfaRecoveryPageContent />
    </Suspense>
  );
}