import Link from 'next/link';
import { useState, type ReactNode } from 'react';
import { CheckCircle2, Headphones, Loader2, Mic, Play, Volume2 } from 'lucide-react';
import type { PlacementHistoryItem } from '@/lib/api/placement';
import { PARTS, PRIMARY_BUTTON, SECONDARY_BUTTON } from './placement-shared';

// ── Chrome ───────────────────────────────────────────────────────────

export function ProgressHeader({ part, completed, fraction }: { part: number; completed: number; fraction: number }) {
  const info = PARTS[part];
  const percent = Math.round(((Math.min(completed, PARTS.length) + fraction) / PARTS.length) * 100);
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <p className="text-sm font-semibold text-navy">
          Part {part + 1} of {PARTS.length} — {info.name}
        </p>
        <p className="text-xs text-muted">
          {completed} of {PARTS.length} parts complete
        </p>
      </div>
      <div
        className="h-1.5 w-full overflow-hidden rounded-full bg-border"
        role="progressbar"
        aria-label="Placement test progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
      >
        <div className="h-full rounded-full bg-primary transition-[width] duration-500" style={{ width: `${percent}%` }} />
      </div>
      <ol className="hidden flex-wrap gap-x-4 gap-y-1 text-xs text-muted sm:flex">
        {PARTS.map((item, index) => (
          <li key={item.key} className={`flex items-center gap-1 ${index === part ? 'font-semibold text-navy' : ''}`}>
            {index < completed ? (
              <CheckCircle2 className="h-3.5 w-3.5 text-success" aria-hidden />
            ) : (
              <span className="inline-block h-3.5 w-3.5 rounded-full border border-border" aria-hidden />
            )}
            {item.name}
            {index < completed ? <span className="sr-only"> (complete)</span> : null}
          </li>
        ))}
      </ol>
    </div>
  );
}

export function TransitionCard({
  heading,
  text,
  ctaLabel,
  onContinue,
  completed = false,
  busy = false,
  error = null,
  children,
}: {
  heading: string;
  text: string;
  ctaLabel: string;
  onContinue: () => void;
  completed?: boolean;
  busy?: boolean;
  error?: string | null;
  children?: ReactNode;
}) {
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        {completed ? <CheckCircle2 className="h-8 w-8 text-success" aria-hidden /> : null}
        <h2 className="text-xl font-semibold text-navy">{heading}</h2>
        <p className="text-sm leading-relaxed text-muted">{text}</p>
      </div>
      {children}
      {error ? (
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
      ) : null}
      <button type="button" onClick={onContinue} disabled={busy} className={PRIMARY_BUTTON}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null}
        {ctaLabel}
      </button>
    </section>
  );
}

// ── Overview ─────────────────────────────────────────────────────────

export function OverviewCard({
  errorMessage,
  history,
  busy,
  extraTimePercent,
  onStart,
}: {
  errorMessage: string | null;
  history: PlacementHistoryItem[];
  busy: boolean;
  extraTimePercent: number | null;
  onStart: () => void;
}) {
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        <h2 className="text-xl font-semibold text-navy">Free General English Placement Test</h2>
        <p className="text-sm leading-relaxed text-muted">
          An indicative CEFR placement estimate from Pre-A1 to C2 across Reading, Listening, Speaking and Writing,
          with a grammar and vocabulary diagnostic. Suitable for OET, IELTS, PTE, TOEFL and General English learners.
        </p>
      </div>

      <ol className="divide-y divide-border overflow-hidden rounded-xl border border-border">
        {PARTS.map((part, index) => (
          <li key={part.key} className="flex flex-col gap-1 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-sm font-semibold text-navy">
                Part {index + 1} of {PARTS.length} — {part.name}
              </p>
              <p className="text-xs text-muted">{part.measures}</p>
            </div>
            <p className="text-xs text-muted sm:text-right">
              {part.count} · {part.time}
            </p>
          </li>
        ))}
      </ol>

      <p className="text-sm leading-relaxed text-navy">
        Approximate full-profile time: 60–85 minutes. The test is adaptive, so the exact number of objective questions can
        vary. You may take a short break between parts, but not during an active timed question, recording or audio item.
      </p>

      {extraTimePercent != null && extraTimePercent > 0 ? (
        <p className="rounded-xl border border-border bg-background-light px-4 py-3 text-sm text-navy">
          Extra time has been approved for your account (+{extraTimePercent}% on timed sections).
        </p>
      ) : null}

      <ul className="grid gap-2 text-sm text-muted sm:grid-cols-2">
        <li className="flex items-start gap-2">
          <Headphones className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> Headphones or speakers for Listening
        </li>
        <li className="flex items-start gap-2">
          <Mic className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> A working microphone for Speaking
        </li>
        <li className="flex items-start gap-2">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> Listening and Reading are scored
          instantly
        </li>
        <li className="flex items-start gap-2">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> Writing and Speaking are reviewed by
          Dr Hesham&apos;s team
        </li>
      </ul>

      <p className="text-xs text-muted">
        Your answers save as you go, so you can continue after an interruption. Results are saved to your profile.
      </p>

      {errorMessage ? (
        <p role="alert" className="text-sm text-danger">
          {errorMessage}
        </p>
      ) : null}

      <button type="button" onClick={onStart} disabled={busy} className={PRIMARY_BUTTON}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Play className="h-4 w-4" aria-hidden />}
        Start Free Placement Test
      </button>

      {history.length > 0 ? (
        <p className="text-xs text-muted">
          You have{' '}
          <Link href="/placement-test/history" className="text-primary underline">
            {history.length} previous result{history.length === 1 ? '' : 's'}
          </Link>{' '}
          saved on your profile ({history[0].status === 'completed' ? 'latest complete' : 'latest partial'}). Starting a
          new test creates a fresh attempt.
        </p>
      ) : null}
    </section>
  );
}

// ── Audio / microphone checks ────────────────────────────────────────

// One element reused for every Listening unit. iOS Safari unlocks autoplay
// per media element, so priming this element inside the Start Listening tap
// lets every later unit auto-play without another gesture.
export let sharedListeningAudio: HTMLAudioElement | null = null;
export const SILENT_WAV = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=';

export function getListeningAudio(): HTMLAudioElement {
  sharedListeningAudio ??= new Audio();
  return sharedListeningAudio;
}

export function primePlacementAudio() {
  if (typeof Audio === 'undefined') return;
  const element = getListeningAudio();
  element.src = SILENT_WAV;
  element.play().catch(() => undefined);
}

/** Speaks a short sound-check sentence (Web Speech), falling back to a tone. */
export function playSoundCheck(): boolean {
  if (typeof window === 'undefined') return false;
  if ('speechSynthesis' in window) {
    const utterance = new SpeechSynthesisUtterance(
      'This is your sound check. If you can hear this clearly, you are ready to begin the listening part.',
    );
    utterance.lang = 'en-GB';
    window.speechSynthesis.cancel();
    window.speechSynthesis.speak(utterance);
    return true;
  }
  // `window` is narrowed to `never` after the `'speechSynthesis' in window` guard.
  const audioWindow = window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext };
  const Ctx = audioWindow.AudioContext ?? audioWindow.webkitAudioContext;
  if (!Ctx) return false;
  const context = new Ctx();
  const oscillator = context.createOscillator();
  const gain = context.createGain();
  oscillator.frequency.value = 660;
  gain.gain.value = 0.15;
  oscillator.connect(gain).connect(context.destination);
  oscillator.start();
  oscillator.stop(context.currentTime + 1);
  oscillator.onended = () => void context.close();
  return true;
}

export function AudioCheckCard({ onStart }: { onStart: () => void }) {
  const [played, setPlayed] = useState<boolean | null>(null);
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        <h2 className="text-xl font-semibold text-navy">Part 3 of 5 — Listening</h2>
        <p className="text-sm leading-relaxed text-muted">
          The audio will start automatically after you begin. Questions are visible while you listen.
        </p>
      </div>
      <div className="space-y-3 rounded-xl border border-border bg-background-light p-4">
        <p className="flex items-center gap-2 text-sm font-semibold text-navy">
          <Headphones className="h-4 w-4 text-primary" aria-hidden /> Audio check
        </p>
        <p className="text-sm text-muted">
          Put on headphones if you have them, then play the sample and adjust your volume until it is comfortable.
        </p>
        <button type="button" onClick={() => setPlayed(playSoundCheck())} className={SECONDARY_BUTTON}>
          <Volume2 className="h-4 w-4" aria-hidden /> Play sample
        </button>
        {played === false ? (
          <p className="text-sm text-warning">This browser could not play the sample. You can still continue.</p>
        ) : null}
        <p className="text-xs text-muted">
          Each recording can be played twice. A replay never lowers your result.
        </p>
      </div>
      <button type="button" onClick={onStart} className={PRIMARY_BUTTON}>
        <Play className="h-4 w-4" aria-hidden /> Start Listening
      </button>
    </section>
  );
}

export function MicCheckCard({ onStart }: { onStart: () => void }) {
  const [state, setState] = useState<'idle' | 'checking' | 'ok' | 'silent' | 'denied'>('idle');
  const [level, setLevel] = useState(0);

  const check = async () => {
    setState('checking');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const context = new AudioContext();
      const analyser = context.createAnalyser();
      analyser.fftSize = 512;
      context.createMediaStreamSource(stream).connect(analyser);
      const samples = new Uint8Array(analyser.fftSize);
      const startedAt = performance.now();
      let peak = 0;
      const tick = () => {
        analyser.getByteTimeDomainData(samples);
        let max = 0;
        for (const value of samples) max = Math.max(max, Math.abs(value - 128));
        const current = max / 128;
        peak = Math.max(peak, current);
        setLevel(current);
        if (performance.now() - startedAt < 4000) {
          requestAnimationFrame(tick);
          return;
        }
        stream.getTracks().forEach((track) => track.stop());
        void context.close();
        setLevel(0);
        setState(peak > 0.05 ? 'ok' : 'silent');
      };
      requestAnimationFrame(tick);
    } catch {
      setState('denied');
    }
  };

  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        <h2 className="text-xl font-semibold text-navy">Part 4 of 5 — Speaking</h2>
        <p className="text-sm leading-relaxed text-muted">
          8 responses. You will see planning/response time for each task.
        </p>
      </div>
      <div className="space-y-3 rounded-xl border border-border bg-background-light p-4">
        <p className="flex items-center gap-2 text-sm font-semibold text-navy">
          <Mic className="h-4 w-4 text-primary" aria-hidden /> Microphone check
        </p>
        <p className="text-sm text-muted">Tap the button, allow microphone access, then say a sentence out loud.</p>
        <button type="button" onClick={check} disabled={state === 'checking'} className={SECONDARY_BUTTON}>
          {state === 'checking' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Mic className="h-4 w-4" aria-hidden />}
          {state === 'checking' ? 'Listening…' : 'Check microphone'}
        </button>
        {state === 'checking' ? (
          <div className="h-2 w-full overflow-hidden rounded-full bg-border" aria-hidden>
            <div className="h-full rounded-full bg-success transition-[width]" style={{ width: `${Math.min(100, level * 250)}%` }} />
          </div>
        ) : null}
        <p className="text-sm" role="status">
          {state === 'ok' ? <span className="text-success">Your microphone is working.</span> : null}
          {state === 'silent' ? (
            <span className="text-warning">
              We could not hear you. Check your microphone and try again — you can still continue.
            </span>
          ) : null}
          {state === 'denied' ? (
            <span className="text-danger">
              Microphone access was blocked. Allow it in your browser settings to record your responses.
            </span>
          ) : null}
        </p>
      </div>
      <button type="button" onClick={onStart} disabled={state === 'checking'} className={PRIMARY_BUTTON}>
        <Play className="h-4 w-4" aria-hidden /> Start Speaking
      </button>
    </section>
  );
}
