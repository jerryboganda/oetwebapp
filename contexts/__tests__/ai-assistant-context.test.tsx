import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { AiAssistantProvider, useAiAssistantContext } from '@/contexts/ai-assistant-context';

vi.mock('@/hooks/use-ai-assistant', () => ({
  useAiAssistant: (): { marker: string } => ({ marker: 'from-hook' }),
}));

function Consumer(): React.JSX.Element {
  const value = useAiAssistantContext() as unknown as { marker: string };
  return <div data-testid="marker">{value.marker}</div>;
}

describe('AiAssistantContext', () => {
  it('throws when used outside provider', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<Consumer />)).toThrow(
      /useAiAssistantContext must be used inside <AiAssistantProvider>/,
    );
    spy.mockRestore();
  });

  it('exposes value from useAiAssistant when wrapped in provider', () => {
    render(
      <AiAssistantProvider>
        <Consumer />
      </AiAssistantProvider>,
    );
    expect(screen.getByTestId('marker')).toHaveTextContent('from-hook');
  });
});
