'use client';

import { useAiAssistantContext } from '@/contexts/ai-assistant-context';

export function ThreadSidebar(): React.JSX.Element {
  const { threads, activeThread, selectThread, createThread, deleteThread } = useAiAssistantContext();

  return (
    <aside className="hidden w-48 shrink-0 overflow-y-auto border-r border-slate-200 bg-slate-50 px-2 py-3 text-sm md:block">
      <button
        type="button"
        onClick={() => { void createThread(); }}
        className="w-full rounded bg-slate-900 px-2 py-1.5 text-xs font-medium text-white hover:bg-slate-800"
        data-testid="ai-assistant-new-thread"
      >
        + New thread
      </button>
      <ul className="mt-3 space-y-1">
        {threads.length === 0 ? (
          <li className="text-xs text-slate-400">No threads</li>
        ) : (
          threads.map((t) => {
            const active = activeThread?.id === t.id;
            return (
              <li key={t.id} className="group flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => { void selectThread(t.id); }}
                  className={`flex-1 truncate rounded px-2 py-1 text-left text-xs ${active ? 'bg-slate-200 font-semibold text-slate-900' : 'text-slate-700 hover:bg-slate-100'}`}
                  title={t.title}
                >
                  {t.title}
                </button>
                <button
                  type="button"
                  onClick={() => { void deleteThread(t.id); }}
                  aria-label={`Delete ${t.title}`}
                  className="invisible rounded px-1 text-slate-400 hover:text-rose-600 group-hover:visible"
                >
                  ×
                </button>
              </li>
            );
          })
        )}
      </ul>
    </aside>
  );
}
