import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SpeakingRoleCard } from './speaking-role-card';

describe('SpeakingRoleCard', () => {
  it('renders structured OET speaking role-play metadata', () => {
    render(
      <SpeakingRoleCard
        role="Nurse"
        setting="Community clinic"
        patient="Anxious parent"
        task="Explain inhaler technique."
        background="The child had wheeze overnight."
        tasks={["Find the main concern", "Explain in lay language"]}
        patientEmotion="anxious"
        communicationGoal="Reassure and safety-net"
        clinicalTopic="Paediatric asthma"
        prepTimeSeconds={180}
        roleplayTimeSeconds={300}
        disclaimer="Practice estimate only. This is not an official OET score or result."
      />,
    );

    expect(screen.getByRole('region', { name: /role card details/i })).toBeInTheDocument();
    expect(screen.getByText('Prep: 3 min')).toBeInTheDocument();
    expect(screen.getByText('Role-play: 5 min')).toBeInTheDocument();
    expect(screen.getByText('Find the main concern')).toBeInTheDocument();
    expect(screen.getByText('Reassure and safety-net')).toBeInTheDocument();
    expect(screen.getByText(/not an official OET score/i)).toBeInTheDocument();
  });
});
