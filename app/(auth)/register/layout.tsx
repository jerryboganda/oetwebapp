import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Create your account',
  description: 'Register for OET preparation with Dr Hesham — courses, mocks, and expert marking.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}