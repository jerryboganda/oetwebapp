import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockList, mockOffer } = vi.hoisted(() => ({ mockList: vi.fn(), mockOffer: vi.fn() }));

vi.mock('@/lib/api/speaking-live-voice-admin', () => ({
  adminGetLiveVoicePreviewList: mockList,
  adminCreateLiveVoicePreviewOffer: mockOffer,
}));

import { LiveVoicePreviewsPanel } from './live-voice-previews-panel';

const list = (overrides: Record<string, unknown> = {}) => ({
  primaryProvider: 'openai',
  candidateOrder: ['openai', 'gemini'],
  sampleText: 'Good morning, doctor.',
  maxSeconds: 20,
  cells: [
    { key: 'female-younger', gender: 'Female', ageBand: 'Under 45', openAiVoice: 'quartz', geminiVoice: 'Leda', accentNote: 'Australian' },
    { key: 'female-older', gender: 'Female', ageBand: '45 and over', openAiVoice: 'willow', geminiVoice: 'Kore', accentNote: 'Irish' },
    { key: 'male-younger', gender: 'Male', ageBand: 'Under 45', openAiVoice: 'ripple', geminiVoice: 'Orus', accentNote: 'Australian' },
    { key: 'male-older', gender: 'Male', ageBand: '45 and over', openAiVoice: 'vesper', geminiVoice: 'Charon', accentNote: 'British' },
  ],
  ...overrides,
});

describe('LiveVoicePreviewsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockList.mockResolvedValue(list());
  });

  it('lists the four OpenAI voices with the Gemini fallback voices, OpenAI as primary', async () => {
    render(<LiveVoicePreviewsPanel />);

    const rows = await screen.findAllByTestId('live-voice-row');
    expect(rows).toHaveLength(4);
    expect(rows.map((row) => row.textContent)).toEqual([
      expect.stringContaining('quartz'),
      expect.stringContaining('willow'),
      expect.stringContaining('ripple'),
      expect.stringContaining('vesper'),
    ]);
    expect(rows[0]).toHaveTextContent('Leda');
    expect(screen.getByTestId('live-voice-order')).toHaveTextContent('Primary: OpenAI');
    expect(screen.getByTestId('live-voice-order')).toHaveTextContent('Fallback: Gemini');
    expect(screen.queryByTestId('live-voice-primary-warning')).not.toBeInTheDocument();
  });

  it('warns when the server still orders Gemini first, because the owner decision is OpenAI first', async () => {
    mockList.mockResolvedValue(list({ primaryProvider: 'gemini', candidateOrder: ['gemini', 'openai'] }));
    render(<LiveVoicePreviewsPanel />);

    expect(await screen.findByTestId('live-voice-primary-warning')).toHaveTextContent('OpenAI first');
  });

  it('says so plainly when the browser has no WebRTC, without calling the provider', async () => {
    render(<LiveVoicePreviewsPanel />);
    await screen.findAllByTestId('live-voice-row');

    fireEvent.click(screen.getByRole('button', { name: 'Play quartz' }));

    await waitFor(() => expect(screen.getByTestId('live-voice-error')).toHaveTextContent('WebRTC is not available'));
    expect(mockOffer).not.toHaveBeenCalled();
  });
});
