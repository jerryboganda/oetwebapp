import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConversationChatView, type ChatTurn } from './ConversationChatView';
import { ConversationMicControl } from './ConversationMicControl';
import { ConversationPrepCard } from './ConversationPrepCard';
import { ConversationTimerBar } from './ConversationTimerBar';
import type { ConversationScenario } from '@/lib/types/conversation';

const scenario: ConversationScenario = {
  title: 'Chest pain roleplay',
  context: 'A patient is worried about chest pain.',
  patientRole: 'Patient',
  clinicianRole: 'Nurse',
  objectives: ['Reassure the patient'],
};

describe('Conversation realtime controls', () => {
  it('requires both audio consent acknowledgements before starting', async () => {
    const user = userEvent.setup();
    const onConsentChange = vi.fn();
    const onStart = vi.fn();

    render(
      <ConversationPrepCard
        scenario={scenario}
        prepCountdown={90}
        recordingConsentAccepted={false}
        vendorConsentAccepted={false}
        consentVersion="consent-v2"
        audioRetentionDays={14}
        startDisabled
        onConsentChange={onConsentChange}
        onStart={onStart}
      />,
    );

    expect(screen.getByText(/14-day retention/i)).toBeInTheDocument();
    expect(screen.getByText(/consent-v2/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /start now/i })).toBeDisabled();

    await user.click(screen.getByLabelText(/microphone capture/i));
    await user.click(screen.getByLabelText(/speech processing/i));

    expect(onConsentChange).toHaveBeenNthCalledWith(1, 'recording', true);
    expect(onConsentChange).toHaveBeenNthCalledWith(2, 'vendor', true);
    expect(onStart).not.toHaveBeenCalled();
  });

  it('shows a learner-friendly fallback reason for normal recording mode', () => {
    render(
      <ConversationTimerBar
        elapsed={20}
        timeLimit={300}
        turns={2}
        sttMode="batch-fallback"
        connectionState="fallback"
        fallbackReason="REALTIME_STT_DISABLED"
      />,
    );

    expect(screen.getByText('Normal recording')).toBeInTheDocument();
    expect(screen.getByText(/using normal recording/i)).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Normal recording');
    expect(screen.getByRole('progressbar', { name: /conversation time elapsed/i })).toHaveAttribute('aria-valuenow', '20');
  });

  it('locks the microphone while a turn is submitting', () => {
    render(
      <ConversationMicControl
        recording={false}
        disabled
        ending={false}
        canEnd
        turnState="sending"
        disabledReason="Submitting and transcribing your last answer."
        onRecord={vi.fn()}
        onEnd={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: /submitting answer/i })).toBeDisabled();
    expect(screen.getByText(/submitting and transcribing/i)).toBeInTheDocument();
  });

  it('renders a provisional transcript separately from committed turns', () => {
    const turns: ChatTurn[] = [{
      turnNumber: 1,
      role: 'ai',
      content: 'Tell me what brought you in today.',
      timestamp: 0,
      audioUrl: '/v1/conversations/media/ai.mp3',
    }];

    render(
      <ConversationChatView
        turns={turns}
        aiThinking={false}
        partialTranscript={{ turnClientId: 'rt-1', text: 'I have chest pain', receivedAt: Date.now() }}
        turnState="listening"
        onReplay={vi.fn()}
      />,
    );

    expect(screen.getByText('Tell me what brought you in today.')).toBeInTheDocument();
    expect(screen.getByText('I have chest pain')).toBeInTheDocument();
    expect(screen.getByText('Live transcript: I have chest pain')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /replay ai partner turn 1/i })).toBeInTheDocument();
  });
});
