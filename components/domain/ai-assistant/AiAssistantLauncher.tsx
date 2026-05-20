'use client';

// Floating launcher button (bottom-right). Phase 2 will add unread/badge state.

interface Props {
  onOpen: () => void;
}

export function AiAssistantLauncher({ onOpen }: Props): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onOpen}
      aria-label="Open AI Assistant"
      className="fixed bottom-6 right-6 z-50 inline-flex h-12 w-12 items-center justify-center rounded-full bg-slate-900 text-white shadow-lg hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-sky-400"
    >
      <span aria-hidden className="text-lg">AI</span>
    </button>
  );
}
