import type { Metadata } from 'next';
import { Suspense } from 'react';
import { MfaChallengePageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Two-factor authentication · OET with Dr Ahmed Hesham',
  robots: { index: false, follow: false },
};

export default function MfaChallengePage() {
  return (
    <Suspense>
      <MfaChallengePageContent />
    </Suspense>
  );
}