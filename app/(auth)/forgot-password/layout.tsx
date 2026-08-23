import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Forgot password',
  description: 'Reset your OET with Dr Hesham account password.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}