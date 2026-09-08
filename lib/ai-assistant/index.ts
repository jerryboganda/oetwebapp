export { type AiMessage, type AiThread, type StreamEvent, type AiAssistantAccess } from './types';
export { isContentDelta, isToolCallStart, isStreamError, isStreamDone, isUserMessage, isAssistantMessage } from './types';
// `sendMessage` is gone: there is no POST /threads/{id}/messages route. A learner
// message is sent over SignalR via `StartTurn`, which is what starts the stream.
export { createThread, listThreads, getMessages, archiveThread, renameThread, setThreadModel, listAssistantModels } from './api';
export type { AssistantModelOption } from './api';
export { getAiAssistantAccess, canAccessAiAssistant } from './permissions';
