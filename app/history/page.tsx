import type { Metadata } from 'next';
import SubmissionsPage from '../submissions/page';

export const metadata: Metadata = {
  title: 'History — OET Prep',
  description: 'Review every attempt you have made across Writing, Speaking, Reading, and Listening.',
};

// `/app/history` is the blueprint-canonical learner history route. It renders
// the same experience as `/app/submissions` so both URLs resolve to the
// attempt history surface. The submissions route remains the in-code home and
// is linked from existing flows; `/history` is kept as the named entry point
// described in the learner blueprint.
export default function HistoryPage() {
  return <SubmissionsPage />;
}
