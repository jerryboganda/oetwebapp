import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ReadingExamFolderBrowser } from './reading-exam-folder-browser';

const PAPERS = [
  { id: 'ah1', slug: 'anna-hartford-01-cigarette-smoking-lung-cancer', title: 'Anna Hartford 1' },
  { id: 'jb1', slug: 'jayden-book-01-bed-bugs', title: 'Jayden Book 01 — Bed Bugs' },
  { id: 'legacy', slug: 'reading-sample-1', title: 'Reading Sample 1' },
];

describe('ReadingExamFolderBrowser', () => {
  it('shows the official Reading materials folders before any paper', () => {
    render(
      <ReadingExamFolderBrowser
        papers={PAPERS}
        emptyMessage="No papers in this series yet."
        renderPaper={(paper) => <p>{paper.title}</p>}
      />,
    );

    expect(screen.getByTestId('reading-exam-folder-anna-hartford')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /anna hartford/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /atlas practice series/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /jayden book/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /nova practice series/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /very difficult reading exams/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /other papers/i })).toBeInTheDocument();
    expect(screen.queryByText('Anna Hartford 1')).not.toBeInTheDocument();
    expect(screen.queryByText('Jayden Book 01 — Bed Bugs')).not.toBeInTheDocument();
  });

  it('opens a folder to the papers that belong there and can return to the list', async () => {
    const user = userEvent.setup();
    render(
      <ReadingExamFolderBrowser
        papers={PAPERS}
        emptyMessage="No papers in this series yet."
        renderPaper={(paper) => <p>{paper.title}</p>}
      />,
    );

    await user.click(screen.getByRole('button', { name: /anna hartford/i }));
    expect(screen.getByRole('heading', { name: 'Anna Hartford' })).toBeInTheDocument();
    expect(screen.getByText('Anna Hartford 1')).toBeInTheDocument();
    expect(screen.queryByText('Jayden Book 01 — Bed Bugs')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /back to folders/i }));
    expect(screen.getByRole('button', { name: /jayden book/i })).toBeInTheDocument();
    expect(screen.queryByText('Anna Hartford 1')).not.toBeInTheDocument();
  });
});
