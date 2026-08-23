import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'OET Reading',
  description: 'OET Reading practice: Parts A, B and C, timed papers, and instant scoring.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}