import type { Metadata } from 'next';
import { Suspense } from 'react';
import { MfaSetupPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Set up authenticator · OET with Dr Ahmed Hesham',
  robots: { index: false, follow: false },
};

export default function MfaSetupPage() {
  return (
    <Suspense>
      <MfaSetupPageContent />
    </Suspense>
  );
}