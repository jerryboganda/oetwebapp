import { describe, expect, it } from 'vitest';
import { HttpTransportType } from '@microsoft/signalr';
import { AI_ASSISTANT_SIGNALR_TRANSPORT, normaliseStreamFrame } from './signalr';

describe('normaliseStreamFrame', () => {
  it('normalises backend casing and snake_case frame discriminators', () => {
    expect(normaliseStreamFrame({ Type: 'token_delta', ThreadId: 't1', MessageId: 'm1', Delta: 'hello' })).toEqual({
      type: 'TokenDelta',
      threadId: 't1',
      messageId: 'm1',
      delta: 'hello',
    });
  });

  it.each([
    ['message_start', 'MessageStart'],
    ['token_delta', 'TokenDelta'],
    ['tool_call_start', 'ToolCallStart'],
    ['tool_call_delta', 'ToolCallDelta'],
    ['tool_call_result', 'ToolCallResult'],
    ['approval_request', 'ApprovalRequest'],
    ['message_end', 'MessageEnd'],
    ['error', 'Error'],
  ])('maps %s to %s', (wireType, clientType) => {
    expect(normaliseStreamFrame({ type: wireType })).toEqual({ type: clientType });
  });
});

describe('AI Assistant SignalR transport', () => {
  it('uses long polling when routed through the same-origin backend proxy', () => {
    expect(AI_ASSISTANT_SIGNALR_TRANSPORT).toBe(HttpTransportType.LongPolling);
  });
});