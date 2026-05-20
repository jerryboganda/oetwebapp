'use client';

import { ThreadSidebar } from './ThreadSidebar';
import { ChatMessageList } from './ChatMessageList';
import { MessageComposer } from './MessageComposer';
import { ToolEventStream } from './ToolEventStream';
import { ApprovalModal } from './ApprovalModal';
import { SettingsQuickMenu } from './SettingsQuickMenu';

interface Props {
  onClose: () => void;
}

// TODO Phase 1: replace placeholder shell with motion/react drawer
// (right-side, 480px wide, dismiss on Esc, focus trap, aria-modal).
export function AiAssistantPanel({ onClose }: Props): React.JSX.Element {
  return (
    <div
      role="dialog"
      aria-modal
      aria-label="AI Assistant"
      className="fixed inset-y-0 right-0 z-50 flex w-full max-w-[480px] flex-col bg-white shadow-2xl ring-1 ring-slate-200"
    >
      <header className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <h2 className="text-sm font-semibold text-slate-900">AI Assistant</h2>
        <div className="flex items-center gap-2">
          <SettingsQuickMenu />
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 text-slate-500 hover:bg-slate-100"
            aria-label="Close"
          >
            ✕
          </button>
        </div>
      </header>

      <div className="flex min-h-0 flex-1">
        <ThreadSidebar />
        <main className="flex min-w-0 flex-1 flex-col">
          <ChatMessageList />
          <ToolEventStream />
          <MessageComposer />
        </main>
      </div>

      <ApprovalModal />
    </div>
  );
}
