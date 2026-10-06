import type { Metadata } from 'next';
// The LiveKit stylesheet is imported by LearnerLiveRoomShell itself, so only a human live-tutor room loads it
// (it used to ride on every /speaking route, including AI exams that never mount LiveKit).

export const metadata: Metadata = {
  title: 'OET Speaking',
  description: 'OET Speaking practice: role-plays, structure guides, and AI conversation practice.',
};

export default function SegmentLayout({ children }: { children: React.ReactNode }) {
  return children;
}
