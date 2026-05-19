import { beforeEach, describe, expect, it, vi } from 'vitest';
import { registerNativePushToken } from './notifications-api';

const { mockRequest } = vi.hoisted(() => ({
  mockRequest: vi.fn(),
}));

vi.mock('./api', () => ({
  ApiError: class ApiError extends Error {
    constructor(
      public readonly status: number,
      public readonly code: string,
      message: string,
    ) {
      super(message);
    }
  },
  apiClient: {
    request: mockRequest,
  },
}));

describe('notifications-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('registers native push tokens through apiClient so auth and CSRF headers are centralized', async () => {
    mockRequest.mockResolvedValue({ tokenId: 'token-1' });

    await expect(registerNativePushToken({ token: 'native-token', platform: 'ios' }))
      .resolves.toEqual({ tokenId: 'token-1' });

    expect(mockRequest).toHaveBeenCalledWith('/v1/notifications/push-token', {
      method: 'POST',
      body: JSON.stringify({ token: 'native-token', platform: 'ios' }),
    });
  });
});
