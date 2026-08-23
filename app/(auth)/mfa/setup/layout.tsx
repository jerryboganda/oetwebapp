import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Set up two-factor auth',
  description: 'Secure your account with two-factor authentication.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}