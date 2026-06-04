import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { WritingStimulus } from '../WritingStimulus';
import type { WritingScenarioDto } from '@/lib/writing/types';

// ── Mock @/lib/api ───────────────────────────────────────────────────────────
vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: vi.fn().mockResolvedValue('blob:fake-url'),
}));

// ── Mock pdfjs-dist ──────────────────────────────────────────────────────────
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

beforeEach(() => {
  vi.clearAllMocks();
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

/** Minimal scenario with a stimulus PDF path. */
const scenarioWithPdf: WritingScenarioDto = {
  id: 'scen-1',
  title: 'Letter to Dr Smith',
  letterType: 'LT-RR',
  profession: 'medicine',
  subDiscipline: null,
  topics: [],
  difficulty: 3,
  caseNotesMarkdown: '## Patient\n- John Doe\n',
  caseNotesStructured: [],
  isDiagnostic: false,
  status: 'published',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
  stimulusPdfDownloadPath: '/v1/media/pdf-abc/content',
};

/** Minimal scenario WITHOUT a stimulus PDF (text fallback path). */
const scenarioTextOnly: WritingScenarioDto = {
  ...scenarioWithPdf,
  stimulusPdfDownloadPath: null,
  caseNotesMarkdown: '## Patient Details\n- Jane Doe\n- DOB: 01/01/1990\n',
};

describe('WritingStimulus', () => {
  describe('PDF path — when stimulusPdfDownloadPath is set', () => {
    it('calls fetchAuthorizedObjectUrl with the stimulus PDF path', async () => {
      const { fetchAuthorizedObjectUrl } = await import('@/lib/api');

      render(<WritingStimulus scenario={scenarioWithPdf} />);

      await waitFor(() => {
        expect(fetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/pdf-abc/content');
      });
    });

    it('renders the PDF viewer (not the text fallback)', async () => {
      render(<WritingStimulus scenario={scenarioWithPdf} title="Stimulus PDF" />);

      // The PDF viewer toolbar should appear; case-note sections should not.
      expect(screen.getByText('Stimulus PDF')).toBeInTheDocument();
      // The text fallback would show case-note section headings in the DOM.
      expect(screen.queryByText('Patient Details')).not.toBeInTheDocument();
    });
  });

  describe('Text fallback — when stimulusPdfDownloadPath is null', () => {
    it('does NOT call fetchAuthorizedObjectUrl', async () => {
      const { fetchAuthorizedObjectUrl } = await import('@/lib/api');

      render(<WritingStimulus scenario={scenarioTextOnly} />);

      // Allow a tick for any async work to settle.
      await waitFor(() => {
        expect(fetchAuthorizedObjectUrl).not.toHaveBeenCalled();
      });
    });

    it('renders case-note text from the scenario markdown', () => {
      render(<WritingStimulus scenario={scenarioTextOnly} />);

      // CaseNotePdfViewer parses the markdown and renders section headings.
      expect(screen.getByText('Patient Details')).toBeInTheDocument();
    });
  });

  describe('Null scenario', () => {
    it('renders the text fallback with empty content when scenario is null', () => {
      // Should not crash; CaseNotePdfViewer handles missing content gracefully.
      expect(() => render(<WritingStimulus scenario={null} />)).not.toThrow();
    });
  });
});
