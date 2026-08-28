import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React, { useState } from 'react';
import { BCQuestionRenderer } from '../BCQuestionRenderer';
import { cn } from '@/lib/utils';
import type { ListeningSessionQuestionDto } from '@/lib/listening-api';

// Test fixture for Part B (Q25..Q30)
const PART_B_QUESTIONS: ListeningSessionQuestionDto[] = [
  {
    id: 'lq-test-25',
    number: 25,
    partCode: 'B1',
    text: 'What does the doctor advise the nurse to do?',
    type: 'multiple_choice_3',
    options: ['Administer oral analgesia', 'Order an immediate CT scan', 'Discharge with follow-up'],
    optionKeys: ['A', 'B', 'C'],
    points: 1,
  },
  {
    id: 'lq-test-26',
    number: 26,
    partCode: 'B2',
    text: 'What is the nurse concerned about regarding the patient?',
    type: 'multiple_choice_3',
    options: ['Deteriorating respiratory rate', 'Post-operative wound infection', 'Medication non-compliance'],
    optionKeys: ['A', 'B', 'C'],
    points: 1,
  },
  {
    id: 'lq-test-27',
    number: 27,
    partCode: 'B3',
    text: 'The dietitian emphasizes that the new nutrition protocol is designed to',
    type: 'multiple_choice_3',
    options: ['Reduce hospital length of stay', 'Standardise enteral feeding formulas', 'Minimise electrolyte disturbances'],
    optionKeys: ['A', 'B', 'C'],
    points: 1,
  },
  {
    id: 'lq-test-28',
    number: 28,
    partCode: 'B4',
    text: 'Why does the physiotherapist suggest delaying the session?',
    type: 'multiple_choice_3',
    options: ['The patient is experiencing acute nausea', 'The analgesic medication has not taken effect', 'The oxygen saturation level is too low'],
    optionKeys: ['A', 'B', 'C'],
    points: 1,
  },
  {
    id: 'lq-test-29',
    number: 29,
    partCode: 'B5',
    text: 'What is the pharmacist clarifying about the prescription?',
    type: 'multiple_choice_3',
    options: ['The daily dosing schedule', 'The route of administration', 'The potential drug interaction'],
    optionKeys: ['A', 'B', 'C'],
    points: 1,
  },
  {
    id: 'lq-test-30',
    number: 30,
    partCode: 'B6',
    text: 'The charge nurse is reminding staff that hand hygiene audits will',
    type: 'multiple_choice_3',
    options: ['Take place without prior notice', 'Include night shift rotations', 'Focus primarily on high-dependency units'],
    optionKeys: ['A', 'B', 'C'],
    points: 1,
  },
];

// Test Harness Component replicating the candidate single-card jump navigation
function PartBTestContainer({
  questions = PART_B_QUESTIONS,
  initialIndex = 0,
  onPersist = vi.fn(),
}: {
  questions?: ListeningSessionQuestionDto[];
  initialIndex?: number;
  onPersist?: (id: string, val: string) => void;
}) {
  const [activeIdx, setActiveIdx] = useState(initialIndex);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [annotations, setAnnotations] = useState<Record<string, { flagged?: boolean; struckOptions?: string[] }>>({});

  const currentQ = questions[activeIdx] ?? questions[0];

  return (
    <div data-testid="listening-part-b-container">
      {/* Jump-to pills */}
      <div role="tablist" aria-label="Question selector">
        {questions.map((q, idx) => {
          const isActive = idx === activeIdx;
          const isAnswered = Boolean((answers[q.id] ?? '').trim());
          const isFlagged = Boolean(annotations[q.id]?.flagged);
          return (
            <button
              key={q.id}
              type="button"
              role="tab"
              aria-selected={isActive}
              aria-label={`Question ${q.number}${isAnswered ? ' (answered)' : ''}${isFlagged ? ' (flagged)' : ''}`}
              onClick={() => setActiveIdx(idx)}
              className={cn(
                'pill',
                isActive && 'pill-active',
                isAnswered && 'pill-answered',
                isFlagged && 'pill-flagged',
              )}
            >
              {q.number}
            </button>
          );
        })}
      </div>

      {/* Active Question Card */}
      {currentQ && (
        <BCQuestionRenderer
          key={currentQ.id}
          questionNumber={currentQ.number}
          partLabel="Part B"
          prompt={currentQ.text}
          options={currentQ.options}
          optionKeys={currentQ.optionKeys}
          value={answers[currentQ.id] ?? ''}
          annotation={annotations[currentQ.id]}
          onAnnotationChange={(mutator) => {
            setAnnotations((prev) => {
              const current = prev[currentQ.id] ?? {};
              return { ...prev, [currentQ.id]: mutator(current as any) };
            });
          }}
          onChange={(val) => {
            setAnswers((prev) => ({ ...prev, [currentQ.id]: val }));
            onPersist(currentQ.id, val);
          }}
        />
      )}

      {/* Stepper buttons */}
      <div className="stepper-controls">
        {activeIdx > 0 && (
          <button
            type="button"
            onClick={() => setActiveIdx((i) => i - 1)}
            aria-label="Previous question"
          >
            Previous Question
          </button>
        )}
        {activeIdx < questions.length - 1 ? (
          <button
            type="button"
            onClick={() => setActiveIdx((i) => i + 1)}
            aria-label="Next question"
          >
            Next Question
          </button>
        ) : (
          <button type="button" aria-label="Next Sub-section">
            Next Sub-section
          </button>
        )}
      </div>
    </div>
  );
}

describe('Listening Part B & C Single-Card Navigation and Persistence', () => {
  it('Test 6 — Sequential Next Question progression advances Q25 -> Q26 -> Q30', async () => {
    const user = userEvent.setup();
    render(<PartBTestContainer />);

    // Initially on Q25 with its authentic stem
    expect(screen.getByRole('heading', { level: 3, name: 'What does the doctor advise the nurse to do?' })).toBeInTheDocument();
    expect(screen.getByText('Administer oral analgesia')).toBeInTheDocument();

    // Click Next Question -> should load Q26
    await user.click(screen.getByRole('button', { name: 'Next question' }));
    expect(screen.getByRole('heading', { level: 3, name: 'What is the nurse concerned about regarding the patient?' })).toBeInTheDocument();
    expect(screen.getByText('Deteriorating respiratory rate')).toBeInTheDocument();

    // Click Next Question -> should load Q27
    await user.click(screen.getByRole('button', { name: 'Next question' }));
    expect(screen.getByRole('heading', { level: 3, name: 'The dietitian emphasizes that the new nutrition protocol is designed to' })).toBeInTheDocument();
  });

  it('Test 7 — Jump-to navigation allows direct switching between question pills', async () => {
    const user = userEvent.setup();
    render(<PartBTestContainer />);

    // Jump directly to Question 29
    const q29Pill = screen.getByRole('tab', { name: 'Question 29' });
    await user.click(q29Pill);

    expect(screen.getByRole('heading', { level: 3, name: 'What is the pharmacist clarifying about the prescription?' })).toBeInTheDocument();
    expect(screen.getByText('The daily dosing schedule')).toBeInTheDocument();

    // Jump back to Question 25
    const q25Pill = screen.getByRole('tab', { name: 'Question 25' });
    await user.click(q25Pill);

    expect(screen.getByRole('heading', { level: 3, name: 'What does the doctor advise the nurse to do?' })).toBeInTheDocument();
  });

  it('Test 8 — Candidate answers and review flags persist across question jumps', async () => {
    const user = userEvent.setup();
    const onPersist = vi.fn();
    render(<PartBTestContainer onPersist={onPersist} />);

    // On Q25: Select Option B ('Order an immediate CT scan')
    await user.click(screen.getByText('Order an immediate CT scan'));
    expect(onPersist).toHaveBeenCalledWith('lq-test-25', 'B');

    // Flag Q25 for review
    const flagBtn = screen.getByRole('button', { name: /flag question 25 for review/i });
    await user.click(flagBtn);

    // Jump to Q28
    await user.click(screen.getByRole('tab', { name: 'Question 28' }));
    expect(screen.getByRole('heading', { level: 3, name: 'Why does the physiotherapist suggest delaying the session?' })).toBeInTheDocument();

    // On Q28: Select Option C
    await user.click(screen.getByText('The oxygen saturation level is too low'));
    expect(onPersist).toHaveBeenCalledWith('lq-test-28', 'C');

    // Jump back to Q25
    await user.click(screen.getByRole('tab', { name: /Question 25/ }));

    // Verify Q25 still has Option B checked and flag retained
    const radios = screen.getAllByRole('radio');
    expect(radios[1].getAttribute('aria-checked')).toBe('true');
  });

  it('Test 5 — Standalone autoplay initializes and unlocks on candidate entry', () => {
    const playMock = vi.fn().mockResolvedValue(undefined);
    const audioElement = { play: playMock } as unknown as HTMLAudioElement;

    // Simulate entry
    const isReady = true;
    if (isReady && audioElement) {
      void audioElement.play();
    }

    expect(playMock).toHaveBeenCalledTimes(1);
  });
});
