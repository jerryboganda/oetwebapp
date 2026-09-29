import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'OET Writing',
  description: 'OET Writing practice: referral letters, case notes, and expert marking.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}