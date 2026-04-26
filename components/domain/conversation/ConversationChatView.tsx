'use client';

import { motion, AnimatePresence } from 'motion/react';
import { Loader2, Volume2 } from 'lucide-react';
import { resolveApiMediaUrl } from '@/lib/media-url';

export interface ChatTurn {
  turnNumber: number;
  role: 'learner' | 'ai' | 'system';
  content: string;
  timestamp: number;
  audioUrl?: string | null;
  appliedRuleIds?: string[];
}

interface Props {
  turns: ChatTurn[];
  aiThinking: boolean;
  aiSpeakingTurn?: number | null;
  onReplay?: (turn: ChatTurn) => void;
}

export function ConversationChatView({ turns, aiThinking, aiSpeakingTurn, onReplay }: Props) {
  return (
    <div className="flex-1 overflow-y-auto space-y-3 px-1 pb-4" role="log" aria-live="polite" aria-atomic="false">
      <AnimatePresence>
        {turns.map((turn, i) => {
          const isLearner = turn.role === 'learner';
          const isSpeaking = !isLearner && aiSpeakingTurn === turn.turnNumber;
          return (
            <motion.div key={`${turn.turnNumber}-${i}`}
              initial={{ opacity: 0, y: 12, scale: 0.97 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              transition={{ type: 'spring', damping: 20 }}
              className={`flex ${isLearner ? 'justify-end' : 'justify-start'}`}>
              <div className={`max-w-[75%] rounded-2xl px-4 py-3 ${
                isLearner
                  ? 'bg-purple-600 text-white rounded-br-sm'
                  : 'bg-background-light dark:bg-gray-800 text-navy dark:text-white rounded-bl-sm border border-border dark:border-gray-700'
              }`}>
                <div className="mb-1 flex items-center justify-between gap-2 text-xs font-semibold opacity-70">
                  <span>{isLearner ? 'You' : 'AI Partner'}</span>
                  {isSpeaking && (
                    <span className="flex items-center gap-1 text-purple-200">
                      <Volume2 className="h-3 w-3" /> speaking…
                    </span>
                  )}
                </div>
                <p className="text-sm leading-relaxed">{turn.content}</p>
                {turn.audioUrl && !isLearner && onReplay && (
                  <button type="button" onClick={() => onReplay(turn)}
                    className="mt-2 inline-flex items-center gap-1 rounded-full bg-black/10 px-2 py-0.5 text-xs font-medium hover:bg-black/20 dark:bg-white/10">
                    <Volume2 className="h-3 w-3" /> Replay
                  </button>
                )}
                {turn.audioUrl && isLearner && (
                  <audio src={resolveApiMediaUrl(turn.audioUrl) ?? undefined} controls preload="none" className="mt-2 w-full" />
                )}
              </div>
            </motion.div>
          );
        })}
        {aiThinking && (
          <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="flex justify-start" aria-hidden="true">
            <div className="rounded-2xl rounded-bl-sm border border-border bg-background-light px-4 py-3 dark:border-gray-700 dark:bg-gray-800">
              <div className="flex items-center gap-2 text-sm text-muted">
                <Loader2 className="h-4 w-4 animate-spin" />AI is thinking…
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
