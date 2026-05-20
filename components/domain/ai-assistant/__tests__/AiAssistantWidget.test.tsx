import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiAssistantWidget } from '../AiAssistantWidget';

// Mock child panel so we can test toggle independently
vi.mock('../AiAssistantPanel', () => ({
  AiAssistantPanel: ({ onClose }: { onClose: () => void }) => (
    <div data-testid="ai-panel">
      <button onClick={onClose}>Close Panel</button>
    </div>
  ),
}));

describe('AiAssistantWidget', () => {
  it('renders the floating button for learner role', () => {
    render(<AiAssistantWidget role="learner" />);
    expect(screen.getByRole('button', { name: /toggle ai assistant/i })).toBeInTheDocument();
  });

  it('does not render for sponsor role (no access)', () => {
    const { container } = render(<AiAssistantWidget role="sponsor" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('does not render for null role (unauthenticated)', () => {
    const { container } = render(<AiAssistantWidget role={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('toggles panel visibility on click', async () => {
    const user = userEvent.setup();
    render(<AiAssistantWidget role="admin" />);

    // Panel should not be visible initially
    expect(screen.queryByTestId('ai-panel')).not.toBeInTheDocument();

    // Click to open
    await user.click(screen.getByRole('button', { name: /toggle ai assistant/i }));
    expect(screen.getByTestId('ai-panel')).toBeInTheDocument();

    // Click again to close
    await user.click(screen.getByRole('button', { name: /toggle ai assistant/i }));
    expect(screen.queryByTestId('ai-panel')).not.toBeInTheDocument();
  });

  it('shows notification indicator when hasNotification is true', () => {
    render(<AiAssistantWidget role="learner" hasNotification />);
    expect(screen.getByTestId('notification-indicator')).toBeInTheDocument();
  });

  it('does not show notification indicator by default', () => {
    render(<AiAssistantWidget role="learner" />);
    expect(screen.queryByTestId('notification-indicator')).not.toBeInTheDocument();
  });

  it('renders for expert role', () => {
    render(<AiAssistantWidget role="expert" />);
    expect(screen.getByRole('button', { name: /toggle ai assistant/i })).toBeInTheDocument();
  });
});
