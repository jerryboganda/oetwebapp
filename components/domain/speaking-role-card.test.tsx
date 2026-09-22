import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SpeakingRoleCard } from './speaking-role-card';

describe('SpeakingRoleCard', () => {
  it('renders the exam-style Profession/Setting/Background/Tasks card', () => {
    render(
      <SpeakingRoleCard
        role="Nurse"
        setting="Community clinic"
        patient="Anxious parent"
        background="The child had wheeze overnight."
        tasks={["Find the main concern", "Explain in lay language"]}
        prepTimeSeconds={180}
        roleplayTimeSeconds={300}
        disclaimer="Practice estimate only. This is not an official OET score or result."
      />,
    );

    const region = screen.getByRole('region', { name: /role card details/i });

    expect(region).toBeInTheDocument();
    expect(region).toHaveTextContent(/prep:\s*3 min/i);
    expect(region).toHaveTextContent(/role-play:\s*5 min/i);
    expect(screen.getByText('Profession')).toBeInTheDocument();
    expect(screen.getByText('Nurse')).toBeInTheDocument();
    expect(screen.getByText('Setting')).toBeInTheDocument();
    expect(screen.getByText('Community clinic')).toBeInTheDocument();
    expect(screen.getByText('Background')).toBeInTheDocument();
    expect(screen.getByText('Anxious parent')).toBeInTheDocument();
    expect(screen.getByText('Tasks')).toBeInTheDocument();
    expect(screen.getByText('Find the main concern')).toBeInTheDocument();
    expect(screen.getByText(/not an official OET score/i)).toBeInTheDocument();
  });

  it('never renders Emotion/Goal/Topic fields', () => {
    render(
      <SpeakingRoleCard
        role="Nurse"
        setting="Community clinic"
        patient="Anxious parent"
        background="The child had wheeze overnight."
        tasks={['Find the main concern']}
      />,
    );

    expect(screen.queryByText('Emotion')).not.toBeInTheDocument();
    expect(screen.queryByText('Goal')).not.toBeInTheDocument();
    expect(screen.queryByText('Topic')).not.toBeInTheDocument();
  });
});
