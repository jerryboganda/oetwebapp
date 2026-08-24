import type { Metadata } from 'next';
import { Suspense } from 'react';
import { SignInPageContent } from './page-content';

export const metadata: Metadata = {
  title: 'Sign in · OET with Dr Ahmed Hesham',
  description: 'Sign in to your OET with Dr Ahmed Hesham account to continue listening, reading, writing, and speaking practice.',
};

export default function SignInPage() {
  return (
    <Suspense>
      <SignInPageContent />
    </Suspense>
  );
}