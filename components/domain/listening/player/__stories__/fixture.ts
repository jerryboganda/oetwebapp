// Storybook fixture for ListeningSessionDto — used by Listening player
// component stories. Not exported from production code.

import type { ListeningSessionDto } from '@/lib/listening-api';

export const listeningSessionFixture: ListeningSessionDto = {
  paper: {
    id: 'lt-001',
    sourceKind: 'authored',
    title: 'Storybook sample paper — community consultation',
    slug: 'storybook-sample-paper',
    difficulty: 'medium',
    estimatedDurationMinutes: 40,
    scenarioType: 'consultation',
    audioUrl: '/audio/sample.mp3',
    questionPaperUrl: null,
    audioAvailable: true,
    audioUnavailableReason: null,
    assetReadiness: {
      audio: true,
      questionPaper: true,
      answerKey: true,
      audioScript: true,
    },
    transcriptPolicy: 'after_submit',
    extracts: [
      {
        partCode: 'A1',
        displayOrder: 1,
        kind: 'consultation',
        title: 'Asthma review',
        accentCode: 'AU',
        speakers: [
          { id: 's1', role: 'GP', gender: 'f', accent: 'AU' },
          { id: 's2', role: 'Patient', gender: 'm', accent: 'AU' },
        ],
        audioStartMs: 0,
        audioEndMs: 240_000,
      },
      {
        partCode: 'A2',
        displayOrder: 2,
        kind: 'consultation',
        title: 'Diabetes follow-up',
        accentCode: 'UK',
        speakers: [
          { id: 's3', role: 'GP', gender: 'm', accent: 'UK' },
          { id: 's4', role: 'Patient', gender: 'f', accent: 'UK' },
        ],
        audioStartMs: 240_000,
        audioEndMs: 480_000,
      },
    ],
  },
  attempt: null,
  questions: [],
  modePolicy: {
    mode: 'exam',
    canPause: false,
    canScrub: false,
    onePlayOnly: true,
    autosave: true,
    transcriptPolicy: 'after_submit',
    presentationStyle: 'exam_standard',
    integrityLockRequired: true,
    printableBooklet: false,
  },
  scoring: {
    maxRawScore: 42,
    passRawScore: 30,
    passScaledScore: 350,
  },
  readiness: {
    objectiveReady: true,
    questionCount: 42,
    audioAvailable: true,
    missingReason: null,
  },
};
