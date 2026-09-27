'use client';

import { useCallback, useEffect, useMemo, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useRouter } from 'next/navigation';
import { Archive, Send, Square } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { useOwnerAgentSession } from '@/hooks/use-owner-agent';
import { describeOwnerAgentError } from '@/lib/owner-agent/api';
import type { Mode, SessionDiff, ShipState } from '@/lib/owner-agent/types';
import {
  ENGINE_LABEL,
  EngineModelEffortPicker,
  normalizePickerValue,
  type EngineModelEffortValue,
} from './EngineModelEffortPicker';
import { HandOffButton } from './HandOffButton';
import { MessageStream } from './MessageStream';
import { ModeToggle } from './ModeToggle';
import { useOwnerAgentConsole } from './OwnerAgentConsoleShell';
import { RateLimitBadge } from './RateLimitBadge';
import { SessionDiffViewer } from './SessionDiffViewer';
import { SESSION_STATUS_BADGE } from './SessionsList';
import { ShipPanel, isShipActive } from './ShipPanel';
import { UsageMeter } from './UsageMeter';

type TabId = 'conversation' | 'diff' | 'ship';
const SHIP_POLL_MS = 5_000;

export interface SessionWorkspaceProps {
  sessionId: string;
}

/** Conversation, composer, mode/taint, hand-off, diff and ship for one session. */
export function SessionWorkspace({ sessionId }: SessionWorkspaceProps) {
  const router = useRouter();
  const consoleState = useOwnerAgentConsole();
  const session = useOwnerAgentSession(sessionId, { enabled: consoleState.unlocked });
  const { detail, model } = session;
  const engines = consoleState.status?.engines ?? null;

  const [tab, setTab] = useState<TabId>('conversation');
  const [draft, setDraft] = useState('');
  const [turnPick, setTurnPick] = useState<EngineModelEffortValue | null>(null);
  const [sending, setSending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [pendingMode, setPendingMode] = useState<Mode | null>(null);
  const [diff, setDiff] = useState<SessionDiff | null>(null);
  const [diffLoading, setDiffLoading] = useState(false);
  const [diffError, setDiffError] = useState<string | null>(null);
  const [ship, setShip] = useState<ShipState | null>(null);

  // Per-turn picker starts from the session's model/effort once detail arrives.
  const detailEngine = detail?.engine;
  const detailModel = detail?.model;
  const detailEffort = detail?.effort;
  useEffect(() => {
    if (!detailEngine || !detailModel) return;
    setTurnPick((current) =>
      current && current.engine === detailEngine
        ? normalizePickerValue(engines, current, { lockEngine: true })
        : normalizePickerValue(engines, { engine: detailEngine, model: detailModel, effort: detailEffort }, { lockEngine: true }),
    );
  }, [detailEngine, detailModel, detailEffort, engines]);

  // Detail is re-fetched on every mode_changed event and replaced by PATCH responses.
  const mode: Mode = detail?.mode ?? model.mode ?? 'read_only';
  const archived = detail?.status === 'archived';
  const blocked = Boolean(consoleState.status?.killed || consoleState.status?.draining);
  const taintReasons = useMemo(() => model.taints.map((t) => `${t.source}: ${t.reason}`), [model.taints]);

  const report = (error: unknown, fallback: string) => {
    setActionError(describeOwnerAgentError(error, fallback));
  };

  const send = async (event?: FormEvent) => {
    event?.preventDefault();
    const text = draft.trim();
    if (!text || sending || session.running || archived || blocked) return;
    setSending(true);
    setActionError(null);
    try {
      await session.send(draft, turnPick ? { model: turnPick.model, effort: turnPick.effort } : undefined);
      setDraft('');
    } catch (error) {
      report(error, 'Message was not sent.');
    } finally {
      setSending(false);
    }
  };

  const onComposerKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      void send();
    }
  };

  const changeMode = async (next: Mode) => {
    setPendingMode(next);
    setActionError(null);
    try {
      await session.patch({ mode: next });
    } catch (error) {
      report(error, 'Mode was not changed.');
    } finally {
      setPendingMode(null);
    }
  };

  const toggleArchive = async () => {
    setActionError(null);
    try {
      await session.patch({ archived: !archived });
      void consoleState.refreshSessions();
    } catch (error) {
      report(error, 'Archive state was not changed.');
    }
  };

  const interrupt = async () => {
    setActionError(null);
    try {
      await session.interrupt();
    } catch (error) {
      report(error, 'Interrupt failed.');
    }
  };

  const { loadDiff: fetchDiff, loadShip } = session;
  const loadDiff = useCallback(async () => {
    setDiffLoading(true);
    setDiffError(null);
    try {
      setDiff(await fetchDiff());
    } catch (error) {
      setDiffError(describeOwnerAgentError(error, 'Diff unavailable.'));
    } finally {
      setDiffLoading(false);
    }
  }, [fetchDiff]);

  const refreshShip = useCallback(async () => {
    try {
      setShip(await loadShip());
    } catch (error) {
      setActionError(describeOwnerAgentError(error, 'Ship status unavailable.'));
    }
  }, [loadShip]);

  const selectTab = (id: string) => {
    const next = id as TabId;
    setTab(next);
    if (next === 'diff' && !diff && !diffLoading) void loadDiff();
    if (next === 'ship') void refreshShip();
  };

  const shipActive = isShipActive(ship);
  useEffect(() => {
    if (tab !== 'ship' || !shipActive) return;
    const id = setInterval(() => void refreshShip(), SHIP_POLL_MS);
    return () => clearInterval(id);
  }, [tab, shipActive, refreshShip]);

  const startShip = async (body: { prTitle?: string; prBody?: string }) => {
    try {
      setShip(await session.ship(body));
    } catch (error) {
      throw new Error(describeOwnerAgentError(error, 'Ship failed to start.'));
    }
  };

  const handoff = async (value: EngineModelEffortValue) => {
    try {
      const next = await session.handoff(value.engine, value.model, value.effort);
      void consoleState.refreshSessions();
      router.push(`/admin/agent-console/${encodeURIComponent(next.id)}`);
    } catch (error) {
      throw new Error(describeOwnerAgentError(error, 'Hand-off failed.'));
    }
  };

  if (session.detailState === 'error' && !detail) {
    return (
      <EmptyState
        variant="error"
        title="Session unavailable"
        description={session.detailError ?? 'This session could not be loaded.'}
        primaryAction={{ label: 'Back to sessions', href: '/admin/agent-console' }}
      />
    );
  }

  const status = detail ? SESSION_STATUS_BADGE[detail.status] ?? SESSION_STATUS_BADGE.idle : null;
  const pendingCount = session.pendingApprovals.length;

  return (
    <AdminOperationsLayout
      title={detail?.title || 'Agent session'}
      eyebrow="Owner only"
      breadcrumbs={[{ label: 'Agent Console', href: '/admin/agent-console' }, { label: detail?.title || sessionId }]}
      description={
        detail
          ? `${ENGINE_LABEL[detail.engine] ?? detail.engine} · ${detail.model}${detail.effort ? ` · ${detail.effort}` : ''} · ${detail.branch}`
          : 'Loading session…'
      }
      actions={
        <div className="flex flex-wrap items-center gap-2">
          {detail ? (
            <HandOffButton
              engines={engines}
              currentEngine={detail.engine}
              disabled={session.running || archived || blocked}
              onHandoff={handoff}
            />
          ) : null}
          <Button variant="ghost" size="sm" onClick={() => void toggleArchive()} disabled={!detail || session.running}>
            <Archive className="h-4 w-4" aria-hidden="true" />
            {archived ? 'Unarchive' : 'Archive'}
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-3 rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2">
          <ModeToggle
            value={mode}
            onChange={(next) => void changeMode(next)}
            disabled={archived || !detail}
            tainted={session.tainted}
            taintReasons={taintReasons}
            pendingMode={pendingMode}
          />
          {status ? <Badge variant={status.variant}>{status.label}</Badge> : null}
          {pendingCount > 0 ? <Badge variant="warning">{pendingCount} approval(s) waiting</Badge> : null}
          <Badge variant={session.connectionState === 'connected' ? 'success' : 'muted'}>stream {session.connectionState}</Badge>
          {detail ? <RateLimitBadge limits={model.rateLimits[detail.engine] ?? engines?.[detail.engine]?.rateLimits} /> : null}
          <UsageMeter live={model.usage} summary={detail?.usage} className="ml-auto" />
        </div>

        {session.streamError && session.connectionState !== 'connected' ? (
          <p className="text-xs text-amber-700 dark:text-amber-300" role="status">
            {session.streamError}
          </p>
        ) : null}
        {actionError ? (
          <p className="text-xs text-red-700 dark:text-red-300" role="alert">
            {actionError}
          </p>
        ) : null}

        <Tabs
          tabs={[
            { id: 'conversation', label: 'Conversation', count: pendingCount || undefined },
            { id: 'diff', label: 'Diff' },
            { id: 'ship', label: 'Ship' },
          ]}
          activeTab={tab}
          onChange={selectTab}
        />

        <TabPanel id="conversation" activeTab={tab} className="space-y-3">
          <MessageStream
            items={model.items}
            pendingApprovals={session.pendingApprovals}
            onDecide={session.decide}
            emptyHint={session.connectionState === 'connected' ? 'No activity yet. Send the first message.' : 'Connecting to the session stream…'}
            className="max-h-[65vh] min-h-[16rem] rounded-lg border border-admin-border bg-admin-bg-subtle p-3"
          />
          <form onSubmit={(event) => void send(event)} className="space-y-2 rounded-lg border border-admin-border bg-admin-bg-surface p-3">
            <EngineModelEffortPicker
              engines={engines}
              value={turnPick}
              onChange={setTurnPick}
              lockEngine
              compact
              sessionModel={detail ? { model: detail.model, effort: detail.effort } : null}
              disabled={sending || session.running}
            />
            <label htmlFor="owner-agent-composer" className="sr-only">
              Message
            </label>
            <textarea
              id="owner-agent-composer"
              value={draft}
              rows={4}
              onChange={(event) => setDraft(event.target.value)}
              onKeyDown={onComposerKeyDown}
              placeholder={archived ? 'Archived sessions are read-only.' : 'Message the agent (Ctrl/Cmd + Enter to send)'}
              disabled={archived || blocked}
              className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
            />
            <div className="flex flex-wrap items-center justify-end gap-2">
              {blocked ? <span className="mr-auto text-xs text-admin-fg-muted">The console is stopped or draining; no new turns.</span> : null}
              {session.running ? (
                <Button type="button" variant="destructive" size="sm" onClick={() => void interrupt()}>
                  <Square className="h-4 w-4" aria-hidden="true" /> Interrupt
                </Button>
              ) : null}
              <Button type="submit" variant="primary" size="sm" loading={sending} disabled={!draft.trim() || session.running || archived || blocked}>
                <Send className="h-4 w-4" aria-hidden="true" /> Send
              </Button>
            </div>
          </form>
        </TabPanel>

        <TabPanel id="diff" activeTab={tab}>
          <SessionDiffViewer diff={diff} loading={diffLoading} error={diffError} onRefresh={() => void loadDiff()} />
        </TabPanel>

        <TabPanel id="ship" activeTab={tab}>
          <ShipPanel
            ship={ship}
            log={model.shipLog}
            branch={detail?.branch}
            onShip={startShip}
            onRefresh={() => void refreshShip()}
            disabled={!detail || session.running || blocked}
            disabledReason={session.running ? 'Wait for the current turn to finish before shipping.' : undefined}
          />
        </TabPanel>
      </div>
    </AdminOperationsLayout>
  );
}
