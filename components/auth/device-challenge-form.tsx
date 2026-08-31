'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowRight, Clock, Laptop, ShieldCheck } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { claimDeviceVerificationOtpSend, formatDeviceCountdown, getPendingDeviceChallenge, selectReplacementDevice, sendDeviceVerificationOtp } from '@/lib/auth-client';
import { obtainFirebaseOtpRecaptchaToken } from '@/lib/auth/firebase-otp-recaptcha';
import { describeOtpDelivery } from '@/lib/auth/otp-delivery';
import { appendAuthNextParam, AUTH_ROUTES } from '@/lib/auth/routes';
import { resolvePostAuthDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from './auth-screen-shell';
import { OtpCodeInput } from './otp-code-input';
import styles from './auth-screen-shell.module.scss';
import { readErrorMessage } from '@/lib/read-error-message';

interface DeviceChallengeFormProps {
  nextHref?: string | null;
}

function formatTrustTime(value: string | null | undefined): string {
  if (!value) return 'unknown';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return value;
  }
}

export function DeviceChallengeForm({ nextHref }: DeviceChallengeFormProps) {
  const router = useRouter();
  const { pendingDeviceChallenge, completeDeviceVerification, cancelDeviceVerification } = useAuth();
  const [code, setCode] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSending, setIsSending] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSelecting, setIsSelecting] = useState(false);
  const [deliveryChannel, setDeliveryChannel] = useState('email');
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(pendingDeviceChallenge?.selectedDeviceId ?? null);
  const [countdown, setCountdown] = useState<string | null>(pendingDeviceChallenge?.countdown ?? null);
  const [secondsRemaining, setSecondsRemaining] = useState<number | null>(pendingDeviceChallenge?.secondsRemaining ?? null);
  // Mirrors the live (possibly selection-bound) challenge token from storage.
  // `pendingDeviceChallenge.challengeToken` from context is a snapshot taken
  // when the challenge started and never updates when selectReplacementDevice
  // exchanges it for a selection-bound token, so it can't drive the auto-send
  // effect below on its own.
  const [activeChallengeToken, setActiveChallengeToken] = useState<string | null>(pendingDeviceChallenge?.challengeToken ?? null);
  const sentForToken = useRef<string | null>(null);

  const isReplacementRequired = pendingDeviceChallenge?.mode === 'replacement_required';
  const isCooldown = pendingDeviceChallenge?.mode === 'cooldown' || (pendingDeviceChallenge?.secondsRemaining != null && pendingDeviceChallenge?.cooldownUntil);
  const registeredDevices = pendingDeviceChallenge?.registeredDevices ?? [];
  const activeCount = pendingDeviceChallenge?.activeDeviceCount ?? registeredDevices.length;
  const maxCount = pendingDeviceChallenge?.maxDevices ?? 2;

  // Live countdown for cooldown
  useEffect(() => {
    if (!pendingDeviceChallenge?.cooldownUntil) {
      setCountdown(pendingDeviceChallenge?.countdown ?? null);
      setSecondsRemaining(pendingDeviceChallenge?.secondsRemaining ?? null);
      return;
    }
    const compute = () => {
      const until = new Date(pendingDeviceChallenge.cooldownUntil as string).getTime();
      const sec = Math.max(0, Math.floor((until - Date.now()) / 1000));
      setSecondsRemaining(sec);
      setCountdown(formatDeviceCountdown(sec) ?? pendingDeviceChallenge.countdown ?? null);
    };
    compute();
    const id = window.setInterval(compute, 1000);
    return () => window.clearInterval(id);
  }, [pendingDeviceChallenge?.cooldownUntil, pendingDeviceChallenge?.countdown, pendingDeviceChallenge?.secondsRemaining]);

  useEffect(() => {
    setSelectedDeviceId(pendingDeviceChallenge?.selectedDeviceId ?? null);
  }, [pendingDeviceChallenge?.selectedDeviceId]);

  // Re-sync when context hands us a genuinely new challenge (e.g. a fresh
  // sign-in attempt). Selection binding updates `activeChallengeToken`
  // itself (see handleSelectDevice), so this intentionally does not run on
  // every render.
  useEffect(() => {
    setActiveChallengeToken(pendingDeviceChallenge?.challengeToken ?? null);
  }, [pendingDeviceChallenge?.challengeToken]);

  const applyChallengeNotice = (destinationHint?: string, channel?: string) => {
    const nextChannel = channel || 'email';
    setDeliveryChannel(nextChannel);
    setNotice(`Enter the 6-digit code sent by ${describeOtpDelivery(nextChannel, destinationHint, pendingDeviceChallenge?.email)}.`);
  };

  const sendOtp = async () => {
    const recaptchaToken = await obtainFirebaseOtpRecaptchaToken();
    return sendDeviceVerificationOtp({ recaptchaToken });
  };

  const handleSelectDevice = async (deviceId: string) => {
    if (selectedDeviceId === deviceId) return;
    setSelectedDeviceId(deviceId);
    setError(null);
    setIsSelecting(true);
    try {
      const updated = await selectReplacementDevice(deviceId);
      setNotice(null);
      // Binding a selection exchanges the challenge for a fresh,
      // selection-bound token. Surface it so the auto-send effect below
      // (keyed on activeChallengeToken/selectedDeviceId) sends the OTP —
      // sending it again here too used to fire two OTPs for one selection.
      setActiveChallengeToken(updated.challengeToken);
    } catch (selectError) {
      setError(readErrorMessage(selectError, 'Unable to select that device.'));
    } finally {
      setIsSelecting(false);
    }
  };

  useEffect(() => {
    if (!activeChallengeToken) {
      return;
    }
    // In-flight/same-mount lock: avoids a duplicate request if this effect
    // re-runs (e.g. React StrictMode double-invoke) before the send below
    // has resolved and persisted the durable flag checked next.
    if (sentForToken.current === activeChallengeToken) {
      return;
    }
    // Durable guard, re-read from storage (not the `pendingDeviceChallenge`
    // prop, which never updates after a selection binds a new token): if a
    // code was already requested for this exact token — including in a
    // previous mount, e.g. the WebView reloaded while the learner briefly
    // left the app to check their email — do not silently request another
    // one and invalidate the code already in their inbox.
    const stored = getPendingDeviceChallenge();
    if (!stored || stored.challengeToken !== activeChallengeToken) {
      return;
    }
    if (stored.otpRequestedForToken === activeChallengeToken) {
      return;
    }
    // For replacement_required without a selection, do not auto-send OTP — user must pick a slot first
    if (isReplacementRequired && !stored.selectedDeviceId) {
      return;
    }

    sentForToken.current = activeChallengeToken;
    let cancelled = false;
    setIsSending(true);

    (async () => {
      try {
        // Claim before the side-effecting HTTP request. This closes the gap
        // where two mounts (or an Android WebView recreation) could both see
        // an unmarked challenge while the first request was still in flight.
        if (!await claimDeviceVerificationOtpSend(activeChallengeToken)) {
          return;
        }
        const challenge = await sendOtp();
        if (!cancelled) {
          applyChallengeNotice(challenge.destinationHint, challenge.deliveryChannel);
        }
      } catch (sendError) {
        if (!cancelled) {
          sentForToken.current = null; // allow a retry on transient failure
          const msg = readErrorMessage(sendError, 'Unable to send the device verification code.');
          // Show replacement selection required as a distinct message
          if (msg.toLowerCase().includes('select which device')) {
            setError('Select which device to replace before we send a code.');
          } else {
            setError(msg);
          }
        }
      } finally {
        if (!cancelled) {
          setIsSending(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeChallengeToken, isReplacementRequired, selectedDeviceId]);

  const handleResend = async () => {
    if (isReplacementRequired && !selectedDeviceId) {
      setError('Select which device to replace before sending a code.');
      return;
    }
    setCode('');
    setError(null);
    setIsSending(true);

    try {
      const challenge = await sendOtp();
      applyChallengeNotice(challenge.destinationHint, challenge.deliveryChannel);
    } catch (sendError) {
      setError(readErrorMessage(sendError, 'Unable to send the device verification code.'));
    } finally {
      setIsSending(false);
    }
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (isReplacementRequired && !selectedDeviceId) {
      setError('Select which device to replace before verifying the code.');
      return;
    }
    const normalizedCode = code.replace(/\D/g, '');

    if (normalizedCode.length !== 6) {
      setError(deliveryChannel === 'sms'
        ? 'Enter the 6-digit code sent by SMS.'
        : 'Enter the 6-digit code sent to your email.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const session = await completeDeviceVerification(normalizedCode);
      router.replace(resolvePostAuthDestination(session.currentUser, nextHref));
    } catch (submitError) {
      const msg = readErrorMessage(submitError, 'Unable to verify this device.');
      if (msg.toLowerCase().includes('select which device')) {
        setError('Select which device to replace before verifying the code.');
      } else {
        setError(msg);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  const signInHref = appendAuthNextParam(AUTH_ROUTES.signIn, nextHref);

  const showReplacementChoice = isReplacementRequired && registeredDevices.length > 0;
  const showCooldown = isCooldown && pendingDeviceChallenge?.cooldownUntil;

  return (
    <AuthScreenShell
      eyebrow="Device Verification"
      title={isReplacementRequired ? 'Choose a device to replace' : 'Verify this device'}
      subtitle={pendingDeviceChallenge
        ? isReplacementRequired
          ? `You've reached the limit of ${maxCount} approved devices (${activeCount}/${maxCount}). Select one to replace for ${pendingDeviceChallenge.email}.`
          : `We don't recognize this device for ${pendingDeviceChallenge.email}. Confirm it's you before continuing.`
        : 'A pending device verification is required before you can continue.'}
      footer={
        <p className={styles.resend}>
          Did not receive a code?{' '}
          <button
            type="button"
            className={styles.link}
            onClick={() => void handleResend()}
            disabled={!pendingDeviceChallenge || isSending || isSelecting || (isReplacementRequired && !selectedDeviceId)}
          >
            Resend it
          </button>
        </p>
      }
    >
      <form className={styles.passwordFlowForm} onSubmit={handleSubmit}>
        <div className={styles.summaryCard}>
          <h4>{isReplacementRequired ? `Approved devices ${activeCount}/${maxCount}` : 'New device detected'}</h4>
          <div className={styles.summaryList}>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <Laptop size={16} />
              </span>
              <p>{isReplacementRequired ? 'Pick the device you want to sign out. The new device will take its place.' : 'Approving this device signs your previous device out once verification succeeds.'}</p>
            </div>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <ShieldCheck size={16} />
              </span>
              <p>
                {deliveryChannel === 'sms'
                  ? 'Enter the SMS code to trust this device and finish signing in.'
                  : 'Enter the code we emailed you to trust this device and finish signing in.'}
              </p>
            </div>
          </div>
          {!isReplacementRequired && <p className={styles.fieldHint} style={{ marginTop: '0.5rem' }}>Identity key: <code className="font-mono text-xs">X-OET-Device-Id</code> (browser profile / app installation).</p>}
        </div>

        {showCooldown ? (
          <div className={`${styles.notice} ${styles.noticeWarning}`.trim()} role="status" aria-live="polite">
            <div className="flex items-start gap-2">
              <Clock size={16} className="mt-0.5 shrink-0" />
              <div>
                <p className="font-medium">Too many device changes recently.</p>
                <p className="text-sm">You can still recover with email OTP. Cooldown until <span className="font-mono text-xs">{pendingDeviceChallenge.cooldownUntil ? new Date(pendingDeviceChallenge.cooldownUntil).toLocaleString() : 'unknown'}</span>{countdown ? ` — ${countdown} remaining` : secondsRemaining != null ? ` — ${secondsRemaining}s remaining` : ''}.</p>
                <p className="mt-1 text-xs">Window: {pendingDeviceChallenge.changeWindowDays ?? 7} days · Limit: {pendingDeviceChallenge.changeMaxPerWindow ?? 3} · Not counted: same browser/app after IP/location change or storage recovery via continuity cookie.</p>
                {countdown ? <p className="mt-1 font-mono text-xs">Live countdown: {countdown}</p> : null}
              </div>
            </div>
          </div>
        ) : null}

        {showReplacementChoice ? (
          <fieldset className={styles.field} aria-required="true">
            <legend className="mb-2 text-sm font-semibold">Select a device to replace</legend>
            <div className="space-y-2">
              {registeredDevices.map((device) => {
                const isSelected = selectedDeviceId === device.id;
                return (
                  <label
                    key={device.id}
                    className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm ${isSelected ? 'border-navy bg-blue-50' : 'border-border bg-background-light'} ${isSelecting ? 'opacity-60' : ''}`}
                  >
                    <input
                      type="radio"
                      name="replacementDevice"
                      value={device.id}
                      checked={isSelected}
                      onChange={() => void handleSelectDevice(device.id)}
                      disabled={isSubmitting || isSelecting}
                      className="mt-1"
                      aria-label={`Replace ${device.maskedDeviceId}`}
                    />
                    <span className="min-w-0 flex-1">
                      <span className="font-mono text-xs font-medium">{device.maskedDeviceId}</span>
                      {device.deviceName ? <span className="ml-2 text-xs text-muted">{device.deviceName}</span> : null}
                      {device.platform ? <span className="ml-2 rounded bg-muted px-1.5 py-0.5 text-[11px]">{device.platform}</span> : null}
                      <span className="block text-xs text-muted">Trusted: {formatTrustTime(device.trustedAt)}{device.lastSeenAt ? ` · Last seen: ${formatTrustTime(device.lastSeenAt)}` : ''}</span>
                    </span>
                  </label>
                );
              })}
            </div>
            <p className={styles.fieldHint}>You have {activeCount} approved {activeCount === 1 ? 'device' : 'devices'} (limit {maxCount}). This choice is required and binds to your verification code.</p>
          </fieldset>
        ) : !isReplacementRequired && typeof activeCount === 'number' ? (
          <div className={styles.field}>
            <p className={styles.fieldHint}>Approved devices: {activeCount}/{maxCount}. No replacement choice needed for this free slot — we’ll send a code.</p>
          </div>
        ) : null}

        <div className={styles.field}>
          <label htmlFor="device-otp-code">Verification code</label>
          <OtpCodeInput value={code} onChange={(next) => {
            setCode(next.replace(/\D/g, '').slice(0, 6));
            setError(null);
          }} disabled={!pendingDeviceChallenge || isSubmitting || (isReplacementRequired && !selectedDeviceId)} />
          <p className={styles.fieldHint}>
            {deliveryChannel === 'sms'
              ? 'Use the 6-digit code from the SMS we sent you.'
              : 'Use the 6-digit code from your email.'}
          </p>
        </div>

        {notice ? <p className={styles.fieldHint}>{notice}</p> : null}
        {error ? <div className={`${styles.notice} ${styles.noticeDanger}`.trim()} role="alert">{error}</div> : null}
        {isReplacementRequired && !selectedDeviceId ? <p className="text-sm text-amber-700">Select a device above to enable verification. The code is bound to your selection.</p> : null}

        <button
          type="submit"
          className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
          disabled={!pendingDeviceChallenge || isSubmitting || isSelecting || (isReplacementRequired && !selectedDeviceId)}
        >
          <span>{isSubmitting ? 'Verifying device...' : isSelecting ? 'Binding selection...' : 'Verify Device'}</span>
          {!isSubmitting && !isSelecting ? <ArrowRight size={18} /> : null}
        </button>

        <p className={styles.fieldHint} style={{ textAlign: 'center', marginTop: '0.75rem' }}>
          Entered the wrong email or want to use another account?{' '}
          <Link
            href={signInHref}
            className={styles.link}
            onClick={cancelDeviceVerification}
          >
            Back to sign in
          </Link>
        </p>
      </form>
    </AuthScreenShell>
  );
}
