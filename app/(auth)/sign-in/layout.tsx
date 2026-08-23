import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Sign in',
  description: 'Sign in to your OET with Dr Hesham account to continue your preparation.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}