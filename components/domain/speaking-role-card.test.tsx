import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SpeakingRoleCard, roleCardPropsFrom } from './speaking-role-card';

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

  it('orders the rows Profession → Setting → Background → Tasks, with one task list', () => {
    render(
      <SpeakingRoleCard
        role="Doctor"
        setting="General practice"
        patient="Mr Lee, 54"
        background="Recent chest pain."
        tasks={['Take a history', 'Explain the plan']}
      />,
    );

    const labels = ['Profession', 'Setting', 'Background', 'Tasks'].map((label) => screen.getByText(label));
    for (let index = 1; index < labels.length; index += 1) {
      expect(labels[index - 1].compareDocumentPosition(labels[index]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
    expect(screen.getAllByRole('list')).toHaveLength(1);
    expect(screen.getAllByText('Take a history')).toHaveLength(1);
  });

  it('maps a server card projection without emotion/goal/topic fields', () => {
    const props = roleCardPropsFrom({
      professionId: 'medicine',
      candidateRole: '',
      setting: 'Emergency department',
      patientName: 'Ms Ortiz',
      patientAge: '32',
      background: 'Fell from a bike.',
      tasks: ['Reassure'],
      disclaimer: 'Practice estimate only.',
      displayCardNumber: 7,
    });

    expect(props).toEqual({
      role: 'Medicine',
      setting: 'Emergency department',
      patient: 'Ms Ortiz, 32',
      background: 'Fell from a bike.',
      tasks: ['Reassure'],
      cardNumber: 7,
      disclaimer: 'Practice estimate only.',
    });
    render(<SpeakingRoleCard {...props} />);
    expect(screen.getByText('Role-Play Card No. 7')).toBeInTheDocument();
  });
});
