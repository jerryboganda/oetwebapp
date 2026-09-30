const { mockRequest } = vi.hoisted(() => ({ mockRequest: vi.fn() }));

vi.mock('@/lib/api', async () => {
  const client = await vi.importActual<typeof import('@/lib/api/client')>('@/lib/api/client');
  return { ApiError: client.ApiError, apiClient: { request: mockRequest } };
});

import { ApiError } from '@/lib/api/client';
import { uploadSpeakingSessionRecording } from '../speaking-sessions';

describe('uploadSpeakingSessionRecording', () => {
  const audio = new Blob(['audio'], { type: 'audio/webm' });

  beforeEach(() => {
    mockRequest.mockReset().mockResolvedValue(undefined);
  });

  it('posts the recording once and no longer accepts a status by number alone', async () => {
    await uploadSpeakingSessionRecording('sps 1', audio, 12.4);

    expect(mockRequest).toHaveBeenCalledTimes(1);
    const [path, init, options] = mockRequest.mock.calls[0];
    expect(path).toBe('/v1/speaking/sessions/sps%201/recording');
    expect(init.method).toBe('POST');
    expect(options).toEqual({ json: false, timeoutMs: 120_000 });
  });

  it('treats a repeat upload (409 recording_already_received) as success', async () => {
    mockRequest.mockRejectedValueOnce(new ApiError(409, 'recording_already_received', 'already', false));

    await expect(uploadSpeakingSessionRecording('sps-1', audio)).resolves.toBeUndefined();
  });

  it.each(['live_voice_transcript_window_closed', 'speaking_consent_required', 'speaking_session_invalid_state'])(
    'rejects the other 409 %s instead of reporting the upload as received',
    async (code) => {
      mockRequest.mockRejectedValueOnce(new ApiError(409, code, 'refused', false));

      await expect(uploadSpeakingSessionRecording('sps-1', audio)).rejects.toMatchObject({ status: 409, code });
    },
  );

  it('rejects a server error unchanged, so the caller can keep the audio and retry', async () => {
    mockRequest.mockRejectedValueOnce(new ApiError(503, 'internal_server_error', 'try later', true));

    await expect(uploadSpeakingSessionRecording('sps-1', audio)).rejects.toMatchObject({ status: 503 });
  });
});
