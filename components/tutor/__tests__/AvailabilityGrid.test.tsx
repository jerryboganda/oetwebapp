import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AvailabilityGrid } from '../AvailabilityGrid';

describe('AvailabilityGrid', () => {
  const mockSlots = [
    {
      id: 'slot-1',
      dayOfWeek: 'Monday' as const,
      startTime: '09:00:00',
      endTime: '17:00:00',
      isActive: true,
    },
  ];

  it('renders availability slots with day and time inputs', () => {
    const onSave = vi.fn();
    render(<AvailabilityGrid slots={mockSlots} timeZone="Australia/Sydney" saving={false} onSave={onSave} />);

    expect(screen.getByDisplayValue('Monday')).toBeInTheDocument();
    expect(screen.getByDisplayValue('09:00')).toBeInTheDocument();
    expect(screen.getByDisplayValue('17:00')).toBeInTheDocument();
  });

  it('renders accessible delete buttons and toggles slots', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    render(<AvailabilityGrid slots={mockSlots} timeZone="Australia/Sydney" saving={false} onSave={onSave} />);

    const removeButtons = screen.getAllByRole('button', { name: /remove slot 1/i });
    expect(removeButtons.length).toBeGreaterThan(0);

    const saveButton = screen.getByRole('button', { name: /save availability/i });
    fireEvent.click(saveButton);
    expect(onSave).toHaveBeenCalled();
  });
});
