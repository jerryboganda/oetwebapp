'use client';

import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { CheckCircle2, Copy, ExternalLink, Loader2 } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import {
  cancelConnect,
  describeOwnerAgentError,
  getConnectFlow,
  submitConnectCode,
} from '@/lib/owner-agent/api';
import { ANTHROPIC_SIGN_IN_HOSTS, OPENAI_SIGN_IN_HOSTS, safeExternalUrl } from '@/lib/owner-agent/text-safety';
import type { ConnectFlow } from '@/lib/owner-agent/types';
import { ENGINE_LABEL } from './EngineModelEffortPicker';
import { VisibleText } from './VisibleText';

const POLL_MS = 2_500;
const TERMINAL_STATES: ReadonlySet<ConnectFlow['state']> = new Set(['completed', 'failed', 'cancelled', 'expired']);

export function isConnectFlowFinished(flow: ConnectFlow | null | undefined): boolean {
  return Boolean(flow && TERMINAL_STATES.has(flow.state));
}

function useCountdown(expiresAt: string | undefined): number | null {
  const target = useMemo(() => {
    const parsed = expiresAt ? Date.parse(expiresAt) : Number.NaN;
    return Number.isFinite(parsed) ? parsed : null;
  }, [expiresAt]);
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (target === null) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [target]);
  return target === null ? null : Math.max(0, Math.floor((target - now) / 1000));
}

export interface ConnectEngineDialogProps {
  /** The flow returned by POST /auth/{engine}/connect. */
  flow: ConnectFlow | null;
  onClose: () => void;
  /** Called once when the flow reaches a terminal state. */
  onFinished?: (flow: ConnectFlow) => void;
}

/**
 * Relays the vendor's own sign-in flow; the console never sees a credential.
 * - Claude (paste_code): open the Anthropic URL, sign in, paste the code the
 *   page shows back here; the CLI in the sidecar stores its own credentials.
 * - Codex (device_code): open the OpenAI verification URL and enter the user
 *   code before the countdown ends; completion is detected by polling.
 * Claude/Codex links use approved vendor hosts.
 */
export function ConnectEngineDialog({ flow: initialFlow, onClose, onFinished }: ConnectEngineDialogProps) {
  const [flow, setFlow] = useState<ConnectFlow | null>(initialFlow);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const finishedRef = useRef<string | null>(null);

  useEffect(() => {
    setFlow(initialFlow);
    setCode('');
    setError(null);
    setCopied(false);
  }, [initialFlow]);

  const finished = isConnectFlowFinished(flow);

  // Poll the flow until it is terminal.
  const flowEngine = flow?.engine;
  const flowId = flow?.flowId;
  useEffect(() => {
    if (!flowEngine || !flowId || finished) return;
    let cancelled = false;
    const id = setInterval(() => {
      void getConnectFlow(flowEngine, flowId)
        .then((next) => {
          if (!cancelled && next) setFlow(next);
        })
        .catch((err) => {
          if (!cancelled) setError(describeOwnerAgentError(err, 'Could not refresh the sign-in status.'));
        });
    }, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [flowEngine, flowId, finished]);

  useEffect(() => {
    if (flow && finished && finishedRef.current !== flow.flowId) {
      finishedRef.current = flow.flowId;
      onFinished?.(flow);
    }
  }, [flow, finished, onFinished]);

  const countdown = useCountdown(flow?.kind === 'device_code' ? flow.expiresAt : undefined);

  if (!flow) return null;

  const safeUrl = safeExternalUrl(flow.verificationUrl, flow.engine === 'claude' ? ANTHROPIC_SIGN_IN_HOSTS : OPENAI_SIGN_IN_HOSTS);

  const close = () => {
    if (!finished) {
      void cancelConnect(flow.engine, flow.flowId).catch(() => undefined);
    }
    onClose();
  };

  const submitCode = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = code.trim();
    if (!trimmed || busy) return;
    setBusy(true);
    setError(null);
    try {
      const next = await submitConnectCode(flow.engine, flow.flowId, trimmed);
      setFlow(next);
      setCode('');
    } catch (err) {
      setError(describeOwnerAgentError(err, 'The code was not accepted.'));
    } finally {
      setBusy(false);
    }
  };

  const copyUserCode = async () => {
    if (!flow.userCode || typeof navigator === 'undefined' || !navigator.clipboard) return;
    try {
      await navigator.clipboard.writeText(flow.userCode);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  return (
    <Modal open onClose={close} title={`Connect ${ENGINE_LABEL[flow.engine]}`} size="md">
      <div className="space-y-4 text-sm" data-testid="connect-engine-dialog">
        {flow.verificationUrl ? (
          <div className="space-y-1">
            <p className="font-medium text-admin-fg-strong">
              1. {flow.engine === 'opencode'
                ? `Authorize ${flow.providerName ?? 'the OpenCode provider'}`
                : flow.kind === 'paste_code' ? 'Sign in with Anthropic' : 'Open the OpenAI device page'}
            </p>
            {safeUrl ? (
              <a
                href={safeUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 break-all text-[var(--admin-primary)] underline underline-offset-2"
              >
                {new URL(safeUrl).hostname}
                <ExternalLink className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              </a>
            ) : (
              <p className="rounded-lg bg-red-50 p-2 text-xs text-red-800 dark:bg-red-950 dark:text-red-200" role="alert">
                The sign-in URL is not an approved https link, so it is not clickable:{' '}
                <span className="font-mono"><VisibleText text={flow.verificationUrl} /></span>
              </p>
            )}
            {safeUrl ? <p className="break-all font-mono text-2xs text-admin-fg-muted">{safeUrl}</p> : null}
          </div>
        ) : !finished ? (
          <p className="flex items-center gap-2 text-admin-fg-muted">
            <Loader2 className="h-4 w-4 animate-spin motion-reduce:animate-none" aria-hidden="true" /> Waiting for the sign-in link…
          </p>
        ) : null}

        {flow.kind === 'device_code' && flow.userCode && !finished ? (
          <div className="space-y-1">
            <p className="font-medium text-admin-fg-strong">2. Enter this code</p>
            <div className="flex items-center gap-2">
              <code className="rounded-lg border border-admin-border bg-admin-bg-subtle px-3 py-2 font-mono text-xl tracking-[0.2em]" data-testid="device-user-code">
                {flow.userCode}
              </code>
              <Button variant="ghost" size="sm" onClick={() => void copyUserCode()} aria-label="Copy code">
                <Copy className="h-4 w-4" aria-hidden="true" />
                {copied ? 'Copied' : 'Copy'}
              </Button>
            </div>
            {countdown !== null ? (
              <p className={countdown < 60 ? 'text-xs text-red-700 dark:text-red-300' : 'text-xs text-admin-fg-muted'}>
                Expires in {Math.floor(countdown / 60)}:{String(countdown % 60).padStart(2, '0')}
              </p>
            ) : null}
          </div>
        ) : null}

        {flow.kind === 'paste_code' && !finished ? (
          <form onSubmit={(event) => void submitCode(event)} className="space-y-1">
            <label htmlFor="owner-agent-connect-code" className="font-medium text-admin-fg-strong">
              2. {flow.engine === 'opencode' ? 'Paste the authorization code' : 'Paste the code shown after sign-in'}
            </label>
            <div className="flex gap-2">
              <input
                id="owner-agent-connect-code"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                autoComplete="off"
                spellCheck={false}
                className="h-10 min-w-0 flex-1 rounded-lg border border-admin-border bg-admin-bg-surface px-3 font-mono text-sm"
                disabled={busy}
              />
              <Button type="submit" variant="primary" size="sm" loading={busy} disabled={!code.trim()}>
                Submit
              </Button>
            </div>
          </form>
        ) : null}

        {flow.state === 'completed' ? (
          <p className="flex items-center gap-2 text-emerald-700 dark:text-emerald-300" role="status">
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> Connected.
          </p>
        ) : null}
        {flow.state === 'failed' || flow.state === 'expired' || flow.state === 'cancelled' ? (
          <p className="text-red-700 dark:text-red-300" role="alert">
            Sign-in {flow.state}.{flow.detail ? ` ${flow.detail}` : ''}
          </p>
        ) : null}
        {!finished && flow.detail ? <p className="text-xs text-admin-fg-muted">{flow.detail}</p> : null}
        {error ? (
          <p className="text-xs text-red-700 dark:text-red-300" role="alert">
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2">
          <Button variant={finished ? 'primary' : 'ghost'} size="sm" onClick={close}>
            {finished ? 'Close' : 'Cancel sign-in'}
          </Button>
        </div>
      </div>
    </Modal>
  );
}

