import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { WritingStimulusViewer } from '../WritingStimulusViewer';

// ── Mock @/lib/api ───────────────────────────────────────────────────────────
vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: vi.fn().mockResolvedValue('blob:fake-url'),
}));

// ── Mock pdfjs-dist ──────────────────────────────────────────────────────────
// Mirrors the dynamic import('pdfjs-dist/legacy/build/pdf.mjs') used in the
// component. We return a minimal fake that satisfies the component's API:
//   pdfjs.GlobalWorkerOptions.workerSrc = ...
//   pdfjs.getDocument({ url }).promise
//     → { numPages, getPage }
//   page.getViewport({ scale }) → { width, height }
//   page.render({ canvasContext, viewport }).promise
vi.mock('pdfjs-dist/legacy/build/pdf.mjs', () => {
  const fakePage = {
    getViewport: vi.fn(() => ({ width: 100, height: 100 })),
    render: vi.fn(() => ({ promise: Promise.resolve() })),
  };
  const fakePdf = {
    numPages: 1,
    getPage: vi.fn().mockResolvedValue(fakePage),
  };
  return {
    GlobalWorkerOptions: { workerSrc: '' },
    version: '3.x',
    getDocument: vi.fn(() => ({ promise: Promise.resolve(fakePdf) })),
  };
});

// jsdom does not implement canvas 2D context. Guard the render code against a
// null context (the component already does `if (!ctx) continue`), and also
// stub getContext so it returns a minimal object to avoid any uncaught errors
// from libraries that probe capabilities.
beforeEach(() => {
  HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
    clearRect: vi.fn(),
    drawImage: vi.fn(),
    fillRect: vi.fn(),
    putImageData: vi.fn(),
    createImageData: vi.fn(),
    scale: vi.fn(),
    save: vi.fn(),
    restore: vi.fn(),
    transform: vi.fn(),
    fillText: vi.fn(),
  });
});

describe('WritingStimulusViewer', () => {
  it('calls fetchAuthorizedObjectUrl with the provided downloadPath', async () => {
    const { fetchAuthorizedObjectUrl } = await import('@/lib/api');

    render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);

    await waitFor(() => {
      expect(fetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/abc123/content');
    });
  });

  it('renders without throwing', async () => {
    expect(() =>
      render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" title="Test PDF" />),
    ).not.toThrow();

    // Wait for loading state to appear then resolve.
    await waitFor(() => {
      // Either the loading text appeared and went away, or it was fast enough
      // to skip — either way the component should not be in an error state.
      expect(screen.queryByText(/error/i)).not.toBeInTheDocument();
    });
  });

  it('shows the provided title in the toolbar', async () => {
    render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" title="My Stimulus" />);
    expect(screen.getByText('My Stimulus')).toBeInTheDocument();
  });

  it('shows an error message when the authenticated PDF fetch fails', async () => {
    const api = await import('@/lib/api');
    vi.mocked(api.fetchAuthorizedObjectUrl).mockRejectedValueOnce(new Error('forbidden'));

    render(<WritingStimulusViewer downloadPath="/v1/media/denied/content" />);

    await waitFor(() => {
      expect(screen.getByText('forbidden')).toBeInTheDocument();
    });
  });

  it('prevents contextmenu default on the root container', async () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;

    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    root.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('prevents dragstart default on the root container', async () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;

    // jsdom does not define DragEvent; use a plain MouseEvent with type
    // 'dragstart' — the React synthetic onDragStart handler calls preventDefault
    // on the native event regardless of its concrete class.
    const event = new MouseEvent('dragstart', { bubbles: true, cancelable: true });
    root.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('prevents Ctrl+S from propagating (keyboard exfil block)', () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;

    const event = new KeyboardEvent('keydown', {
      key: 's',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    root.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('prevents Ctrl+P from propagating (keyboard exfil block)', () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;

    const event = new KeyboardEvent('keydown', {
      key: 'p',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    root.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('prevents Ctrl+C from propagating (keyboard exfil block)', () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;

    const event = new KeyboardEvent('keydown', {
      key: 'c',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    root.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('carries the print:hidden class on the root element', () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;
    expect(root.classList.contains('print:hidden')).toBe(true);
  });

  it('carries the select-none class on the root element', () => {
    const { container } = render(<WritingStimulusViewer downloadPath="/v1/media/abc123/content" />);
    const root = container.firstElementChild as HTMLElement;
    expect(root.classList.contains('select-none')).toBe(true);
  });
});
