import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'OET Speaking',
  description: 'OET Speaking practice: role-plays, structure guides, and AI conversation practice.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}