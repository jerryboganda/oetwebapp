// Entry point for the oet-agent-egress container (CONTRACT.md §6).
// Refuses to start without a readable proxy token of at least 32 characters.
import { ApprovalBroker, createHttpApprovalTransport } from './approvals.js';
import { loadConfig, type EgressConfig } from './config.js';
import { SessionGrants } from './grants.js';
import { createLogger } from './log.js';
import { createEgressServer } from './server.js';

const log = createLogger('egress');

let config: EgressConfig;
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
const broker = new ApprovalBroker(transport, { denyCacheMs: config.denyCacheMs });
const grants = new SessionGrants(config.sessionGrantTtlMs);
const server = createEgressServer({ config, broker, grants, log });

setInterval(() => grants.prune(), 60_000).unref();

server.on('error', (err: Error) => {
  log.event('server_error', { error: err.message });
  process.exit(1);
});

server.listen(config.listenPort, config.listenHost, () => {
  log.event('listening', {
    host: config.listenHost,
    port: config.listenPort,
    connectPorts: [...config.connectPorts],
    httpPorts: [...config.httpPorts],
    approvalUrl: config.approvalUrl,
  });
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
