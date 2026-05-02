import { render, screen } from '@testing-library/react';
import { PronunciationDashboardTile } from '@/components/domain/pronunciation/PronunciationDashboardTile';

describe('PronunciationDashboardTile', () => {
  it('routes learners to Recalls audio instead of the standalone pronunciation surface', () => {
    render(<PronunciationDashboardTile />);

    expect(screen.getByRole('link', { name: /open recalls audio practice/i })).toHaveAttribute('href', '/recalls/words');
    expect(screen.getByText(/recalls audio/i)).toBeInTheDocument();
    expect(screen.getByText(/click any vocabulary word/i)).toBeInTheDocument();
    expect(screen.queryByText(/^Pronunciation$/i)).not.toBeInTheDocument();
  });
});
