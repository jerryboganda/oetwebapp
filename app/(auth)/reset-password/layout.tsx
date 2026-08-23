import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Reset password',
  description: 'Choose a new password for your OET with Dr Hesham account.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}