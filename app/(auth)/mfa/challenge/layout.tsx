import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Two-factor verification',
  description: 'Verify your identity with your authenticator app.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}