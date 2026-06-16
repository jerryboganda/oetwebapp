import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetAdminVoiceDesignConfig,
  mockGetAudioRegenerationBatches,
  mockRegenerateAllAudio,
  mockRetryAudioRegenerationBatch,
  mockGetElevenLabsVoices,
  mockUploadDictionary,
} = vi.hoisted(() => ({
  mockGetAdminVoiceDesignConfig: vi.fn(),
  mockGetAudioRegenerationBatches: vi.fn(),
  mockRegenerateAllAudio: vi.fn(),
  mockRetryAudioRegenerationBatch: vi.fn(),
  mockGetElevenLabsVoices: vi.fn(),
  mockUploadDictionary: vi.fn(),
}));

vi.mock('@/components/domain/admin-route-surface', () => ({
  AdminRouteWorkspace: ({ children }: { children: React.ReactNode }) => <main>{children}</main>,
  AdminRoutePanel: ({ title, children }: { title: string; children: React.ReactNode }) => <section aria-label={title}>{children}</section>,
  AdminRouteSectionHeader: ({ title }: { title: string }) => <h1>{title}</h1>,
}));

vi.mock('@/components/admin/layout/admin-page-shell', () => ({
  AdminPageShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/admin/ui/page-header', () => ({
  PageHeader: ({ title }: { title: string }) => <h1>{title}</h1>,
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({ children, loading: _loading, variant: _variant, size: _size, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { loading?: boolean; variant?: string; size?: string }) => (
    <button {...props}>{children}</button>
  ),
}));

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

vi.mock('@/components/ui/modal', () => ({
  Modal: ({ open, title, children }: { open: boolean; title: string; children: React.ReactNode }) => (
    open ? <div role="dialog" aria-label={title}>{children}</div> : null
  ),
}));

vi.mock('@/components/ui/alert', () => ({
  Toast: ({ message }: { message: string }) => <div role="status">{message}</div>,
}));

vi.mock('@/lib/api', () => ({
  getElevenLabsVoices: mockGetElevenLabsVoices,
  previewAdminVoiceDesign: vi.fn(),
  regenerateAllAudio: mockRegenerateAllAudio,
  getAudioRegenerationBatches: mockGetAudioRegenerationBatches,
  cancelAudioRegenerationBatch: vi.fn(),
  retryAudioRegenerationBatch: mockRetryAudioRegenerationBatch,
  getAdminVoiceDesignConfig: mockGetAdminVoiceDesignConfig,
  saveAdminVoiceDesignConfig: vi.fn(),
  uploadElevenLabsPronunciationDictionary: mockUploadDictionary,
}));

import AdminVoiceDesignPage from './page';

const voiceDesignConfig = {
  elevenLabsTtsBaseUrl: 'https://api.elevenlabs.io/v1',
  elevenLabsDefaultVoiceId: 'auq43ws1oslv0tO4BDa7',
  elevenLabsModel: 'eleven_multilingual_v2',
  elevenLabsOutputFormat: 'mp3_44100_128',
  elevenLabsPronunciationDictionaryId: 'dict-001',
  elevenLabsPronunciationDictionaryVersionId: 'dict-version-001',
  elevenLabsStability: 0.45,
  elevenLabsSimilarityBoost: 0.85,
  elevenLabsStyle: 0,
  elevenLabsUseSpeakerBoost: true,
  elevenLabsApiKeyPresent: true,
  lastUpdatedAt: null,
  lastUpdatedBy: null,
};

describe('Admin voice design (ElevenLabs)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetAdminVoiceDesignConfig.mockResolvedValue(voiceDesignConfig);
    mockGetAudioRegenerationBatches.mockResolvedValue({ batches: [] });
    mockRegenerateAllAudio.mockResolvedValue({ batchId: 'batch-1', totalItems: 12 });
    mockRetryAudioRegenerationBatch.mockResolvedValue({ batchId: 'batch-retry' });
    mockGetElevenLabsVoices.mockResolvedValue({ voices: [] });
    mockUploadDictionary.mockResolvedValue({ dictionaryId: 'd1', versionId: 'v1' });
  });

  it('blocks regeneration preview when ElevenLabs API key is missing', async () => {
    mockGetAdminVoiceDesignConfig.mockResolvedValueOnce({
      ...voiceDesignConfig,
      elevenLabsApiKeyPresent: false,
    });

    render(<AdminVoiceDesignPage />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Preview Count/i })).toBeDisabled();
    });
    expect(mockRegenerateAllAudio).not.toHaveBeenCalled();
  });

  it('blocks regeneration preview when saved ElevenLabs settings are dirty', async () => {
    const user = userEvent.setup();
    render(<AdminVoiceDesignPage />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Preview Count/i })).toBeEnabled();
    });
    await user.clear(screen.getByLabelText('ElevenLabs API Base URL'));
    await user.type(screen.getByLabelText('ElevenLabs API Base URL'), 'https://example.test/v1');

    expect(screen.getByRole('button', { name: /Preview Count/i })).toBeDisabled();
    expect(screen.getByText(/Save a valid ElevenLabs API key/i)).toBeInTheDocument();
  });

  it('sends regeneration preview through the ElevenLabs payload', async () => {
    const user = userEvent.setup();
    render(<AdminVoiceDesignPage />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Preview Count/i })).toBeEnabled();
    });
    await user.click(screen.getByRole('button', { name: /Preview Count/i }));

    expect(mockRegenerateAllAudio).toHaveBeenCalledWith(expect.objectContaining({
      audioType: 'recalls',
      dryRun: true,
      providerName: 'elevenlabs',
      modelVariant: 'eleven_multilingual_v2',
      voiceId: 'auq43ws1oslv0tO4BDa7',
    }));
  });

  it('fetches ElevenLabs voices and selecting one updates the default voice id', async () => {
    mockGetElevenLabsVoices.mockResolvedValueOnce({
      voices: [{ voiceId: 'voice-aria', name: 'Aria', category: 'premade', previewUrl: null, labels: null }],
    });
    const user = userEvent.setup();
    render(<AdminVoiceDesignPage />);

    await waitFor(() => expect(mockGetAdminVoiceDesignConfig).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: /Fetch Voices/i }));

    await user.click(await screen.findByRole('button', { name: /^Select$/i }));

    expect((screen.getByLabelText(/Default Voice ID/) as HTMLInputElement).value).toBe('voice-aria');
  });

  it('surfaces the ElevenLabs error detail when a PLS upload fails', async () => {
    mockUploadDictionary.mockRejectedValueOnce(new Error('ElevenLabs dictionary upload failed (422): invalid lexeme'));
    const user = userEvent.setup();
    render(<AdminVoiceDesignPage />);

    await waitFor(() => expect(mockGetAdminVoiceDesignConfig).toHaveBeenCalled());

    const file = new File(['<lexicon/>'], 'terms.pls', { type: 'application/pls+xml' });
    await user.upload(screen.getByLabelText('PLS Pronunciation Dictionary'), file);
    await user.click(screen.getByRole('button', { name: /Upload PLS/i }));

    expect(await screen.findByText(/invalid lexeme/i)).toBeInTheDocument();
  });

  it('retries failed recall batches from the job tracker', async () => {
    mockGetAudioRegenerationBatches.mockResolvedValueOnce({
      batches: [{
        batchId: 'batch-failed',
        audioType: 'recalls',
        scope: 'missing',
        status: 'failed',
        totalItems: 10,
        completedItems: 7,
        failedItems: 3,
        voiceId: 'auq43ws1oslv0tO4BDa7',
        modelVariant: 'eleven_multilingual_v2',
        providerName: 'elevenlabs',
        speed: 1,
        pitch: 0,
        emotion: '',
        startedAt: '2026-05-23T00:00:00Z',
        completedAt: '2026-05-23T00:01:00Z',
        requestedBy: 'admin-1',
      }],
    });
    const user = userEvent.setup();

    render(<AdminVoiceDesignPage />);
    await user.click(await screen.findByRole('button', { name: /Retry failed/i }));

    expect(mockRetryAudioRegenerationBatch).toHaveBeenCalledWith('batch-failed');
  });
});
