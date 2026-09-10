'use client';

import { MarkdownContent } from '@/components/ui/markdown-content';
import type { AiMessage, MessageCitation } from '@/lib/ai-assistant/types';

export interface AiAssistantMessagesProps {
  messages: AiMessage[];
  streamingContent?: string;
  /** Sources for the turn currently streaming. */
  streamingCitations?: MessageCitation[];
}

/**
 * Authority class → the short label a learner can actually act on. An official
 * exam fact and Dr Hesham's teaching method are different kinds of claim, and
 * the source specification is explicit that the two must not read alike.
 */
const AUTHORITY_LABELS: Record<string, string> = {
  OfficialCurrentFact: 'Official exam fact',
  DrHeshamApprovedMethod: 'Dr Hesham’s method',
  ProfessionApprovedMethod: 'Profession rulebook',
  CourseMaterial: 'Course material',
  PlatformSupport: 'Platform',
  CandidateEvidence: 'Your own work',
  AdminOverride: 'Correction',
};

export function AiAssistantMessages({
  messages,
  streamingContent,
  streamingCitations,
}: AiAssistantMessagesProps) {
  const isStreaming = streamingContent !== undefined;

  if (messages.length === 0 && !isStreaming) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted">
        Start a conversation with the AI assistant
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {messages.map((msg) => (
        <MessageBubble key={msg.id} message={msg} />
      ))}
      {streamingContent !== undefined && (
        <div className="rounded-lg bg-background-light p-3" data-testid="streaming-message">
          <MarkdownContent markdown={streamingContent} className="prose prose-sm max-w-none" />
          <span className="inline-block h-4 w-1 animate-pulse bg-primary" data-testid="streaming-cursor" />
          <CitationList citations={streamingCitations} />
        </div>
      )}
    </div>
  );
}

function MessageBubble({ message }: { message: AiMessage }) {
  const isUser = message.role === 'user';
  const isTool = message.role === 'tool';

  if (isTool) {
    // A tool-role message carries its name on the wire DTO's scalar `toolName`
    // field (AiAssistantMessageDto), not on `toolCalls` — that array is only
    // ever populated on the preceding assistant message that issued the call.
    // `AiMessage` doesn't model this scalar, so it's read off the raw payload
    // rather than the always-empty `toolCalls[0]`.
    const toolName = (message as AiMessage & { toolName?: string }).toolName;
    return (
      <div className="rounded-lg border border-border bg-background-light p-3" data-testid="tool-call-card">
        <div className="text-xs font-medium text-muted mb-1">
          Tool: {toolName ?? 'unknown'}
        </div>
        <pre className="text-xs overflow-x-auto">{message.content}</pre>
      </div>
    );
  }

  return (
    <div
      className={`rounded-lg p-3 ${isUser ? 'ml-8 bg-primary/10' : 'mr-8 bg-background-light'}`}
      data-testid={isUser ? 'user-message' : 'assistant-message'}
    >
      {/*
        Assistant output is markdown (the companion cites rules, lists steps and
        quotes short examples). The learner's own message is deliberately NOT
        parsed as markdown: it is untrusted input, and echoing it through a
        renderer changes what the learner sees themselves type.
      */}
      {isUser ? (
        <div className="whitespace-pre-wrap text-sm">{message.content}</div>
      ) : (
        <MarkdownContent markdown={message.content} className="prose prose-sm max-w-none" />
      )}
      {!isUser && <CitationList citations={message.citations} />}
    </div>
  );
}

/**
 * The sources behind an answer, labelled by authority.
 *
 * Deliberately shows the source name, rule heading and location only — never the
 * source text. A citation says where a claim came from; reprinting the passage
 * here would turn the citation list into a second delivery channel for paid
 * material, which is exactly what the retriever's verbatim caps exist to prevent.
 */
function CitationList({ citations }: { citations?: MessageCitation[] }) {
  if (!citations || citations.length === 0) return null;

  return (
    <div className="mt-3 border-t border-border pt-2" data-testid="message-citations">
      <p className="text-xs font-semibold text-muted">Based on</p>
      <ul className="mt-1 space-y-1">
        {citations.map((citation) => (
          <li key={`${citation.ordinal}:${citation.sourceKey}`} className="text-xs text-muted">
            <span className="font-medium text-navy">[S{citation.ordinal}]</span>{' '}
            {citation.sourceTitle}
            {citation.heading ? ` — ${citation.heading}` : ''}
            {citation.pageNumber !== null && citation.pageNumber !== undefined
              ? ` (p.${citation.pageNumber})`
              : ''}
            {citation.timestampSeconds !== null && citation.timestampSeconds !== undefined
              ? ` (${formatTimestamp(citation.timestampSeconds)})`
              : ''}
            <span className="ms-1 rounded bg-background-light px-1 py-0.5 text-[10px] uppercase tracking-wide">
              {AUTHORITY_LABELS[citation.authority] ?? citation.authority}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function formatTimestamp(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = Math.floor(totalSeconds % 60);
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}
