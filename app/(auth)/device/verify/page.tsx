import type { Metadata } from 'next';
import { Suspense } from 'react';
import { DeviceVerifyPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Verify this device · OET with Dr Ahmed Hesham',
  robots: { index: false, follow: false },
};

export default function DeviceVerifyPage() {
  return (
    <Suspense>
      <DeviceVerifyPageContent />
    </Suspense>
  );
}