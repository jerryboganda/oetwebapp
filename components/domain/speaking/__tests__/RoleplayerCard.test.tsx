import { render, screen } from '@testing-library/react';
import { RoleplayerCard, type RoleplayerCardData } from '../RoleplayerCard';

const card = (overrides: Partial<RoleplayerCardData> = {}): RoleplayerCardData => ({
  professionId: 'medicine',
  setting: 'General practice',
  interlocutorRole: 'Patient',
  patientName: 'Mr Lee',
  patientAge: '54',
  patientBackground: 'You have had chest pain since exercising.',
  patientTasks: ['Explain your symptoms'],
  ...overrides,
});

describe('RoleplayerCard heading', () => {
  it('names the tutor card by its exam slot, like the candidate card, whatever number the source card prints', () => {
    render(<RoleplayerCard card={card({ displayCardNumber: 4 })} cardNumber={1} slotLabel="A" />);

    expect(screen.getByRole('heading', { name: 'Roleplayer Card A' })).toBeInTheDocument();
    expect(screen.queryByText(/No\./)).not.toBeInTheDocument();
  });

  it('still shows the printed number when there is no slot to name', () => {
    render(<RoleplayerCard card={card({ displayCardNumber: 4 })} />);

    expect(screen.getByRole('heading', { name: 'Roleplayer Card No. 4' })).toBeInTheDocument();
  });

  it('falls back to the caller-supplied number, and prints a bare heading with neither', () => {
    const { rerender } = render(<RoleplayerCard card={card()} cardNumber={2} />);
    expect(screen.getByRole('heading', { name: 'Roleplayer Card No. 2' })).toBeInTheDocument();

    rerender(<RoleplayerCard card={card()} />);
    expect(screen.getByRole('heading', { name: 'Roleplayer Card' })).toBeInTheDocument();
  });
});
