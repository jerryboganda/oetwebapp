// Authored preview — EmptyState. Each named export = one labeled card cell.
import { EmptyState, Button } from 'oet-prep';

export const NoMockExams = () => (
  <div style={{ maxWidth: 520 }}>
    <EmptyState
      title="No mock exams yet"
      description="You haven't started any full OET mock exams. Sit a timed paper to see how you'd score on the real test."
      action={{ label: 'Browse mock exams', onClick: () => {} }}
    />
  </div>
);

export const WithIcon = () => (
  <div style={{ maxWidth: 520 }}>
    <EmptyState
      icon={
        <svg width="32" height="32" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 4H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V18a2 2 0 01-2 2z" />
        </svg>
      }
      title="No saved Writing tasks"
      description="Drafts of your referral letters and case notes will appear here once you start a Writing task."
      action={{ label: 'Start a Writing task', onClick: () => {} }}
    />
  </div>
);

export const NoActionNoIcon = () => (
  <div style={{ maxWidth: 520 }}>
    <EmptyState
      title="No results in this date range"
      description="Try widening the date filter to see your earlier Listening and Reading attempts."
    />
  </div>
);
