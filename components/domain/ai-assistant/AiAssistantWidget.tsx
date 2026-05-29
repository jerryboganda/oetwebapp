'use client';

import { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import { canAccessAiAssistant } from '@/lib/ai-assistant/permissions';
import type { UserRole } from '@/lib/types/auth';
import { AiAssistantPanel } from './AiAssistantPanel';

export interface AiAssistantWidgetProps {
  role: UserRole | null | undefined;
  hasNotification?: boolean;
}

export function AiAssistantWidget({ role, hasNotification = false }: AiAssistantWidgetProps) {
  const [isOpen, setIsOpen] = useState(false);

  if (!canAccessAiAssistant(role)) return null;

  return (
    <>
      <button
        onClick={() => setIsOpen((prev) => !prev)}
        className="fixed bottom-6 right-6 z-50 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-white shadow-lg hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 transition-[color,background-color,transform] duration-200"
        aria-label="Toggle AI Assistant"
      >
        <MessageSquare className="h-6 w-6" />
        {hasNotification && (
          <span
            className="absolute -top-1 -right-1 h-3 w-3 rounded-full bg-red-500"
            data-testid="notification-indicator"
          />
        )}
      </button>
      {isOpen && <AiAssistantPanel onClose={() => setIsOpen(false)} />}
    </>
  );
}
