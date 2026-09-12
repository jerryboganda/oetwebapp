import { createElement, forwardRef } from 'react';
import { render } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { VideoDetail } from '@/lib/types/videos';

const mocks = vi.hoisted(() => ({
  fetchVideo: vi.fn(),
  toggleVideoBookmark: vi.fn(),
  getAppRuntimeKind: vi.fn(() => 'web'),
  // Never calls any callback back to the parent — proving the wrapper's
  // aspect-video decision no longer depends on anything the child does.
  videoPlayerStub: vi.fn((_props: Record<string, unknown>) => null),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'video-1' }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: { userId: 'user-1' } }),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));

vi.mock('@/lib/api/videos', () => ({
  fetchVideo: mocks.fetchVideo,
  toggleVideoBookmark: mocks.toggleVideoBookmark,
}));

vi.mock('@/hooks/use-media-preferences', () => ({ useLowBandwidthMode: () => false }));

vi.mock('@/lib/runtime-signals', () => ({ getAppRuntimeKind: mocks.getAppRuntimeKind }));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => createElement('div', null, children),
}));

// The mock component is named so it satisfies react/display-name on its own.
// The previous `eslint-disable-next-line` sat one line above the `forwardRef`
// that actually creates the component, so it was reported as unused while the
// error still fired — and it kept `iOS Build Check` (gated on this lint job)
// skipped on every run.
vi.mock('@/components/videos/video-player', () => ({
  VideoPlayer: forwardRef(function VideoPlayerStub(props: Record<string, unknown>, ref) {
    mocks.videoPlayerStub(props);
    return createElement('div', { ref, 'data-testid': 'video-player-stub' });
  }),
}));

import VideoDetailPage from './page';

const baseVideo: VideoDetail = {
  id: 'video-1',
  title: 'Sample lesson',
  description: null,
  durationSeconds: 300,
  thumbnailUrl: null,
  accessTier: 'free',
  isAccessible: true,
  requiresUpgrade: false,
  lockReason: null,
  subtestCode: 'listening',
  difficulty: null,
  language: null,
  tags: [],
  isFeatured: false,
  publishedAt: null,
  viewCount: 0,
  progress: null,
  bookmarked: false,
  categoryIds: [],
  chapters: [],
  captions: [],
  attachments: [],
  previousVideoId: null,
  nextVideoId: null,
};

/**
 * Regression coverage for the one-frame aspect-video flash (Final Developer
 * Modification Brief item 2, root cause #2): the wrapper around VideoPlayer
 * must decide whether to apply `aspect-video` synchronously, from the same
 * runtime signal VideoPlayer's own boot effect gates WEB_NOT_ALLOWED on —
 * never by waiting on a callback the child fires from a useEffect.
 */
describe('Video detail page — aspect-video wrapper timing', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchVideo.mockResolvedValue(baseVideo);
    mocks.getAppRuntimeKind.mockReturnValue('web');
  });

  it('omits aspect-video on the very first render when the runtime is web, even though the stubbed player never calls back', async () => {
    const { container, findByTestId } = render(<VideoDetailPage />);
    await findByTestId('video-player-stub');

    expect(container.querySelector('.aspect-video')).not.toBeInTheDocument();
    // Proves the decision was not sourced from the child at all.
    expect(mocks.videoPlayerStub).toHaveBeenCalled();
    const stubProps = mocks.videoPlayerStub.mock.calls[0][0] as Record<string, unknown>;
    expect(stubProps.onPlaybackBlockedChange).toBeUndefined();
  });

  it('keeps the normal 16:9 aspect-video crop for non-web runtimes (desktop/native app playback unaffected)', async () => {
    mocks.getAppRuntimeKind.mockReturnValue('desktop');
    const { container, findByTestId } = render(<VideoDetailPage />);
    await findByTestId('video-player-stub');

    expect(container.querySelector('.aspect-video')).toBeInTheDocument();
  });
});
