import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MessageStream } from '../MessageStream';
import type { ConsoleItem } from '@/lib/owner-agent/event-reducer';

const base = { seq: 1, ts: '2026-09-27T10:00:00.000Z', turnId: 't1' };

describe('MessageStream', () => {
  it('renders untrusted assistant markdown without raw HTML, remote images or clickable links', () => {
    const items: ConsoleItem[] = [
      {
        ...base,
        key: 'assistant:m1',
        kind: 'assistant',
        messageId: 'm1',
        streaming: false,
        text: [
          '**Done.** Click [here](javascript:alert(1)) or [docs](https://evil.example/login).',
          '![tracker](https://evil.example/pixel.png)',
          '<script>alert(1)</script><img src=x onerror=alert(2)>',
          'Hidden ‮txt.exe',
        ].join('\n\n'),
      },
    ];
    const { container } = render(<MessageStream items={items} pendingApprovals={[]} />);
    const message = screen.getByTestId('assistant-message');

    expect(message.querySelector('strong')).toHaveTextContent('Done.');
    expect(container.querySelectorAll('a')).toHaveLength(0);
    expect(container.querySelectorAll('img')).toHaveLength(0);
    expect(container.querySelectorAll('script')).toHaveLength(0);
    expect(message).toHaveTextContent('here (javascript:alert(1))');
    expect(message).toHaveTextContent('<script>alert(1)</script>');
    expect(message).toHaveTextContent('[image: tracker] (https://evil.example/pixel.png)');
    expect(message.textContent).not.toContain('‮');
    expect(message).toHaveTextContent('‹U+202E RLO›');
  });

  it('does not produce a link from markdown crafted against the link rewrite', () => {
    const items: ConsoleItem[] = [
      {
        ...base,
        key: 'assistant:m2',
        kind: 'assistant',
        messageId: 'm2',
        streaming: false,
        text: ['[click](https://evil.example/`)` tail', '[multi', 'line](//evil.example/x)', '[pr](//evil.example)'].join('\n'),
      },
    ];
    const { container } = render(<MessageStream items={items} pendingApprovals={[]} />);
    expect(container.querySelectorAll('a')).toHaveLength(0);
  });

  it('renders thinking collapsed once finished and tool cards with their output', () => {
    const items: ConsoleItem[] = [
      { ...base, key: 'thinking:1', kind: 'thinking', text: 'Consider the schema', streaming: false },
      {
        ...base,
        seq: 2,
        key: 'tool:tc-1',
        kind: 'tool',
        toolCallId: 'tc-1',
        name: 'Bash',
        input: { command: 'git status' },
        command: 'git status',
        streamedOutput: 'On branch agent/x',
        outputTruncated: false,
        status: 'error',
        result: { ok: false, output: 'fatal: not a git repository', exitCode: 128 },
      },
    ];
    render(<MessageStream items={items} pendingApprovals={[]} />);

    expect(screen.getByText('Thinking (summarized)').closest('details')).not.toHaveAttribute('open');
    expect(screen.getByTestId('tool-call-card')).toHaveTextContent('Failed · exit 128');
    expect(screen.getByTestId('tool-output')).toHaveTextContent('fatal: not a git repository');
  });

  it('only makes still-pending approvals actionable', () => {
    const request = {
      approvalId: 'ap-1',
      nonce: 'n',
      toolCallId: 'tc-1',
      summary: 'Drop table',
      command: 'psql -c "DROP TABLE x"',
      uid: 10002,
      reasons: [],
      tainted: false,
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    };
    const items: ConsoleItem[] = [{ ...base, key: 'approval:ap-1', kind: 'approval', request }];
    const onDecide = vi.fn();

    const { rerender } = render(<MessageStream items={items} pendingApprovals={[request]} onDecide={onDecide} />);
    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument();

    rerender(<MessageStream items={items} pendingApprovals={[]} onDecide={onDecide} />);
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });
});
