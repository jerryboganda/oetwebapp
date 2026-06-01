import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ReadingPlayer, {
  type ReadingPassageDto,
  type ReadingQuestionDto,
} from './ReadingPlayer';

// A Part B-style passage (one short extract, one 3-option question) followed by
// a Part C-style passage (one long passage, several 4-option questions).
const passages: ReadingPassageDto[] = [
  { id: 'b-1', title: 'Extract B1', bodyHtml: '<p>Hand hygiene policy.</p>', partCode: 2 },
  { id: 'c-1', title: 'Passage C1', bodyHtml: '<p>Long journal passage.</p>', partCode: 3 },
];

const questions: ReadingQuestionDto[] = [
  {
    id: 'b-q-1',
    passageId: 'b-1',
    stem: 'B1 purpose?',
    options: [
      { key: 'A', text: 'Alpha' },
      { key: 'B', text: 'Beta' },
      { key: 'C', text: 'Gamma' },
    ],
    questionType: 'MultipleChoice3',
    partCode: 2,
  },
  {
    id: 'c-q-1',
    passageId: 'c-1',
    stem: 'C1 inference one?',
    options: [
      { key: 'A', text: 'A1' },
      { key: 'B', text: 'B1' },
      { key: 'C', text: 'C1' },
      { key: 'D', text: 'D1' },
    ],
    questionType: 'MultipleChoice4',
    partCode: 3,
  },
  {
    id: 'c-q-2',
    passageId: 'c-1',
    stem: 'C1 inference two?',
    options: [
      { key: 'A', text: 'A2' },
      { key: 'B', text: 'B2' },
      { key: 'C', text: 'C2' },
      { key: 'D', text: 'D2' },
    ],
    questionType: 'MultipleChoice4',
    partCode: 3,
  },
];

describe('ReadingPlayer (stacked layout)', () => {
  it('renders every passage and its questions on one continuous scroll', () => {
    render(
      <ReadingPlayer
        mode="drill"
        questions={questions}
        passages={passages}
        sessionId="session-1"
        onComplete={vi.fn()}
      />,
    );

    // Both passages are present at once (no pagination / single-question view).
    expect(screen.getByText('Hand hygiene policy.')).toBeInTheDocument();
    expect(screen.getByText('Long journal passage.')).toBeInTheDocument();

    // All three questions render together, with continuous numbering.
    const cards = screen.getAllByTestId('reading-question-card');
    expect(cards).toHaveLength(3);
    expect(within(cards[0]).getByText('Q1')).toBeInTheDocument();
    expect(within(cards[1]).getByText('Q2')).toBeInTheDocument();
    expect(within(cards[2]).getByText('Q3')).toBeInTheDocument();

    // One group per passage (Part B pair + Part C stack).
    expect(screen.getAllByTestId('reading-group')).toHaveLength(2);
  });

  it('records answers and submits them via onComplete', async () => {
    const user = userEvent.setup();
    const onComplete = vi.fn();
    render(
      <ReadingPlayer
        mode="drill"
        questions={questions}
        passages={passages}
        sessionId="session-1"
        onComplete={onComplete}
      />,
    );

    expect(screen.getByText('0 of 3 answered')).toBeInTheDocument();

    await user.click(screen.getByRole('radio', { name: /Beta/ }));
    await user.click(screen.getByRole('radio', { name: /A1/ }));

    expect(screen.getByText('2 of 3 answered')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /submit/i }));

    expect(onComplete).toHaveBeenCalledWith({ 'b-q-1': 'B', 'c-q-1': 'A' });
  });
});
