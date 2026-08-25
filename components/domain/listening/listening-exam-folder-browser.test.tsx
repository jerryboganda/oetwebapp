import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListeningExamFolderBrowser } from './listening-exam-folder-browser';

const PAPERS = [
  { id: 'st9', slug: 'atlas-practice-series-listening-sample-test-09', title: 'Atlas Sample Test 9' },
  { id: 'nova20', slug: 'nova-practice-series-listening-20', title: 'Nova Practice Test 20' },
  { id: 'jayden', slug: 'jayden-book-01-bed-bugs', title: 'Jayden should not appear' },
  { id: 'legacy', slug: 'listening-sample-1', title: 'Listening Sample 1' },
];

describe('ListeningExamFolderBrowser', () => {
  it('shows only Atlas and Nova Listening folders before any paper', () => {
    render(
      <ListeningExamFolderBrowser
        papers={PAPERS}
        emptyMessage="No papers in this series yet."
        renderPaper={(paper) => <p>{paper.title}</p>}
      />,
    );

    expect(screen.getByTestId('listening-exam-folders')).toBeInTheDocument();
    expect(screen.getByTestId('listening-exam-folder-atlas-practice-series')).toBeInTheDocument();
    expect(screen.getByTestId('listening-exam-folder-nova-practice-series')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /atlas practice series/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /nova practice series/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /anna hartford/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /jayden book/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /very difficult/i })).not.toBeInTheDocument();
    expect(screen.queryByText('Atlas Sample Test 9')).not.toBeInTheDocument();
    expect(screen.queryByText('Nova Practice Test 20')).not.toBeInTheDocument();
    expect(screen.queryByText('Jayden should not appear')).not.toBeInTheDocument();
  });

  it('opens a folder to the papers that belong there and can return to the list', async () => {
    const user = userEvent.setup();
    render(
      <ListeningExamFolderBrowser
        papers={PAPERS}
        emptyMessage="No papers in this series yet."
        renderPaper={(paper) => <p>{paper.title}</p>}
      />,
    );

    await user.click(screen.getByRole('button', { name: /atlas practice series/i }));
    expect(screen.getByRole('heading', { name: 'Atlas Practice Series' })).toBeInTheDocument();
    expect(screen.getByText('Atlas Sample Test 9')).toBeInTheDocument();
    expect(screen.queryByText('Nova Practice Test 20')).not.toBeInTheDocument();
    expect(screen.queryByText('Jayden should not appear')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /back to folders/i }));
    expect(screen.getByRole('button', { name: /nova practice series/i })).toBeInTheDocument();
    expect(screen.queryByText('Atlas Sample Test 9')).not.toBeInTheDocument();
  });
});
