'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Archive, Download, Bot, User, Wrench, ShieldCheck } from 'lucide-react';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
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
        className={`max-w-[80%] rounded-xl px-4 py-3 ${
          isUser
            ? 'bg-violet-500/20 text-admin-text'
            : isTool
              ? 'border border-amber-500/30 bg-amber-500/10 text-admin-text'
              : isSystem
                ? 'border border-admin-border bg-admin-surface-raised text-admin-text-muted italic'
                : 'border border-admin-border bg-admin-surface-raised text-admin-text'
        }`}
      >
        <div className="mb-1 flex items-center gap-2">
          {isUser ? (
            <User className="h-3.5 w-3.5 text-violet-400" />
          ) : isTool ? (
            <Wrench className="h-3.5 w-3.5 text-amber-400" />
          ) : (
            <Bot className="h-3.5 w-3.5 text-blue-400" />
          )}
          <span className="text-xs font-bold uppercase tracking-wide text-admin-text-muted">
            {message.role}
          </span>
          <span className="text-xs text-admin-text-muted">
            {new Date(message.createdAt).toLocaleTimeString()}
          </span>
        </div>

        <div className="whitespace-pre-wrap text-sm leading-relaxed">{message.content}</div>

        {message.toolCalls && message.toolCalls.length > 0 && (
          <div className="mt-2 space-y-1">
            {message.toolCalls.map((tc) => (
              <div key={tc.id} className="rounded-lg border border-admin-border bg-admin-surface p-2">
                <p className="text-xs font-bold text-amber-400">
                  <Wrench className="mr-1 inline-block h-3 w-3" />
                  Tool Call: {tc.name}
                </p>
                <pre className="mt-1 overflow-x-auto text-xs text-admin-text-muted">{tc.arguments}</pre>
              </div>
            ))}
          </div>
        )}

        {message.toolResult && (
          <div className="mt-2 rounded-lg border border-admin-border bg-admin-surface p-2">
            <p className="text-xs font-bold text-emerald-400">
              <Wrench className="mr-1 inline-block h-3 w-3" />
              Result: {message.toolResult.name}
            </p>
            <pre className="mt-1 max-h-40 overflow-auto text-xs text-admin-text-muted">{message.toolResult.result}</pre>
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
      // Could add toast here
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

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<ShieldCheck className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      {/* Breadcrumb */}
      <div className="flex items-center gap-2 text-sm">
        <Link href="/admin/ai-assistant/threads" className="flex items-center gap-1 text-admin-text-muted hover:text-admin-text transition-colors">
          <ArrowLeft className="h-3.5 w-3.5" />
          Back to Threads
        </Link>
      </div>

      <AsyncStateWrapper status={status} onRetry={loadThread}>
        {thread && (
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_280px]">
            {/* Messages panel */}
            <AdminRoutePanel>
              <div className="p-4">
                <div className="mb-4 flex items-center justify-between">
                  <h2 className="text-sm font-bold text-admin-text">{thread.title || 'Untitled Thread'}</h2>
                  <div className="flex items-center gap-2">
                    <Button variant="ghost" onClick={handleExport} className="h-7 gap-1.5 px-2 text-xs">
                      <Download className="h-3.5 w-3.5" />
                      Export
                    </Button>
                    {thread.status === 'active' && (
                      <Button variant="destructive" onClick={handleArchive} disabled={archiving} className="h-7 gap-1.5 px-2 text-xs">
                        <Archive className="h-3.5 w-3.5" />
                        {archiving ? 'Archiving…' : 'Archive'}
                      </Button>
                    )}
                  </div>
                </div>

                <div className="space-y-3">
                  {messages.length === 0 ? (
                    <p className="py-8 text-center text-sm text-admin-text-muted">No messages in this thread.</p>
                  ) : (
                    messages.map((msg) => <MessageBubble key={msg.id} message={msg} />)
                  )}
                </div>
              </div>
            </AdminRoutePanel>

            {/* User info sidebar */}
            <div className="flex flex-col gap-3">
              <AdminRoutePanel>
                <div className="p-4">
                  <h3 className="text-xs font-bold uppercase tracking-wide text-admin-text-muted">User Info</h3>
                  <div className="mt-3 space-y-2">
                    <div>
                      <p className="text-xs text-admin-text-muted">Name</p>
                      <p className="text-sm font-medium text-admin-text">{thread.userName}</p>
                    </div>
                    <div>
                      <p className="text-xs text-admin-text-muted">Email</p>
                      <p className="text-sm text-admin-text">{thread.userEmail}</p>
                    </div>
                    <div>
                      <p className="text-xs text-admin-text-muted">Role</p>
                      <Badge variant="info">{thread.userRole}</Badge>
                    </div>
                  </div>
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel>
                <div className="p-4">
                  <h3 className="text-xs font-bold uppercase tracking-wide text-admin-text-muted">Thread Info</h3>
                  <div className="mt-3 space-y-2">
                    <div>
                      <p className="text-xs text-admin-text-muted">Status</p>
                      <Badge variant={thread.status === 'active' ? 'success' : thread.status === 'archived' ? 'default' : 'danger'}>
                        {thread.status}
                      </Badge>
                    </div>
                    <div>
                      <p className="text-xs text-admin-text-muted">Messages</p>
                      <p className="text-sm font-medium text-admin-text">{thread.messageCount}</p>
                    </div>
                    <div>
                      <p className="text-xs text-admin-text-muted">Created</p>
                      <p className="text-sm text-admin-text">{new Date(thread.createdAt).toLocaleString()}</p>
                    </div>
                    <div>
                      <p className="text-xs text-admin-text-muted">Last Active</p>
                      <p className="text-sm text-admin-text">{new Date(thread.lastActiveAt).toLocaleString()}</p>
                    </div>
                    <div>
                      <p className="text-xs text-admin-text-muted">Thread ID</p>
                      <p className="truncate text-xs font-mono text-admin-text-muted">{thread.id}</p>
                    </div>
                  </div>
                </div>
              </AdminRoutePanel>
            </div>
          </div>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
