'use client';

import { motion, AnimatePresence } from 'motion/react';
import { Loader2, Volume2 } from 'lucide-react';
import { resolveApiMediaUrl } from '@/lib/media-url';
import type { ConversationTurnState, PartialTranscriptDraft } from '@/lib/types/conversation';

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
  partialTranscript?: PartialTranscriptDraft | null;
  turnState?: ConversationTurnState;
  onReplay?: (turn: ChatTurn) => void;
}

export function ConversationChatView({ turns, aiThinking, aiSpeakingTurn, partialTranscript, turnState, onReplay }: Props) {
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
                  ? 'bg-primary text-white dark:bg-violet-700 rounded-br-sm'
                  : 'bg-background-light text-navy rounded-bl-sm border border-border'
              }`}>
                <div className="mb-1 flex items-center justify-between gap-2 text-xs font-semibold opacity-70">
                  <span>{isLearner ? 'You' : 'AI Partner'}</span>
                  {isSpeaking && (
                    <span className="flex items-center gap-1 text-white/80">
                      <Volume2 className="h-3 w-3" /> speaking…
                    </span>
                  )}
                </div>
                <p className="text-sm leading-relaxed">{turn.content}</p>
                {turn.audioUrl && !isLearner && onReplay && (
                  <button type="button" onClick={() => onReplay(turn)}
                    aria-label={`Replay AI partner turn ${turn.turnNumber}`}
                    className="mt-2 inline-flex items-center gap-1 rounded-full bg-white/15 px-2 py-0.5 text-xs font-medium hover:bg-white/25">
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
        {partialTranscript && (
          <motion.div
            key={`partial-${partialTranscript.turnClientId}`}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            className="flex justify-end"
            aria-live="off">
            <div className="max-w-[75%] rounded-2xl rounded-br-sm border border-primary/20 bg-primary/5 px-4 py-3 text-navy shadow-sm">
              <div className="mb-1 flex items-center justify-between gap-2 text-xs font-semibold text-primary">
                <span>You</span>
                <span>{turnState === 'listening' ? 'live transcript' : 'not submitted yet'}</span>
              </div>
              <p className="text-sm leading-relaxed">{partialTranscript.text}</p>
              <span className="sr-only" aria-live="polite">Live transcript: {partialTranscript.text}</span>
            </div>
          </motion.div>
        )}
        {aiThinking && (
          <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="flex justify-start" aria-hidden="true">
            <div className="rounded-2xl rounded-bl-sm border border-border bg-background-light px-4 py-3">
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
