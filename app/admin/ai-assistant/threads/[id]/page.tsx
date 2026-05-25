'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { Archive, Download, Bot, User, Wrench, ShieldCheck } from 'lucide-react';
import { AdminOperationsLayout, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { apiClient } from '@/lib/api';

interface ThreadMessage {
  id: string;
  role: 'user' | 'assistant' | 'system' | 'tool';
  content: string;
  createdAt: string;
  toolCalls?: ToolCall[];
  toolResult?: ToolResult;
}

interface ToolCall {
  id: string;
  name: string;
  arguments: string;
}

interface ToolResult {
  toolCallId: string;
  name: string;
  result: string;
}

interface ThreadDetail {
  id: string;
  userId: string;
  userName: string;
  userEmail: string;
  userRole: string;
  title: string;
  status: 'active' | 'archived' | 'terminated';
  createdAt: string;
  lastActiveAt: string;
  messageCount: number;
}

type PageStatus = 'loading' | 'success' | 'error';

function MessageBubble({ message }: { message: ThreadMessage }) {
  const isUser = message.role === 'user';
  const isTool = message.role === 'tool';
  const isSystem = message.role === 'system';

  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
      <div
        className={`max-w-[80%] rounded-admin px-4 py-3 ${
          isUser
            ? 'bg-[var(--admin-primary-tint)] text-admin-fg-strong'
            : isTool
              ? 'border border-[var(--admin-warning)] bg-[var(--admin-warning-tint)] text-admin-fg-strong'
              : isSystem
                ? 'border border-admin-border bg-admin-bg-subtle italic text-admin-fg-muted'
                : 'border border-admin-border bg-admin-bg-surface text-admin-fg-strong'
        }`}
      >
        <div className="mb-1 flex items-center gap-2">
          {isUser ? (
            <User className="h-3.5 w-3.5 text-[var(--admin-primary)]" />
          ) : isTool ? (
            <Wrench className="h-3.5 w-3.5 text-[var(--admin-warning)]" />
          ) : (
            <Bot className="h-3.5 w-3.5 text-[var(--admin-info)]" />
          )}
          <span className="text-xs font-bold uppercase tracking-wide text-admin-fg-muted">{message.role}</span>
          <span className="text-xs text-admin-fg-muted">{new Date(message.createdAt).toLocaleTimeString()}</span>
        </div>

        <div className="whitespace-pre-wrap text-sm leading-relaxed">{message.content}</div>

        {message.toolCalls && message.toolCalls.length > 0 && (
          <div className="mt-2 space-y-1">
            {message.toolCalls.map((tc) => (
              <div key={tc.id} className="rounded-admin border border-admin-border bg-admin-bg-surface p-2">
                <p className="text-xs font-bold text-[var(--admin-warning)]">
                  <Wrench className="mr-1 inline-block h-3 w-3" />
                  Tool Call: {tc.name}
                </p>
                <pre className="mt-1 overflow-x-auto text-xs text-admin-fg-muted">{tc.arguments}</pre>
              </div>
            ))}
          </div>
        )}

        {message.toolResult && (
          <div className="mt-2 rounded-admin border border-admin-border bg-admin-bg-surface p-2">
            <p className="text-xs font-bold text-[var(--admin-success)]">
              <Wrench className="mr-1 inline-block h-3 w-3" />
              Result: {message.toolResult.name}
            </p>
            <pre className="mt-1 max-h-40 overflow-auto text-xs text-admin-fg-muted">{message.toolResult.result}</pre>
          </div>
        )}
      </div>
    </div>
  );
}

export default function AiAssistantThreadDetailPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const params = useParams();
  const threadId = (params?.id as string) ?? '';

  const [status, setStatus] = useState<PageStatus>('loading');
  const [thread, setThread] = useState<ThreadDetail | null>(null);
  const [messages, setMessages] = useState<ThreadMessage[]>([]);
  const [archiving, setArchiving] = useState(false);

  const loadThread = useCallback(async () => {
    try {
      setStatus('loading');
      const [threadData, messagesData] = await Promise.all([
        apiClient.get<ThreadDetail>(`/v1/admin/ai-assistant/threads/${threadId}`),
        apiClient.get<{ items: ThreadMessage[] }>(`/v1/admin/ai-assistant/threads/${threadId}/messages`),
      ]);
      setThread(threadData);
      setMessages(messagesData.items);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, [threadId]);

  useEffect(() => {
    if (isAuthenticated && role === 'admin' && threadId) {
      loadThread();
    }
  }, [isAuthenticated, role, threadId, loadThread]);

  const handleArchive = useCallback(async () => {
    if (!thread) return;
    try {
      setArchiving(true);
      await apiClient.delete(`/v1/admin/ai-assistant/threads/${thread.id}`);
      loadThread();
    } catch {
      // silent
    } finally {
      setArchiving(false);
    }
  }, [thread, loadThread]);

  const handleExport = useCallback(() => {
    if (!messages.length || !thread) return;
    const exportData = {
      thread: { id: thread.id, title: thread.title, user: thread.userName },
      messages: messages.map((m) => ({
        role: m.role,
        content: m.content,
        timestamp: m.createdAt,
        toolCalls: m.toolCalls,
        toolResult: m.toolResult,
      })),
    };
    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `thread-${thread.id}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }, [messages, thread]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'AI Assistant', href: '/admin' },
    { label: 'Threads', href: '/admin/ai-assistant/threads' },
    { label: thread?.title || 'Thread' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminOperationsLayout title="Thread Detail" eyebrow="AI Assistant" breadcrumbs={breadcrumbs}
        primaryGrid={
          <EmptyState
            title="Admin access required"
            description="Sign in with an admin account to view this page."
            illustration={<ShieldCheck className="h-8 w-8" />}
          />
        }
      />
    );
  }

  return (
    <AdminOperationsLayout
      title={thread?.title || 'Untitled Thread'}
      description={thread ? `Conversation transcript for ${thread.userName}` : 'Loading thread…'}
      eyebrow="AI Assistant"
      breadcrumbs={breadcrumbs}
      actions={
        thread ? (
          <>
            <Button variant="secondary" size="sm" onClick={handleExport}>
              <Download className="h-4 w-4" />
              Export
            </Button>
            {thread.status === 'active' && (
              <Button variant="destructive" size="sm" onClick={handleArchive} disabled={archiving}>
                <Archive className="h-4 w-4" />
                {archiving ? 'Archiving…' : 'Archive'}
              </Button>
            )}
          </>
        ) : null
      }
      primaryGrid={
        <AsyncStateWrapper status={status} onRetry={loadThread}>
          {thread && (
            <BentoGrid>
              <BentoCell span={{ default: 12, lg: 8 }}>
                <Card>
                  <CardHeader><CardTitle>Conversation</CardTitle></CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {messages.length === 0 ? (
                        <p className="py-8 text-center text-sm text-admin-fg-muted">No messages in this thread.</p>
                      ) : (
                        messages.map((msg) => <MessageBubble key={msg.id} message={msg} />)
                      )}
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, lg: 4 }}>
                <div className="space-y-4">
                  <Card>
                    <CardHeader>
                      <CardTitle className="text-xs uppercase tracking-wider text-admin-fg-muted">User Info</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="space-y-2">
                        <div>
                          <p className="text-xs text-admin-fg-muted">Name</p>
                          <p className="text-sm font-medium text-admin-fg-strong">{thread.userName}</p>
                        </div>
                        <div>
                          <p className="text-xs text-admin-fg-muted">Email</p>
                          <p className="text-sm text-admin-fg-strong">{thread.userEmail}</p>
                        </div>
                        <div>
                          <p className="text-xs text-admin-fg-muted">Role</p>
                          <Badge variant="primary" intensity="tinted" size="sm">{thread.userRole}</Badge>
                        </div>
                      </div>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader>
                      <CardTitle className="text-xs uppercase tracking-wider text-admin-fg-muted">Thread Info</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="space-y-2">
                        <div>
                          <p className="text-xs text-admin-fg-muted">Status</p>
                          <Badge variant={thread.status === 'active' ? 'success' : thread.status === 'archived' ? 'default' : 'danger'} intensity="tinted" size="sm">
                            {thread.status}
                          </Badge>
                        </div>
                        <div>
                          <p className="text-xs text-admin-fg-muted">Messages</p>
                          <p className="text-sm font-medium text-admin-fg-strong">{thread.messageCount}</p>
                        </div>
                        <div>
                          <p className="text-xs text-admin-fg-muted">Created</p>
                          <p className="text-sm text-admin-fg-strong">{new Date(thread.createdAt).toLocaleString()}</p>
                        </div>
                        <div>
                          <p className="text-xs text-admin-fg-muted">Last Active</p>
                          <p className="text-sm text-admin-fg-strong">{new Date(thread.lastActiveAt).toLocaleString()}</p>
                        </div>
                        <div>
                          <p className="text-xs text-admin-fg-muted">Thread ID</p>
                          <p className="truncate font-mono text-xs text-admin-fg-muted">{thread.id}</p>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                </div>
              </BentoCell>
            </BentoGrid>
          )}
        </AsyncStateWrapper>
      }
    />
  );
}
