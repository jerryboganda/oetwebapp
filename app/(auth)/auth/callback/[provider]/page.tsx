import type { Metadata } from 'next';
import { Suspense } from 'react';
import { ExternalAuthCallbackPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Signing you in · OET with Dr Ahmed Hesham',
  robots: { index: false, follow: false },
};

export default function ExternalAuthCallbackPage() {
  return (
    <Suspense>
      <ExternalAuthCallbackPageContent />
    </Suspense>
  );
}