'use client';

import Image from 'next/image';
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { IconArrowRight, IconDeviceMobile, IconKey, IconShieldLock } from '@tabler/icons-react';
import { useAuth } from '@/contexts/auth-context';
import type { AuthenticatorSetup } from '@/lib/types/auth';
import { resolvePostAuthDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from './auth-screen-shell';
import { OtpCodeInput } from './otp-code-input';
import styles from './auth-screen-shell.module.scss';

interface MfaSetupCardProps {
  nextHref?: string | null;
}

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to complete MFA setup.';
}

export function MfaSetupCard({ nextHref }: MfaSetupCardProps) {
  const router = useRouter();
  const { user, beginAuthenticatorSetup, confirmAuthenticatorSetup } = useAuth();
  const [setup, setSetup] = useState<AuthenticatorSetup | null>(null);
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isConfirming, setIsConfirming] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const loadSetup = async () => {
      try {
        const response = await beginAuthenticatorSetup();
        if (cancelled) {
          return;
        }

        setSetup(response);
      } catch (setupError) {
        if (!cancelled) {
          setError(readErrorMessage(setupError));
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    void loadSetup();
    return () => {
      cancelled = true;
    };
  }, [beginAuthenticatorSetup]);

  const handleConfirm = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedCode = code.replace(/\D/g, '');

    if (normalizedCode.length !== 6) {
      setError('Enter the 6-digit authenticator code from your app.');
      return;
    }

    setIsConfirming(true);
    setError(null);

    try {
      const currentUser = await confirmAuthenticatorSetup(normalizedCode);
      router.replace(resolvePostAuthDestination(currentUser, nextHref));
    } catch (confirmError) {
      setError(readErrorMessage(confirmError));
    } finally {
      setIsConfirming(false);
    }
  };

  return (
    <AuthScreenShell
      eyebrow="Authenticator Setup"
      title="Set up authenticator MFA"
      subtitle={user?.email
        ? `Finish the authenticator setup for ${user.email}.`
        : 'Finish the authenticator setup for this account.'}
    >
      <div className={styles.wizard}>
        <div className={styles.summaryCard}>
          <h4>What you&apos;ll need</h4>
          <div className={styles.summaryList}>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <IconDeviceMobile size={16} />
              </span>
              <p>Use Google Authenticator, Microsoft Authenticator, 1Password, or any TOTP-compatible app.</p>
            </div>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <IconShieldLock size={16} />
              </span>
              <p>After pairing the app, enter the current 6-digit code to finish setup and unlock privileged access.</p>
            </div>
          </div>
        </div>

        {isLoading ? (
          <div className={`${styles.notice} ${styles.noticeSuccess}`.trim()}>
            Preparing your authenticator secret and recovery codes...
          </div>
        ) : null}

        {setup ? (
          <>
            <div className={styles.gridTwo}>
              <div className={styles.summaryCard}>
                <h4>Scan the QR code</h4>
                <div style={{ display: 'flex', justifyContent: 'center', marginTop: '0.5rem' }}>
                  <Image
                    src={setup.qrCodeDataUrl}
                    alt="Authenticator QR code"
                    width={220}
                    height={220}
                    unoptimized
                    style={{ borderRadius: '1rem', background: '#fff', padding: '0.75rem' }}
                  />
                </div>
              </div>

              <div className={styles.summaryCard}>
                <h4>Or enter this secret key</h4>
                <p style={{ marginBottom: '0.65rem' }}>
                  If you can&apos;t scan the QR code, enter the key manually in your authenticator app.
                </p>
                <div
                  style={{
                    borderRadius: '1rem',
                    border: '1px solid rgba(117, 131, 178, 0.16)',
                    background: 'rgba(255, 255, 255, 0.82)',
                    padding: '0.95rem',
                    color: 'var(--auth-text)',
                    fontFamily: 'var(--font-geist-mono, monospace)',
                    fontSize: '0.9rem',
                    lineHeight: 1.5,
                    wordBreak: 'break-all',
                  }}
                >
                  {setup.secretKey}
                </div>
              </div>
            </div>

            <div className={styles.summaryCard}>
              <h4>Save your recovery codes</h4>
              <div className={styles.successBadgeMeta}>
                {setup.recoveryCodes.map((recoveryCode) => (
                  <span key={recoveryCode}>{recoveryCode}</span>
                ))}
              </div>
              <p style={{ marginTop: '0.75rem' }}>
                Recovery codes let you sign in if your authenticator device is unavailable. Store them somewhere safe.
              </p>
            </div>
          </>
        ) : null}

        {error ? <div className={`${styles.notice} ${styles.noticeDanger}`.trim()}>{error}</div> : null}

        <form className={styles.passwordFlowForm} onSubmit={handleConfirm}>
          <div className={styles.field}>
            <label>Authenticator code</label>
            <OtpCodeInput value={code} onChange={(next) => {
              setCode(next.replace(/\D/g, '').slice(0, 6));
              setError(null);
            }} disabled={!setup || isConfirming} />
            <p className={styles.fieldHint}>Enter the current 6-digit code from the authenticator app you just configured.</p>
          </div>

          <button type="submit" className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()} disabled={!setup || isConfirming}>
            <span>{isConfirming ? 'Finishing setup...' : 'Finish MFA Setup'}</span>
            {!isConfirming ? <IconArrowRight size={18} /> : null}
          </button>

          <div className={styles.summaryCard}>
            <h4>After confirmation</h4>
            <div className={styles.summaryList}>
              <div className={styles.summaryItem}>
                <span className={styles.summaryIcon}>
                  <IconKey size={16} />
                </span>
                <p>The platform will route you into the correct post-auth destination using the same role-aware logic as sign-in.</p>
              </div>
            </div>
          </div>
        </form>
      </div>
    </AuthScreenShell>
  );
}
