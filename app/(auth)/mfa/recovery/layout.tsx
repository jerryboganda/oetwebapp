import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Recovery codes',
  description: 'Sign in with an MFA recovery code.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}