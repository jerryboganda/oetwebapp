import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'OET Listening',
  description: 'OET Listening practice: consultations, lectures, strategies, and scored mock tests.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}