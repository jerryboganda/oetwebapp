// JSON-line logger. Every decision the proxy takes is one line on stdout so
// `docker logs oet-agent-egress` is the audit trail (CONTRACT.md §6).
// Never log secrets: callers pass hosts/ports/session ids only.

export type LogFields = Record<string, unknown>;

export interface Logger {
  event(event: string, fields?: LogFields): void;
}

export function createLogger(
  component: string,
  write: (line: string) => void = (line) => {
    process.stdout.write(line);
  },
): Logger {
  return {
    event(event: string, fields: LogFields = {}): void {
      let line: string;
      try {
        line = JSON.stringify({ ts: new Date().toISOString(), component, event, ...fields });
      } catch {
        line = JSON.stringify({ ts: new Date().toISOString(), component, event, logError: 'unserializable fields' });
      }
      write(`${line}\n`);
    },
  };
}
