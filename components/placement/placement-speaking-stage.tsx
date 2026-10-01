import { useCallback, useEffect, useRef, useState } from 'react';
import { CheckCircle2, Loader2, Mic, Play, Square } from 'lucide-react';
import { fetchPlacementSpeakingTasks, resolvePlacementAudioUrl, speakingPromptMaxPlays, speakingTaskExpectsAudio, submitPlacementSpeaking, uploadPlacementRecording, type PlacementSpeakingTask } from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';
import { PRIMARY_BUTTON, SECONDARY_BUTTON } from './placement-shared';
import { TransitionCard } from './placement-preflight';
import { type SpeakPhase, pickRecordingMimeType, HEAR_ONLY_INSTRUCTION, AudioUnavailable, SpeakingPromptAudio } from './placement-speaking-audio';

export function SpeakingStage({ sessionId, onComplete }: { sessionId: string; onComplete: () => void }) {
  const [tasks, setTasks] = useState<PlacementSpeakingTask[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [index, setIndex] = useState(0);
  // Which task's prompt audio has played to the end (keyed by task, so moving
  // to the next task re-locks planning/recording without a manual reset).
  const [heardTaskId, setHeardTaskId] = useState<string | null>(null);
  // Bumped by "Retry" when a task expects audio but the engine sent none.
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  const [phase, setPhase] = useState<SpeakPhase>('ready');
  const [endsAt, setEndsAt] = useState<number | null>(null);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [playbackUrl, setPlaybackUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  // The recording stays on this device until the server confirms receipt; a
  // failed upload never discards it.
  const blobRef = useRef<Blob | null>(null);
  const uploadedPathRef = useRef<string | null>(null);
  const playbackUrlRef = useRef<string | null>(null);
  const elapsedActionRef = useRef<'record' | 'stop' | null>(null);
  const responseSecondsRef = useRef(60);

  useEffect(() => {
    let cancelled = false;
    fetchPlacementSpeakingTasks(sessionId)
      .then((loaded) => {
        if (cancelled) return;
        setTasks(loaded);
        setLoadError(null);
        setRefreshing(false);
      })
      .catch((err) => {
        if (cancelled) return;
        setLoadError(readErrorMessage(err, 'Could not load the speaking tasks.'));
        setRefreshing(false);
      });
    return () => {
      cancelled = true;
    };
  }, [loadAttempt, sessionId]);

  useEffect(
    () => () => {
      if (playbackUrlRef.current) URL.revokeObjectURL(playbackUrlRef.current);
      recorderRef.current?.stream.getTracks().forEach((track) => track.stop());
    },
    [],
  );

  const replacePlayback = useCallback((url: string | null) => {
    if (playbackUrlRef.current) URL.revokeObjectURL(playbackUrlRef.current);
    playbackUrlRef.current = url;
    setPlaybackUrl(url);
  }, []);

  const stopRecording = useCallback(() => {
    elapsedActionRef.current = null;
    setEndsAt(null);
    const recorder = recorderRef.current;
    if (recorder && recorder.state !== 'inactive') recorder.stop();
  }, []);

  const beginRecording = useCallback(
    async (seconds: number) => {
      elapsedActionRef.current = null;
      setError(null);
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const mimeType = pickRecordingMimeType();
        const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
        chunksRef.current = [];
        recorder.ondataavailable = (event) => {
          if (event.data.size > 0) chunksRef.current.push(event.data);
        };
        recorder.onstop = () => {
          stream.getTracks().forEach((track) => track.stop());
          recorderRef.current = null;
          const blob = new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' });
          if (blob.size === 0) {
            setPhase('ready');
            setError('Nothing was recorded — check your microphone and record again.');
            return;
          }
          blobRef.current = blob;
          uploadedPathRef.current = null;
          replacePlayback(URL.createObjectURL(blob));
          setPhase('recorded');
        };
        recorder.start(1000);
        recorderRef.current = recorder;
        setPhase('recording');
        setRemaining(seconds);
        elapsedActionRef.current = 'stop';
        setEndsAt(Date.now() + seconds * 1000);
      } catch {
        setPhase('ready');
        setError('Microphone access is required to record your response. Allow it in your browser settings, then try again.');
      }
    },
    [replacePlayback],
  );

  // Planning and response windows: planning hands over to recording, and the
  // response window stops the recorder when it runs out.
  useEffect(() => {
    if (endsAt === null) return;
    const tick = () => {
      const left = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
      setRemaining(left);
      if (left > 0) return;
      const action = elapsedActionRef.current;
      elapsedActionRef.current = null;
      if (action === 'record') void beginRecording(responseSecondsRef.current);
      else if (action === 'stop') stopRecording();
    };
    const interval = window.setInterval(tick, 250);
    return () => window.clearInterval(interval);
  }, [beginRecording, endsAt, stopRecording]);

  if (!tasks) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted" role="status">
        {loadError ? <span className="text-danger-strong">{loadError}</span> : <><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing speaking tasks…</>}
      </div>
    );
  }

  const task = tasks[index];
  if (!task) {
    return (
      <TransitionCard
        heading="No speaking tasks left"
        text="There are no speaking responses left in this attempt."
        ctaLabel="Continue"
        onContinue={onComplete}
      />
    );
  }
  const prepSeconds = task.prepSeconds ?? 0;
  const responseSeconds = task.speakingSeconds ?? 60;
  const promptPath = task.audio ? resolvePlacementAudioUrl(task.audio.storagePath) : null;
  // A task the candidate must hear but has no playable clip is a fault to
  // surface, never a silent screen (and never a reason to show the script).
  const audioMissing = !promptPath && speakingTaskExpectsAudio(task);
  // Planning/recording stay locked until the prompt audio has played to the end.
  const promptLocked = audioMissing || (promptPath !== null && heardTaskId !== task.taskId);

  const retryAudio = () => {
    setLoadError(null);
    setRefreshing(true);
    setLoadAttempt((count) => count + 1);
  };

  const startTask = () => {
    if (promptLocked) return;
    setError(null);
    responseSecondsRef.current = responseSeconds;
    if (prepSeconds > 0) {
      setPhase('prep');
      setRemaining(prepSeconds);
      elapsedActionRef.current = 'record';
      setEndsAt(Date.now() + prepSeconds * 1000);
    } else {
      void beginRecording(responseSeconds);
    }
  };

  const recordNow = () => {
    setEndsAt(null);
    void beginRecording(responseSeconds);
  };

  const submit = async () => {
    const blob = blobRef.current;
    if (!blob) return;
    setPhase('uploading');
    setError(null);
    try {
      if (!uploadedPathRef.current) {
        const extension = blob.type.includes('mp4') ? 'm4a' : 'webm';
        const uploaded = await uploadPlacementRecording(blob, `speaking-${task.taskId}.${extension}`);
        uploadedPathRef.current = uploaded.storagePath;
      }
      await submitPlacementSpeaking(sessionId, task.taskId, uploadedPathRef.current);
      // Saved server-side — only now is the local copy released.
      blobRef.current = null;
      uploadedPathRef.current = null;
      replacePlayback(null);
      if (index + 1 < tasks.length) {
        setIndex((current) => current + 1);
        setPhase('ready');
        setRemaining(null);
      } else {
        onComplete();
      }
    } catch {
      setPhase('upload-failed');
    }
  };

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted">
        Response {index + 1} of {tasks.length}
      </p>

      <section className="space-y-2 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Speaking task">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Task</p>
        {audioMissing ? null : (
          <p className="whitespace-pre-line break-words text-base leading-relaxed text-navy">
            {task.candidateSeesText === false ? HEAR_ONLY_INSTRUCTION : task.prompt}
          </p>
        )}
        <p className="text-xs text-muted">
          {prepSeconds > 0 ? `Planning time: ${prepSeconds} s · ` : ''}Response time: up to {responseSeconds} s
        </p>
      </section>

      {audioMissing ? (
        <AudioUnavailable
          onRetry={retryAudio}
          busy={refreshing}
          detail={loadError ?? (loadAttempt > 0 && !refreshing ? 'Still unavailable — please wait a moment, then try again.' : null)}
        />
      ) : null}

      {promptPath ? (
        <SpeakingPromptAudio
          key={task.taskId}
          apiPath={promptPath}
          maxPlays={speakingPromptMaxPlays(task.taskType)}
          replayLocked={phase === 'prep' || phase === 'recording' || phase === 'uploading'}
          onHeard={() => setHeardTaskId(task.taskId)}
        />
      ) : null}

      <section className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Recording">
        {phase === 'ready' ? (
          <>
            <button type="button" onClick={startTask} disabled={promptLocked} className={PRIMARY_BUTTON}>
              <Play className="h-4 w-4" aria-hidden /> {prepSeconds > 0 ? 'Start planning time' : 'Start recording'}
            </button>
            {promptLocked && !audioMissing ? (
              <p className="text-xs text-muted">Play the audio to the end first — this unlocks your response.</p>
            ) : null}
          </>
        ) : null}

        {phase === 'prep' ? (
          <div className="space-y-3">
            <p className="text-sm text-navy" role="status">
              Planning time — recording starts automatically in{' '}
              <span className="font-mono tabular-nums">{remaining ?? prepSeconds}</span> s.
            </p>
            <button type="button" onClick={recordNow} className={SECONDARY_BUTTON}>
              <Mic className="h-4 w-4" aria-hidden /> Start recording now
            </button>
          </div>
        ) : null}

        {phase === 'recording' ? (
          <div className="space-y-3">
            <p className="flex items-center gap-2 text-sm font-semibold text-danger-strong" role="status">
              <span className="inline-block h-2.5 w-2.5 animate-pulse rounded-full bg-danger" aria-hidden />
              Recording — <span className="font-mono tabular-nums">{remaining ?? responseSeconds}</span> s left
            </p>
            <button
              type="button"
              onClick={stopRecording}
              className="inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-xl bg-danger px-5 py-2.5 text-sm font-semibold text-white hover:opacity-90 sm:w-auto"
            >
              <Square className="h-4 w-4" aria-hidden /> Stop recording
            </button>
          </div>
        ) : null}

        {phase === 'recorded' || phase === 'uploading' || phase === 'upload-failed' ? (
          <div className="space-y-3">
            {playbackUrl ? (
              // eslint-disable-next-line jsx-a11y/media-has-caption -- candidate playback of own recording
              <audio src={playbackUrl} controls className="w-full" />
            ) : null}
            {phase === 'upload-failed' ? (
              <p role="alert" className="rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-navy">
                We could not upload your recording. Your recording is still saved on this device. Retry upload.
              </p>
            ) : null}
            <div className="flex flex-col gap-3 sm:flex-row">
              <button type="button" onClick={submit} disabled={phase === 'uploading'} className={PRIMARY_BUTTON}>
                {phase === 'uploading' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
                {phase === 'upload-failed' ? 'Retry upload' : phase === 'uploading' ? 'Saving your response…' : 'Submit response'}
              </button>
              <button type="button" onClick={recordNow} disabled={phase === 'uploading'} className={SECONDARY_BUTTON}>
                <Mic className="h-4 w-4" aria-hidden /> Record again
              </button>
            </div>
          </div>
        ) : null}

        {error ? (
          <p role="alert" className="text-sm text-danger-strong">
            {error}
          </p>
        ) : null}
      </section>
    </div>
  );
}
