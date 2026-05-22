import { render, screen, fireEvent, waitFor } from '@testing-library/react';

const { mockFetchForecast } = vi.hoisted(() => ({
  mockFetchForecast: vi.fn(),
}));

vi.mock('@/lib/api', () => ({ fetchReadinessForecast: mockFetchForecast }));

import { ReadinessForecastSimulator } from '../readiness-forecast-simulator';

describe('ReadinessForecastSimulator', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchForecast.mockResolvedValue({
      probability: 72,
      weeksNeeded: 6,
      weeksAvailable: 10,
      requiredImprovement: 12,
      slopePerWeek: 1.5,
      scenarios: [
        { label: 'Current pace', hoursPerWeek: 5, projectedReadinessAtTarget: 70, probability: 60 },
        { label: 'Recommended', hoursPerWeek: 10, projectedReadinessAtTarget: 78, probability: 72 },
      ],
    });
  });

  it('does not render when closed', () => {
    const { container } = render(<ReadinessForecastSimulator open={false} onClose={() => {}} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders heading and slider when open', async () => {
    render(<ReadinessForecastSimulator open={true} onClose={() => {}} />);
    expect(await screen.findByText('Forecast simulator')).toBeInTheDocument();
    expect(screen.getByLabelText(/hours per week/i)).toBeInTheDocument();
  });

  it('fetches forecast scenario when hours change', async () => {
    render(<ReadinessForecastSimulator open={true} onClose={() => {}} />);
    await waitFor(() => expect(mockFetchForecast).toHaveBeenCalled());
    const slider = screen.getByLabelText(/hours per week/i) as HTMLInputElement;
    fireEvent.change(slider, { target: { value: '18' } });
    await waitFor(() => expect(mockFetchForecast).toHaveBeenCalledWith(18));
  });

  it('displays projected probability from forecast result', async () => {
    render(<ReadinessForecastSimulator open={true} onClose={() => {}} />);
    expect(await screen.findByText('72%')).toBeInTheDocument();
  });
});
