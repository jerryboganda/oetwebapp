'use client';

import { motion } from 'motion/react';
import { Headphones, MessageSquare, BookOpen, FileText, AlertTriangle, Star } from 'lucide-react';

export type RecallQuizMode =
  | 'listen_and_type'
  | 'word_recognition'
  | 'meaning_check'
  | 'clinical_sentence'
  | 'high_risk_spelling'
  | 'starred_only';

interface QuizModePickerProps {
  selected: RecallQuizMode;
  onChange: (mode: RecallQuizMode) => void;
  disabled?: boolean;
}

const MODES: { key: RecallQuizMode; label: string; description: string; icon: React.ReactNode; tile: string }[] = [
  {
    key: 'listen_and_type',
    label: 'Listen & type',
    description: 'Hear the word in British English. Type it back. Server-side diff.',
    icon: <Headphones className="h-5 w-5" />,
    tile: 'bg-primary/10 text-primary',
  },
  {
    key: 'word_recognition',
    label: 'Word recognition',
    description: 'Pick the correct word from four similar-sounding options.',
    icon: <MessageSquare className="h-5 w-5" />,
    tile: 'bg-info/10 text-info',
  },
  {
    key: 'meaning_check',
    label: 'Meaning check',
    description: 'Pick the correct definition for the term.',
    icon: <BookOpen className="h-5 w-5" />,
    tile: 'bg-emerald-50 text-emerald-700',
  },
  {
    key: 'clinical_sentence',
    label: 'Clinical sentence',
    description: 'Listen to a full clinical sentence. Type the missing word.',
    icon: <FileText className="h-5 w-5" />,
    tile: 'bg-purple-50 text-purple-700',
  },
  {
    key: 'high_risk_spelling',
    label: 'High-risk spelling',
    description: 'Diarrhoea, anaemia, haemorrhage… the British-spelling minefield.',
    icon: <AlertTriangle className="h-5 w-5" />,
    tile: 'bg-warning/10 text-warning',
  },
  {
    key: 'starred_only',
    label: 'Starred only',
    description: 'Drill the cards you marked difficult.',
    icon: <Star className="h-5 w-5" />,
    tile: 'bg-amber-50 text-amber-700',
  },
];

export function QuizModePicker({ selected, onChange, disabled }: QuizModePickerProps) {
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {MODES.map((m) => {
        const isSelected = m.key === selected;
        return (
          <motion.button
            key={m.key}
            type="button"
            disabled={disabled}
            onClick={() => onChange(m.key)}
            whileHover={{ scale: disabled ? 1 : 1.01 }}
            whileTap={{ scale: disabled ? 1 : 0.99 }}
            aria-pressed={isSelected}
            className={`flex items-start gap-3 rounded-2xl border p-4 text-left transition-colors disabled:opacity-50 ${
              isSelected
                ? 'border-primary bg-lavender/40 shadow-sm'
                : 'border-border bg-surface hover:border-border-hover'
            }`}
          >
            <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${m.tile}`}>
              {m.icon}
            </div>
            <div className="flex-1">
              <div className="font-semibold text-navy">{m.label}</div>
              <div className="mt-1 text-xs text-muted">{m.description}</div>
            </div>
          </motion.button>
        );
      })}
    </div>
  );
}

export const QUIZ_MODE_KEYS: readonly RecallQuizMode[] = MODES.map((m) => m.key);
