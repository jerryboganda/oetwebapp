'use client';

/**
 * Permanent admin guide for the AI Pipelines page (owner directive 2026-10-10): how to run every part of the AI
 * infrastructure without a developer. It lives on the page itself, so it cannot drift into a document nobody
 * opens. The flowcharts are drawn from the SAVED order when the page passes it in, so the picture is always the
 * real configuration, never a stale screenshot. "Download PDF" prints just this guide (browser Save as PDF).
 */

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ArrowDown, ArrowRight, Download } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import type { PipelineStage } from '@/lib/api/ai-pipelines';

const FRIENDLY_NAMES: Record<string, string> = {
  'writing-claude-sub': 'Claude Max subscription',
  anthropic: 'Claude API (Anthropic, paid)',
  'writing-codex-sub': 'ChatGPT / Codex subscription',
  'z-ai': 'Z.AI GLM',
  openai: 'OpenAI GPT Live',
  gemini: 'Gemini Live',
};

type Tone = 'start' | 'step' | 'backup' | 'result' | 'warn' | 'off';

const TONE_CLASS: Record<Tone, string> = {
  start: 'border-admin-border bg-admin-bg-subtle',
  step: 'border-[var(--admin-primary)] bg-[var(--admin-primary-tint)]',
  backup: 'border-amber-500/60 bg-amber-500/10',
  result: 'border-emerald-600/60 bg-emerald-500/10',
  warn: 'border-[var(--admin-danger)] bg-[var(--admin-danger-tint)]',
  off: 'border-dashed border-admin-border bg-transparent opacity-70',
};

interface FlowNode {
  title: string;
  detail?: string;
  tone: Tone;
  /** Text on the arrow that leads INTO this node. */
  arrow?: string;
}

/** A left-to-right (top-to-bottom on small screens) flowchart with a label on every arrow. */
function Flow({ nodes, label }: { nodes: FlowNode[]; label: string }) {
  return (
    <ol className="flex flex-col gap-2 md:flex-row md:flex-wrap md:items-stretch" aria-label={label}>
      {nodes.map((node, i) => (
        <li key={`${node.title}-${i}`} className="flex flex-col items-stretch gap-2 md:flex-row md:items-center">
          {i > 0 && (
            <span className="flex items-center justify-center gap-1 text-2xs text-admin-fg-muted md:flex-col" aria-hidden="true">
              <ArrowRight className="hidden h-4 w-4 md:block" />
              <ArrowDown className="h-4 w-4 md:hidden" />
              {node.arrow ? <span className="max-w-[7rem] text-center leading-tight">{node.arrow}</span> : null}
            </span>
          )}
          <div className={`min-w-[9rem] max-w-[15rem] rounded-admin-lg border p-3 text-sm ${TONE_CLASS[node.tone]}`}>
            <p className="font-semibold text-admin-fg-strong">{node.title}</p>
            {node.detail ? <p className="mt-1 text-2xs leading-snug text-admin-fg-default">{node.detail}</p> : null}
          </div>
        </li>
      ))}
    </ol>
  );
}

function Mock({ children, caption }: { children: ReactNode; caption: string }) {
  return (
    <figure className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3">
      {children}
      <figcaption className="mt-2 text-2xs text-admin-fg-muted">{caption}</figcaption>
    </figure>
  );
}

function MockRow({ n, name, status, hot }: { n: number; name: string; status: string; hot?: boolean }) {
  return (
    <div className={`flex items-center justify-between gap-2 rounded-admin-md border px-2 py-1.5 text-sm ${hot ? 'border-[var(--admin-primary)] bg-[var(--admin-primary-tint)]' : 'border-admin-border'}`}>
      <span className="font-medium text-admin-fg-strong">{n}. {name}</span>
      <span className="text-2xs text-admin-fg-muted">{status}</span>
    </div>
  );
}

function Steps({ items }: { items: ReactNode[] }) {
  return (
    <ol className="space-y-2">
      {items.map((item, i) => (
        <li key={i} className="flex gap-3 text-sm text-admin-fg-default">
          <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--admin-primary)] text-xs font-semibold text-white">{i + 1}</span>
          <span className="min-w-0">{item}</span>
        </li>
      ))}
    </ol>
  );
}

function Callout({ tone = 'info', title, children }: { tone?: 'info' | 'warn' | 'ok'; title: string; children: ReactNode }) {
  const cls = tone === 'warn'
    ? 'border-amber-500/50 bg-amber-500/10'
    : tone === 'ok'
      ? 'border-emerald-600/50 bg-emerald-500/10'
      : 'border-admin-border bg-admin-bg-subtle';
  return (
    <div className={`rounded-admin-lg border p-3 text-sm ${cls}`} role="note">
      <p className="font-semibold text-admin-fg-strong">{title}</p>
      <div className="mt-1 space-y-1 text-admin-fg-default">{children}</div>
    </div>
  );
}

function Section({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <details id={`guide-${id}`} className="group rounded-admin-lg border border-admin-border bg-admin-bg-surface">
      <summary className="flex cursor-pointer list-none items-center justify-between gap-2 p-4 text-base font-semibold text-admin-fg-strong marker:hidden">
        <span>{title}</span>
        <span className="text-xs font-normal text-admin-fg-muted group-open:hidden print:hidden">Open</span>
        <span className="hidden text-xs font-normal text-admin-fg-muted group-open:inline print:hidden">Close</span>
      </summary>
      <div className="space-y-4 border-t border-admin-border p-4">{children}</div>
    </details>
  );
}

function nameOf(provider: string, names: Record<string, string> | undefined): string {
  return names?.[provider] ?? FRIENDLY_NAMES[provider] ?? provider;
}

/** The saved order of one stage as flowchart nodes: trigger, each step (off steps dashed), the safe outcome. */
function stageFlow(
  stage: PipelineStage | undefined,
  fallbackOrder: Array<{ provider: string; model?: string }>,
  trigger: FlowNode,
  success: FlowNode,
  failure: FlowNode,
  names: Record<string, string> | undefined,
  live: boolean,
): FlowNode[] {
  interface FlowHop {
    provider: string;
    model: string | undefined;
    enabled: boolean;
    attempts: number;
    seconds: number;
    status: string;
  }
  const hops: FlowHop[] = stage
    ? stage.hops.map((h) => ({ provider: h.provider, model: h.model ?? undefined, enabled: h.enabled, attempts: h.attempts, seconds: h.budgetSeconds, status: h.status }))
    : fallbackOrder.map((h) => ({ provider: h.provider, model: h.model, enabled: true, attempts: 1, seconds: 0, status: 'ready' }));

  const nodes: FlowNode[] = [trigger];
  let firstEnabledSeen = false;
  hops.forEach((h) => {
    if (!h.enabled) {
      nodes.push({ title: nameOf(h.provider, names), detail: 'Switched OFF in this pipeline: skipped, never called.', tone: 'off', arrow: 'skipped' });
      return;
    }
    const unusable = h.status !== 'ready';
    const first = !firstEnabledSeen && !unusable;
    if (!unusable) firstEnabledSeen = true;
    const detail = [
      h.model ? `Model: ${h.model}` : null,
      !live ? `${h.attempts} attempt${h.attempts === 1 ? '' : 's'}${h.seconds ? `, ${h.seconds}s each` : ''}` : null,
      unusable ? `Left out of the next run: ${h.status.replace(/^[^:]+:\s*/, '')}` : null,
    ].filter(Boolean).join(' · ');
    nodes.push({
      title: `${first ? 'Primary: ' : unusable ? 'Unavailable: ' : 'Backup: '}${nameOf(h.provider, names)}`,
      detail,
      tone: unusable ? 'warn' : first ? 'step' : 'backup',
      arrow: nodes.length === 1 ? 'starts here' : 'if the step before fails',
    });
  });
  nodes.push({ ...success, arrow: 'a step succeeds' });
  nodes.push({ ...failure, arrow: 'every step fails' });
  return nodes;
}

export interface PipelineGuideProps {
  stages?: PipelineStage[] | null;
  providerNames?: Record<string, string>;
}

export function PipelineGuide({ stages, providerNames }: PipelineGuideProps) {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [expanded, setExpanded] = useState(false);

  // Restores the page after printing, however the dialog was closed.
  useEffect(() => {
    const restore = () => {
      document.body.classList.remove('print-ai-guide');
      rootRef.current?.querySelectorAll('details').forEach((d) => {
        if (d.dataset.wasOpen === '0') d.open = false;
        delete d.dataset.wasOpen;
      });
    };
    window.addEventListener('afterprint', restore);
    return () => window.removeEventListener('afterprint', restore);
  }, []);

  function downloadPdf() {
    setExpanded(true);
    // Let the section mount and expand, then print only this guide (see the @media print rules below).
    window.setTimeout(() => {
      rootRef.current?.querySelectorAll('details').forEach((d) => {
        d.dataset.wasOpen = d.open ? '1' : '0';
        d.open = true;
      });
      document.body.classList.add('print-ai-guide');
      window.print();
    }, 150);
  }

  const stage = (key: string) => stages?.find((s) => s.stageKey === key);
  const live = stages !== null && stages !== undefined;

  return (
    <div id="ai-pipeline-guide" ref={rootRef} className="space-y-3">
      <style>{`
        @media print {
          body.print-ai-guide * { visibility: hidden !important; }
          body.print-ai-guide #ai-pipeline-guide,
          body.print-ai-guide #ai-pipeline-guide * { visibility: visible !important; }
          body.print-ai-guide #ai-pipeline-guide { position: absolute; left: 0; top: 0; width: 100%; }
          body.print-ai-guide #ai-pipeline-guide details { break-inside: avoid-page; }
        }
      `}</style>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm text-admin-fg-default">
            Everything you need to run the AI pipelines yourself: what each pipeline does, how to switch things on and
            off, change the order, add a key, and read the costs. {live ? 'The diagrams below show your current saved order.' : 'The diagrams show the built-in order until the page has loaded.'}
          </p>
        </div>
        <div className="flex gap-2 print:hidden">
          <Button variant="outline" size="sm" onClick={() => setExpanded((v) => !v)} aria-expanded={expanded}>
            {expanded ? 'Hide the guide' : 'Open the guide'}
          </Button>
          <Button size="sm" onClick={downloadPdf}>
            <Download className="h-4 w-4" aria-hidden="true" />
            Download PDF
          </Button>
        </div>
      </div>

      {expanded && (
        <div className="space-y-3">
          {/* 0 — the big picture */}
          <Section id="overview" title="1. The whole system on one page">
            <p className="text-sm text-admin-fg-default">
              Five pipelines sit between a candidate and a result. Each one is an ordered list of providers: the first
              working one answers, the others are backups that take over automatically if it fails. You never edit code:
              you edit the order and the on/off switches on this page.
            </p>
            <Flow
              label="Overview of the five pipelines"
              nodes={[
                { title: 'Candidate writes a letter', detail: 'Saved first, so nothing is ever lost.', tone: 'start' },
                { title: 'Writing grading', detail: 'Pipeline 1', tone: 'step', arrow: 'submit' },
                { title: 'Writing reviewer', detail: 'Pipeline 2 (optional second reader)', tone: 'backup', arrow: 'grade exists' },
                { title: 'Result shown', tone: 'result', arrow: 'done' },
              ]}
            />
            <Flow
              label="Overview of the Speaking pipelines"
              nodes={[
                { title: 'Candidate starts the role-play', tone: 'start' },
                { title: 'Live voice agent', detail: 'Pipeline 5 (the patient talks)', tone: 'step', arrow: 'conversation' },
                { title: 'Speaking grading', detail: 'Pipeline 3', tone: 'step', arrow: 'transcript saved' },
                { title: 'Speaking reviewer', detail: 'Pipeline 4 (optional)', tone: 'backup', arrow: 'grade exists' },
                { title: 'Provisional result shown', tone: 'result', arrow: 'done' },
              ]}
            />
            <Callout title="Two rules the system keeps for you">
              <p>A candidate never loses work: submissions are saved before any provider is called, and a failed grade stays retryable.</p>
              <p>Every change you save is recorded with who, when and why, and can be undone from History.</p>
            </Callout>
          </Section>

          {/* A — the five pipelines */}
          <Section id="pipelines" title="2. The five pipelines: who is primary, who is backup, how fallback works">
            <div className="space-y-6">
              <div className="space-y-2">
                <p className="font-semibold text-admin-fg-strong">Pipeline 1 — Writing grading</p>
                <p className="text-sm text-admin-fg-default">Grades every submitted letter. It cannot be switched off as a whole.</p>
                <Flow
                  label="Writing grading flow"
                  nodes={stageFlow(
                    stage('writing.grade'),
                    [{ provider: 'writing-claude-sub' }, { provider: 'anthropic' }, { provider: 'writing-codex-sub' }, { provider: 'z-ai' }],
                    { title: 'Letter submitted', tone: 'start' },
                    { title: 'Grade saved and shown', tone: 'result' },
                    { title: 'Letter stays saved and can be retried', detail: 'The candidate is not charged twice.', tone: 'warn' },
                    providerNames,
                    false,
                  )}
                />
              </div>

              <div className="space-y-2">
                <p className="font-semibold text-admin-fg-strong">Pipeline 2 — Writing reviewer</p>
                <p className="text-sm text-admin-fg-default">A second reader of a finished grade. It has its own on/off switch and its own order.</p>
                <Flow
                  label="Writing reviewer flow"
                  nodes={stageFlow(
                    stage('writing.grade.review'),
                    [{ provider: 'writing-codex-sub' }, { provider: 'anthropic' }, { provider: 'z-ai' }],
                    { title: 'A Writing grade exists', tone: 'start' },
                    { title: 'Reviewer may adjust the grade', detail: 'Only through the controlled applier.', tone: 'result' },
                    { title: 'Grade kept as it was', detail: 'A failed review never blocks the candidate.', tone: 'warn' },
                    providerNames,
                    false,
                  )}
                />
                <p className="text-2xs text-admin-fg-muted">Reviewer switched off: the grade is published on the first grader alone and no review cost is spent.</p>
              </div>

              <div className="space-y-2">
                <p className="font-semibold text-admin-fg-strong">Pipeline 3 — Speaking grading</p>
                <p className="text-sm text-admin-fg-default">Grades the saved transcript of a role-play (one card) or a full two-card mock. The approved model is Claude Opus 5.5 at HIGH effort on every Claude route.</p>
                <Flow
                  label="Speaking grading flow"
                  nodes={stageFlow(
                    stage('speaking.grade'),
                    [{ provider: 'writing-claude-sub' }, { provider: 'anthropic' }, { provider: 'z-ai' }],
                    { title: 'Transcript saved', tone: 'start' },
                    { title: 'Provisional band shown', detail: 'Labelled "Provisional — calibration in progress".', tone: 'result' },
                    { title: 'Retryable; nothing is lost', tone: 'warn' },
                    providerNames,
                    false,
                  )}
                />
              </div>

              <div className="space-y-2">
                <p className="font-semibold text-admin-fg-strong">Pipeline 4 — Speaking reviewer</p>
                <p className="text-sm text-admin-fg-default">Re-reads the transcript and the grade and may move each criterion by at most one band.</p>
                <Flow
                  label="Speaking reviewer flow"
                  nodes={stageFlow(
                    stage('speaking.grade.review'),
                    [{ provider: 'writing-codex-sub' }, { provider: 'anthropic' }, { provider: 'z-ai' }],
                    { title: 'Speaking grade exists', tone: 'start' },
                    { title: 'Review applied (±1 band per criterion)', tone: 'result' },
                    { title: 'Claude’s grade kept', detail: 'Any failure keeps the first grade; no retry loop.', tone: 'warn' },
                    providerNames,
                    false,
                  )}
                />
              </div>

              <div className="space-y-2">
                <p className="font-semibold text-admin-fg-strong">Pipeline 5 — Speaking live voice</p>
                <p className="text-sm text-admin-fg-default">The voice of the patient in the role-play. New sessions use the order below; a session already running is never interrupted by a change.</p>
                <Flow
                  label="Live voice flow"
                  nodes={stageFlow(
                    stage('speaking.live_voice'),
                    [{ provider: 'openai' }, { provider: 'gemini' }],
                    { title: 'Candidate starts the conversation', tone: 'start' },
                    { title: 'Live conversation', tone: 'result' },
                    { title: 'Voice unavailable message; retry', tone: 'warn' },
                    providerNames,
                    true,
                  )}
                />
              </div>

              <Callout title="How automatic fallback works">
                <p>Steps are tried top to bottom. A step is skipped if it is switched off, its provider record is inactive, or it has no key. The step you are on gets its number of attempts within its time limit; if it fails (error, quota, timeout), the same submission moves to the next ready step. The next submission starts again at the top.</p>
                <p>Quota or credit errors never stop the candidate: they only move the work to the next step.</p>
              </Callout>
            </div>
          </Section>

          {/* B — enable / disable */}
          <Section id="onoff" title="3. Switching providers on and off — and what each switch really does">
            <div className="grid gap-3 md:grid-cols-2">
              <Callout title="Switch on one step (this pipeline only)">
                <p>Each row has an on/off switch. It affects only that pipeline. The provider stays available everywhere else. Use this to take one provider out of one stage.</p>
                <p>Press <strong>Save order</strong> for it to take effect. The change applies to the next grade or session on every server.</p>
              </Callout>
              <Callout title="Switch on the provider itself (global)" tone="warn">
                <p>In <strong>Keys &amp; providers</strong>, the Active switch turns a provider on or off for the whole platform. An inactive provider is skipped by every pipeline and by every other AI feature that uses it, and its steps show &ldquo;provider record inactive or missing&rdquo;.</p>
              </Callout>
              <Callout title="Switch the whole reviewer stage">
                <p>The reviewer sections have a top switch. Off means grades are published on the first grader alone. Grading stages cannot be switched off: at least one working step must always stay on.</p>
              </Callout>
              <Callout title="Claude Max has an extra guard" tone="warn">
                <p>Turning Claude Max off in a grading stage asks you to type <strong>DISABLE CLAUDE MAX</strong>. A banner at the top then reminds everyone that Max is not first or is off, and who changed it. &ldquo;Restore built-in order&rdquo; puts it back first.</p>
              </Callout>
            </div>
            <p className="font-semibold text-admin-fg-strong">What happens to a skipped provider</p>
            <p className="text-sm text-admin-fg-default">It is never called, never costs anything, and shows an &ldquo;Off&rdquo; or &ldquo;Unavailable&rdquo; badge. Runs already in progress finish on the order they started with.</p>
            <p className="font-semibold text-admin-fg-strong">How to confirm a change took effect</p>
            <Steps
              items={[
                <>The stage header shows a new version number (v7 → v8) and <strong>Next run starts on</strong> names the provider you expect.</>,
                <>Open <strong>History</strong>: your change is the top row with your name, the time and your reason.</>,
                <>After the next real submission, <strong>Last served by</strong> shows the provider and the exact model that answered. This is read from the call record, not from a setting.</>,
                <>Press <strong>Run check</strong> in <strong>Pipeline self-check</strong>: it confirms the plan follows the saved order and that switched-off steps were never called after the change.</>,
              ]}
            />
          </Section>

          {/* C — priorities */}
          <Section id="priorities" title="4. Changing priorities — worked example (Claude API first, Claude Max second)">
            <p className="text-sm text-admin-fg-default">Goal: while the promotional credit lasts, answer Speaking grading with the paid Claude API first, keep the Claude Max subscription as the second step, and keep an approved model as the third.</p>
            <div className="grid gap-3 md:grid-cols-2">
              <Mock caption="Before: Claude Max is first.">
                <div className="space-y-1.5">
                  <MockRow n={1} name="Claude Max subscription" status="Ready" />
                  <MockRow n={2} name="Claude API (Anthropic)" status="Ready" />
                  <MockRow n={3} name="Z.AI GLM" status="Ready" />
                </div>
              </Mock>
              <Mock caption="After: Claude API first, Max second, third unchanged.">
                <div className="space-y-1.5">
                  <MockRow n={1} name="Claude API (Anthropic)" status="Ready" hot />
                  <MockRow n={2} name="Claude Max subscription" status="Ready" hot />
                  <MockRow n={3} name="Z.AI GLM" status="Ready" />
                </div>
              </Mock>
            </div>
            <Steps
              items={[
                <>Find the <strong>Speaking grading</strong> card. Click the up arrow (or drag the grip) on <strong>Claude API</strong> until it is row 1.</>,
                <>Check <strong>Claude Max subscription</strong> is row 2. Use the arrows if it is not.</>,
                <>Leave the third row (the approved fallback) as it is. A model that leaves Claude must have a passing benchmark run before it can be enabled: see section 5.</>,
                <>Check the model of the Claude API row says <strong>claude-opus-5-5</strong>. The &ldquo;Model check&rdquo; strip under the card must be green.</>,
                <>Type a short reason (for example &ldquo;Use promo credit first&rdquo;) and press <strong>Save order</strong>. The header shows the new version.</>,
                <><strong>Verify:</strong> &ldquo;Next run starts on&rdquo; shows Claude API. After the next candidate submission, &ldquo;Last served by&rdquo; shows <strong>Claude API · claude-opus-5-5</strong>.</>,
              ]}
            />
            <Callout title="Go back, three ways">
              <p><strong>History → Restore this</strong> on any older row brings that exact order back as a new version (nothing is deleted).</p>
              <p><strong>Restore built-in order</strong> resets the stage to the factory order, which starts with Claude Max. It is also saved as a new version you can undo.</p>
              <p><strong>Discard</strong> abandons edits you have not saved yet.</p>
            </Callout>
            <Callout title="Automatic protection while promotional credit runs low" tone="warn">
              <p>When the remaining promotional balance falls to the reserve (default $20) the Claude API step is run <em>after</em> Claude Max for new runs; at the floor (default $5) it is left out. Your saved order is not changed, and a banner under Credits &amp; grants says so.</p>
            </Callout>
          </Section>

          {/* D — providers and keys */}
          <Section id="providers" title="5. Providers, API keys, models, connection tests and benchmarks">
            <Steps
              items={[
                <><strong>Add or rotate a key.</strong> In <strong>Keys &amp; providers</strong> press <em>Update key</em> (or <em>Add key</em>), paste the key and Save. It is encrypted on the server and never shown again; only a hint such as the last four characters is displayed.</>,
                <><strong>Choose the model.</strong> In the same editor type the model id, or press <em>Discover models</em> and pick from the list the provider reports.</>,
                <><strong>Test the connection.</strong> Press <em>Test</em>. A green <em>ok</em> with a latency means the key and model work. Anything else shows the reason (see the table below).</>,
                <><strong>Enable it globally.</strong> Switch <em>Active</em> on. Until a provider is active it cannot answer any pipeline.</>,
                <><strong>Add it to a pipeline.</strong> In the stage card choose it under <em>Add a step</em>, set its model, attempts and seconds, switch the step on.</>,
                <><strong>Set its priority.</strong> Move the row with the arrows or the grip, then <em>Save order</em>.</>,
                <><strong>Benchmark a new model first.</strong> A model that leaves Claude in a scoring stage must pass the benchmark: in <em>Benchmark a model</em> choose the stage, provider and model and press Run. A passing run gives a run id; paste it into the step&rsquo;s <em>Benchmark run id</em> field before saving. A run calls the model on a small test set, so it costs a few cents.</>,
              ]}
            />
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-2xs uppercase tracking-wider text-admin-fg-muted">
                    <th className="px-3 py-2 text-start">You see</th>
                    <th className="px-3 py-2 text-start">It means</th>
                    <th className="px-3 py-2 text-start">What to do</th>
                  </tr>
                </thead>
                <tbody>
                  {[
                    ['Ready', 'The step will be used on the next run.', 'Nothing.'],
                    ['Off', 'You switched the step off in this pipeline.', 'Switch it on and Save order if you want it back.'],
                    ['provider record inactive or missing', 'The provider is not Active in Keys & providers.', 'Switch the provider Active.'],
                    ['no credential', 'The provider has no key saved.', 'Add the key, then Test.'],
                    ['Test: auth', 'The key was rejected.', 'Paste a fresh key and Save.'],
                    ['Test: rate_limited', 'The provider is throttling or the account is out of quota or credit.', 'Wait, or fund the account; the pipeline fails over meanwhile.'],
                    ['Claude Max not first banner', 'Max has been moved or switched off.', 'Fine if you did it on purpose; Restore built-in order puts it back first.'],
                  ].map(([a, b, c]) => (
                    <tr key={a} className="border-t border-admin-border align-top">
                      <td className="px-3 py-2 font-medium text-admin-fg-strong">{a}</td>
                      <td className="px-3 py-2">{b}</td>
                      <td className="px-3 py-2">{c}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Section>

          {/* E — usage and credits */}
          <Section id="costs" title="6. Usage, credits and costs — what every number means">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-2xs uppercase tracking-wider text-admin-fg-muted">
                    <th className="px-3 py-2 text-start">Term</th>
                    <th className="px-3 py-2 text-start">What it is</th>
                    <th className="px-3 py-2 text-start">Is money leaving your account?</th>
                  </tr>
                </thead>
                <tbody>
                  {[
                    ['Claude subscription usage', 'Calls answered by your Claude Max account. Shown as a quota gauge and request counts.', 'No. Label: Subscription — $0 incremental API cost.'],
                    ['Claude API usage', 'Calls answered by the paid Anthropic API, billed per token.', 'Yes, unless covered by promotional credit.'],
                    ['Codex / ChatGPT subscription usage', 'Calls answered by your ChatGPT Business / Codex accounts (the reviewers).', 'No. Same $0 incremental label.'],
                    ['Paid API usage', 'Any pay-per-token call: Claude API, GLM, OpenAI/Gemini live voice.', 'Yes.'],
                    ['Promotional API credits', 'A prepaid grant (for example $200 Anthropic). API usage on that provider draws it down first.', 'Not until the grant is used up.'],
                    ['Internally estimated cost', 'Provider-reported tokens × list price, or live-voice minutes × your rate. Estimates, not invoices.', 'It estimates what would be billed.'],
                    ['Actual paid expense', 'Gross API usage minus promotional credits consumed — still an estimate until you check the provider invoice.', 'This is the real out-of-pocket figure.'],
                  ].map(([a, b, c]) => (
                    <tr key={a} className="border-t border-admin-border align-top">
                      <td className="px-3 py-2 font-medium text-admin-fg-strong">{a}</td>
                      <td className="px-3 py-2">{b}</td>
                      <td className="px-3 py-2">{c}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Callout title="Worked example with the $200 Anthropic promotional credit" tone="ok">
              <p>Start: grant $200, nothing used.</p>
              <p>100 Writing letters are graded through the Claude API at about $0.30 each → <strong>gross API consumption $30</strong>, <strong>credits consumed $30</strong>, <strong>estimated remaining $170</strong>, <strong>out-of-pocket $0</strong>.</p>
              <p>20 more letters are answered by Claude Max → <strong>Subscription — $0 incremental API cost</strong>. They show an API-equivalent value for comparison, but it is not charged and does not reduce the grant.</p>
              <p>Live voice: 500 connected minutes at your rate of $0.12 → <strong>$60</strong>. The grant is Anthropic-only, so this is <strong>out-of-pocket $60</strong> (it appears on the OpenAI invoice).</p>
              <p>Total Writing cost per letter = grading cost + reviewer cost. Total Speaking cost = live voice + grading + reviewer (+ the audio judge).</p>
            </Callout>
            <Callout title="Reading the Cost by stage section" tone="info">
              <p>The headline table shows Today, 7 days, 30 days and all time side by side. Below it, each component lists provider, model, requests, failed attempts, retries, tokens and cost. Totals include every call: failures, retries and paid reviewer fallbacks.</p>
              <p>Live voice audio goes straight from the candidate&rsquo;s browser to OpenAI or Gemini, so the server meters connected minutes. Calibrate the rate once: the provider&rsquo;s live-voice invoice ÷ metered minutes, then save it under <strong>Live voice rate</strong>.</p>
            </Callout>
          </Section>

          {/* Safety */}
          <Section id="safety" title="7. What is protected, and what to do if something looks wrong">
            <ul className="list-disc space-y-1 pl-5 text-sm text-admin-fg-default">
              <li>A grading stage always keeps at least one working step; you cannot save an order that would leave it empty.</li>
              <li>Every save is versioned and audited; History shows who changed what and why. Nothing is ever deleted by Restore.</li>
              <li>Keys are encrypted on the server and never sent back to the browser.</li>
              <li>If a page figure looks wrong, press <strong>Refresh</strong>; cost and usage figures are cached for up to 60 seconds.</li>
              <li>If a candidate reports a failed grade: the submission is saved and retryable. Check <strong>Last served by</strong> and the self-check for the stage.</li>
            </ul>
          </Section>
        </div>
      )}

      {!expanded && (
        <div className="flex flex-wrap gap-2 print:hidden">
          <Badge variant="muted">Five pipelines</Badge>
          <Badge variant="muted">On / off controls</Badge>
          <Badge variant="muted">Priority example</Badge>
          <Badge variant="muted">Keys and benchmarks</Badge>
          <Badge variant="muted">Costs and credits</Badge>
        </div>
      )}
    </div>
  );
}
