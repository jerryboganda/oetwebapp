import type { Metadata } from 'next';
import ReviewPage from '../review/page';

export const metadata: Metadata = {
  title: 'Reviews — OET Prep',
  description: 'Track expert review requests and returned feedback across your attempts.',
};

// `/app/reviews` is the blueprint-canonical learner reviews route. It renders
// the same experience as `/app/review` so both URLs resolve to the learner
// review surface.
export default function ReviewsPage() {
  return <ReviewPage />;
}
