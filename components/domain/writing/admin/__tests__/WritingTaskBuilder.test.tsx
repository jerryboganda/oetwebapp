import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * Vitest spec for the admin Writing Task Builder (WS-F2).
 *
 * The builder owns loading/editing/validation/publish plus permission gating.
 * We mock the exam-api contract (create/update/get…) and the admin auth hooks
 * so the component renders in "new" mode with full ContentWrite + ContentPublish
 * permissions, then assert that the structured-content builders work and that
 * Save persists via createWritingTask.
 */

const {
  createWritingTask,
  updateWritingTask,
  getWritingTask,
  validateWritingTask,
  publishWritingTask,
  archiveWritingTask,
  cloneWritingTask,
  exportWritingTask,
  importWritingTask,
  routerPush,
  routerReplace,
} = vi.hoisted(() => ({
  createWritingTask: vi.fn(),
  updateWritingTask: vi.fn(),
  getWritingTask: vi.fn(),
  validateWritingTask: vi.fn(),
  publishWritingTask: vi.fn(),
  archiveWritingTask: vi.fn(),
  cloneWritingTask: vi.fn(),
  exportWritingTask: vi.fn(),
  importWritingTask: vi.fn(),
  routerPush: vi.fn(),
  routerReplace: vi.fn(),
}));

vi.mock('@/lib/writing/exam-api', () => ({
  createWritingTask: (...a: unknown[]) => createWritingTask(...a),
  updateWritingTask: (...a: unknown[]) => updateWritingTask(...a),
  getWritingTask: (...a: unknown[]) => getWritingTask(...a),
  validateWritingTask: (...a: unknown[]) => validateWritingTask(...a),
  publishWritingTask: (...a: unknown[]) => publishWritingTask(...a),
  archiveWritingTask: (...a: unknown[]) => archiveWritingTask(...a),
  cloneWritingTask: (...a: unknown[]) => cloneWritingTask(...a),
  exportWritingTask: (...a: unknown[]) => exportWritingTask(...a),
  importWritingTask: (...a: unknown[]) => importWritingTask(...a),
}));

// The builder imports ApiError from @/lib/api; keep the real class so
// `instanceof` checks still work, but avoid touching the network layer.
vi.mock('@/lib/api', () => ({
  ApiError: class ApiError extends Error {
    status: number;
    code: string;
    fieldErrors: Array<{ field: string; code: string; message: string }>;
    constructor(message: string) {
      super(message);
      this.status = 400;
      this.code = 'validation_error';
      this.fieldErrors = [];
    }
  },
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({ isAuthenticated: true, role: 'admin', isLoading: false }),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({
  useCurrentUser: () => ({
    user: { adminPermissions: ['content:write', 'content:publish'] },
  }),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: routerReplace }),
}));

import { WritingTaskBuilder } from '../WritingTaskBuilder';

function makeTaskDto(overrides: Record<string, unknown> = {}) {
  return {
    id: 'task-123',
    internalCode: null,
    title: 'New writing task',
    profession: 'medicine',
    letterType: 'LT-RR',
    difficulty: 3,
    status: 'draft',
    version: 1,
    writerRole: null,
    todayDate: null,
    taskPromptMarkdown: '',
    recipient: { name: '', role: '', organisation: '', address: '' },
    expectedPurpose: null,
    expectedAction: null,
    caseNotesMarkdown: '',
    caseNoteSections: [],
    fixedInstructions: [],
    wordGuideMin: 180,
    wordGuideMax: 200,
    readingTimeSeconds: 300,
    writingTimeSeconds: 2400,
    simulationModes: 'both',
    markingMode: 'tutor',
    modelAnswerText: null,
    modelAnswerParagraphs: [],
    keyContentChecklist: [],
    irrelevantContentChecklist: [],
    sourceProvenance: null,
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
    ...overrides,
  };
}

describe('WritingTaskBuilder (new mode)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the core authoring sections', () => {
    render(<WritingTaskBuilder mode="new" />);

    expect(screen.getByRole('heading', { name: 'Task metadata' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Case notes' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Key content checklist' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Title')).toBeInTheDocument();
  });

  it('adds a case-note section and a key checklist item', async () => {
    const user = userEvent.setup();
    render(<WritingTaskBuilder mode="new" />);

    // No sections yet — the empty-state copy is shown.
    expect(
      screen.getByText(/No case-note sections yet/i),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '+ Add section' }));
    // The section editor exposes a "Section heading" field once added.
    expect(await screen.findByLabelText('Section heading')).toBeInTheDocument();

    // Add a key-content checklist point.
    await user.click(
      screen.getByRole('button', { name: '+ Add key-content point' }),
    );
    // Each key item renders a "Content point" textarea.
    expect(await screen.findByLabelText('Content point')).toBeInTheDocument();
  });

  it('persists via createWritingTask on Save draft', async () => {
    createWritingTask.mockResolvedValue(makeTaskDto({ id: 'task-created' }));
    const user = userEvent.setup();

    render(<WritingTaskBuilder mode="new" />);

    const titleInput = screen.getByLabelText('Title');
    fireEvent.change(titleInput, { target: { value: 'Discharge — Mr Brown' } });

    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => {
      expect(createWritingTask).toHaveBeenCalledTimes(1);
    });
    const payload = createWritingTask.mock.calls[0][0] as { title: string };
    expect(payload.title).toBe('Discharge — Mr Brown');
    // updateWritingTask must NOT be used for a brand-new task.
    expect(updateWritingTask).not.toHaveBeenCalled();
    // After create, the builder redirects into the canonical edit route.
    await waitFor(() => {
      expect(routerReplace).toHaveBeenCalledWith(
        '/admin/writing/tasks/task-created/edit',
      );
    });
  });

  it('updates an existing task via updateWritingTask on Save draft', async () => {
    getWritingTask.mockResolvedValue(makeTaskDto({ id: 'task-123', title: 'Existing' }));
    updateWritingTask.mockResolvedValue(makeTaskDto({ id: 'task-123', title: 'Existing v2' }));
    const user = userEvent.setup();

    render(<WritingTaskBuilder taskId="task-123" mode="edit" />);

    // Wait for the load to populate the form.
    const titleInput = (await screen.findByLabelText('Title')) as HTMLInputElement;
    await waitFor(() => expect(titleInput.value).toBe('Existing'));

    fireEvent.change(titleInput, { target: { value: 'Existing v2' } });
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => {
      expect(updateWritingTask).toHaveBeenCalledTimes(1);
    });
    expect(updateWritingTask.mock.calls[0][0]).toBe('task-123');
    expect(createWritingTask).not.toHaveBeenCalled();
  });
});
