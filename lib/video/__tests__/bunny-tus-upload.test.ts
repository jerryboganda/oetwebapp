import { describe, expect, it, vi } from 'vitest';
import type { VideoUploadAuthorization } from '@/lib/api/video-library';

const { uploadInstances } = vi.hoisted(() => ({
  uploadInstances: [] as MockUpload[],
}));

interface MockUpload {
  file: unknown;
  options: Record<string, any>;
  url: string | null;
  start: ReturnType<typeof vi.fn>;
  abort: ReturnType<typeof vi.fn>;
  findPreviousUploads: ReturnType<typeof vi.fn>;
  resumeFromPreviousUpload: ReturnType<typeof vi.fn>;
}

vi.mock('tus-js-client', () => {
  class Upload {
    file: unknown;
    options: Record<string, any>;
    url: string | null = null;
    start = vi.fn();
    abort = vi.fn().mockResolvedValue(undefined);
    findPreviousUploads = vi.fn().mockResolvedValue([]);
    resumeFromPreviousUpload = vi.fn();

    constructor(file: unknown, options: Record<string, any>) {
      this.file = file;
      this.options = options;
      uploadInstances.push(this as unknown as MockUpload);
    }
  }
  return { Upload };
});

import { BUNNY_TUS_RETRY_DELAYS, buildBunnyTusHeaders, createBunnyUpload } from '../bunny-tus-upload';

const file = new File(['bytes'], 'lesson.mp4', { type: 'video/mp4' });

function makeSession(overrides: Partial<VideoUploadAuthorization> = {}): VideoUploadAuthorization {
  return {
    bunnyVideoId: 'bunny-video-1',
    libraryId: '123',
    tusEndpoint: 'https://video.bunnycdn.com/tusupload',
    authorizationSignature: 'sig-1',
    authorizationExpire: 1_900_000_000,
    ...overrides,
  };
}

function lastUpload(): MockUpload {
  return uploadInstances[uploadInstances.length - 1];
}

function make401(): Error {
  const err = new Error('unauthorized') as Error & { originalResponse: { getStatus: () => number } };
  err.originalResponse = { getStatus: () => 401 };
  return err;
}

describe('createBunnyUpload', () => {
  it('configures Bunny headers, metadata and retry delays', () => {
    createBunnyUpload(file, makeSession(), {});
    const upload = lastUpload();

    expect(upload.file).toBe(file);
    expect(upload.options.endpoint).toBe('https://video.bunnycdn.com/tusupload');
    expect(upload.options.headers).toEqual({
      AuthorizationSignature: 'sig-1',
      AuthorizationExpire: '1900000000',
      VideoId: 'bunny-video-1',
      LibraryId: '123',
    });
    expect(upload.options.metadata).toEqual({ filetype: 'video/mp4', title: 'lesson.mp4' });
    expect(upload.options.retryDelays).toEqual([0, 3000, 5000, 10000, 20000]);
    expect(upload.options.retryDelays).toEqual(BUNNY_TUS_RETRY_DELAYS);
  });

  it('exposes the header builder used for re-auth', () => {
    expect(buildBunnyTusHeaders(makeSession({ authorizationSignature: 's2', libraryId: '9' }))).toEqual({
      AuthorizationSignature: 's2',
      AuthorizationExpire: '1900000000',
      VideoId: 'bunny-video-1',
      LibraryId: '9',
    });
  });

  it('start() resumes from a previous upload of the same file (reload-resume)', async () => {
    const handle = createBunnyUpload(file, makeSession(), {});
    const upload = lastUpload();
    const previous = {
      size: 5,
      metadata: {},
      creationTime: 'now',
      urlStorageKey: 'k',
      uploadUrl: 'https://video.bunnycdn.com/tusupload/abc',
      parallelUploadUrls: null,
    };
    upload.findPreviousUploads.mockResolvedValue([previous]);

    await handle.start();

    expect(upload.resumeFromPreviousUpload).toHaveBeenCalledWith(previous);
    expect(upload.start).toHaveBeenCalledTimes(1);
  });

  it('start() begins fresh when there is no previous upload', async () => {
    const handle = createBunnyUpload(file, makeSession(), {});
    const upload = lastUpload();

    await handle.start();

    expect(upload.resumeFromPreviousUpload).not.toHaveBeenCalled();
    expect(upload.start).toHaveBeenCalledTimes(1);
  });

  it('does not auto-retry 401/403 through tus, but retries other statuses', () => {
    createBunnyUpload(file, makeSession(), {});
    const { onShouldRetry } = lastUpload().options;

    const err = (status: number) => ({ originalResponse: { getStatus: () => status } });
    expect(onShouldRetry(err(401), 0, {})).toBe(false);
    expect(onShouldRetry(err(403), 0, {})).toBe(false);
    expect(onShouldRetry(err(500), 0, {})).toBe(true);
    expect(onShouldRetry({ originalResponse: null }, 0, {})).toBe(true);
  });

  it('re-fetches authorization for the same video on 401 and resumes with fresh headers', async () => {
    const refreshSession = vi.fn().mockResolvedValue(
      makeSession({ authorizationSignature: 'sig-fresh', authorizationExpire: 2_000_000_000 }),
    );
    const onError = vi.fn();
    createBunnyUpload(file, makeSession(), { refreshSession, onError });
    const upload = lastUpload();

    upload.options.onError(make401());
    await vi.waitFor(() => expect(upload.start).toHaveBeenCalledTimes(1));

    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(upload.options.headers).toMatchObject({
      AuthorizationSignature: 'sig-fresh',
      AuthorizationExpire: '2000000000',
      VideoId: 'bunny-video-1',
    });
    expect(onError).not.toHaveBeenCalled();
  });

  it('surfaces the error when refreshing the authorization fails', async () => {
    const refreshSession = vi.fn().mockRejectedValue(new Error('backend down'));
    const onError = vi.fn();
    createBunnyUpload(file, makeSession(), { refreshSession, onError });
    const upload = lastUpload();

    upload.options.onError(make401());
    await vi.waitFor(() => expect(onError).toHaveBeenCalledTimes(1));

    expect(onError.mock.calls[0][0]).toBeInstanceOf(Error);
    expect((onError.mock.calls[0][0] as Error).message).toBe('backend down');
    expect(upload.start).not.toHaveBeenCalled();
  });

  it('surfaces non-auth errors directly without re-auth', () => {
    const refreshSession = vi.fn();
    const onError = vi.fn();
    createBunnyUpload(file, makeSession(), { refreshSession, onError });
    const upload = lastUpload();

    const err = new Error('disk full') as Error & { originalResponse: { getStatus: () => number } };
    err.originalResponse = { getStatus: () => 507 };
    upload.options.onError(err);

    expect(onError).toHaveBeenCalledWith(err);
    expect(refreshSession).not.toHaveBeenCalled();
  });

  it('pause keeps the upload URL (non-terminating abort); abort terminates', async () => {
    const handle = createBunnyUpload(file, makeSession(), {});
    const upload = lastUpload();

    await handle.pause();
    expect(upload.abort).toHaveBeenLastCalledWith();

    handle.resume();
    expect(upload.start).toHaveBeenCalledTimes(1);

    await handle.abort();
    expect(upload.abort).toHaveBeenLastCalledWith(true);
  });
});
