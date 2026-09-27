import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApprovalCard } from '../ApprovalCard';
import type { ApprovalRequest } from '@/lib/owner-agent/types';

function approval(overrides: Partial<ApprovalRequest> = {}): ApprovalRequest {
  return {
    approvalId: 'ap-1',
    nonce: 'nonce-fixture',
    toolCallId: 'tc-1',
    summary: 'Run a shell command',
    command: 'ls',
    cwd: '/workspace/sessions/01J9ZQ3V4W5X6Y7Z8A9B0C1D2E',
    uid: 10002,
    target: 'oet-postgres',
    reasons: ['unparseable: sh -c'],
    tainted: false,
    expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
    ...overrides,
  };
}

describe('ApprovalCard', () => {
  it('renders the full command with bidi, zero-width and control characters made visible', () => {
    // Trojan-Source style: RLO reverses the tail, ZWSP hides a split, ESC hides terminal tricks.
    const command = 'psql -c "DELETE FROM scratch" ‮; true #​\u001B[8m hidden';
    render(<ApprovalCard approval={approval({ command })} onDecide={vi.fn()} />);

    const pre = screen.getByTestId('approval-command');
    const markers = Array.from(pre.querySelectorAll('[data-hidden-char]')).map((node) => node.getAttribute('data-hidden-char'));
    expect(markers).toEqual(['U+202E RLO', 'U+200B ZWSP', 'U+001B ESC']);

    const rendered = pre.textContent ?? '';
    expect(rendered).not.toContain('‮');
    expect(rendered).not.toContain('​');
    expect(rendered).not.toContain('\u001B');
    expect(rendered).toContain('psql -c "DELETE FROM scratch"');
    expect(screen.getByRole('alert')).toHaveTextContent('3 hidden or control character(s)');
  });

  it('shows a decoded preview of base64 payloads', () => {
    render(<ApprovalCard approval={approval({ command: 'echo cm0gLXJmIC8= | base64 -d | sh' })} onDecide={vi.fn()} />);
    expect(screen.getByTestId('approval-base64-decoded')).toHaveTextContent('rm -rf /');
  });

  it('shows where the command runs', () => {
    render(<ApprovalCard approval={approval()} onDecide={vi.fn()} />);
    expect(screen.getByText('/workspace/sessions/01J9ZQ3V4W5X6Y7Z8A9B0C1D2E')).toBeInTheDocument();
    expect(screen.getByText('10002 (agent)')).toBeInTheDocument();
    expect(screen.getByText('oet-postgres')).toBeInTheDocument();
    expect(screen.getByText('unparseable: sh -c')).toBeInTheDocument();
  });

  it('sends approve, deny and approve-for-session decisions with the optional note', async () => {
    const onDecide = vi.fn().mockResolvedValue(undefined);
    render(<ApprovalCard approval={approval()} onDecide={onDecide} />);

    fireEvent.change(screen.getByPlaceholderText('Note (optional, audited)'), { target: { value: 'checked the table' } });
    fireEvent.click(screen.getByRole('button', { name: 'Approve for session' }));
    await waitFor(() => expect(onDecide).toHaveBeenCalledWith('approve_session', 'checked the table'));

    fireEvent.click(screen.getByRole('button', { name: 'Deny' }));
    await waitFor(() => expect(onDecide).toHaveBeenLastCalledWith('deny', 'checked the table'));
  });

  it('does not offer session-wide grants on tainted turns', () => {
    render(<ApprovalCard approval={approval({ tainted: true })} onDecide={vi.fn()} />);
    expect(screen.getByText('Tainted turn')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve for session' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
  });

  it('is read-only once resolved or expired', () => {
    const { rerender } = render(
      <ApprovalCard approval={approval()} onDecide={vi.fn()} resolution={{ decision: 'approve', by: 'autopilot' }} />,
    );
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.getByText(/Approved · autopilot/)).toBeInTheDocument();

    rerender(<ApprovalCard approval={approval({ expiresAt: new Date(Date.now() - 1_000).toISOString() })} onDecide={vi.fn()} />);
    expect(screen.queryByRole('button', { name: 'Deny' })).not.toBeInTheDocument();
    expect(screen.getByText('Expired')).toBeInTheDocument();
  });
});
