import type { Metadata } from 'next';
import { Suspense } from 'react';
import { RegisterSuccessPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Account created · OET with Dr Ahmed Hesham',
  description: 'Your OET with Dr Ahmed Hesham account is ready. Continue to verify your email and start practising.',
};

export default function RegisterSuccessPage() {
  return (
    <Suspense>
      <RegisterSuccessPageContent />
    </Suspense>
  );
}