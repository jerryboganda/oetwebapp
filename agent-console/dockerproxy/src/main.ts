// Entry point for the oet-agent-dockerproxy container (CONTRACT.md §6).
// Refuses to start without a readable proxy token of at least 32 characters.
import { ApprovalBroker, createHttpApprovalTransport } from './approvals.js';
import { loadConfig, type DockerProxyConfig } from './config.js';
import { SessionGrants, TtlSet } from './grants.js';
import { createLogger } from './log.js';
import { createDockerProxyServer } from './server.js';
import { DockerUpstream } from './upstream.js';

const log = createLogger('dockerproxy');

let config: DockerProxyConfig;
try {
  config = loadConfig();
} catch (err) {
  log.event('startup_failed', { error: err instanceof Error ? err.message : String(err) });
  process.exit(1);
}

const transport = createHttpApprovalTransport({
  url: config.approvalUrl,
  token: config.proxyToken,
  timeoutMs: config.approvalTimeoutMs,
});
// Docker approvals are never coalesced or deny-cached: each operation gets its own card.
const broker = new ApprovalBroker(transport, { denyCacheMs: 0 });
const grants = new SessionGrants(config.sessionGrantTtlMs);
const execApprovals = new TtlSet(config.execApprovalTtlMs);
const upstream = new DockerUpstream(config.socketPath);
const server = createDockerProxyServer({ config, broker, grants, execApprovals, upstream, log });

setInterval(() => {
  grants.prune();
  execApprovals.prune();
}, 60_000).unref();

server.on('error', (err: Error) => {
  log.event('server_error', { error: err.message });
  process.exit(1);
});

server.listen(config.listenPort, config.listenHost, () => {
  log.event('listening', { host: config.listenHost, port: config.listenPort, socketPath: config.socketPath, approvalUrl: config.approvalUrl });
});

function shutdown(signal: string): void {
  log.event('shutdown', { signal });
  server.close();
  server.closeAllConnections();
  setTimeout(() => process.exit(0), 2_000).unref();
}

process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('SIGINT', () => shutdown('SIGINT'));
process.on('uncaughtException', (err: Error) => {
  log.event('uncaught_exception', { error: err.message });
  process.exit(1);
});
