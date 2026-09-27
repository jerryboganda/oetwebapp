// Redaction applied to every event before it is persisted or streamed
// (CONTRACT.md §4) and to command output the control plane relays.
// Two layers: exact known secret values (tokens the sidecar holds), then
// pattern rules for secrets the sidecar cannot know in advance.
// Best-effort by design — it is a seatbelt, not a boundary.

export interface RedactionRule {
  kind: string;
  pattern: RegExp;
  replace: (match: string, ...groups: string[]) => string;
}

const tag = (kind: string): string => `[REDACTED:${kind}]`;

export const REDACTION_RULES: readonly RedactionRule[] = [
  {
    kind: 'private_key',
    pattern: /-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----/g,
    replace: () => tag('private_key'),
  },
  {
    // Credentials embedded in any URL: postgres://user:pass@host, https://x:token@github.com, ...
    kind: 'url_password',
    pattern: /\b([a-z][a-z0-9+.-]*:\/\/)([^\s:@/?#'"]+):([^\s@/?#'"]+)@/gi,
    replace: (_m, scheme, user) => `${scheme}${user}:${tag('password')}@`,
  },
  {
    kind: 'jwt',
    pattern: /\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}/g,
    replace: () => tag('jwt'),
  },
  {
    kind: 'github_pat',
    pattern: /\bgithub_pat_[A-Za-z0-9_]{20,}/g,
    replace: () => tag('github_pat'),
  },
  {
    // ghp_ (classic PAT), gho_ (OAuth), ghu_ (user-to-server), ghs_ (server-to-server/Actions), ghr_ (refresh).
    kind: 'github_token',
    pattern: /\bgh[pousr]_[A-Za-z0-9]{20,}/g,
    replace: () => tag('github_token'),
  },
  {
    // OpenAI / Anthropic style keys: sk-..., sk-proj-..., sk-ant-...
    kind: 'api_key',
    pattern: /(?<![A-Za-z0-9_-])sk-[A-Za-z0-9_-]{16,}/g,
    replace: () => tag('api_key'),
  },
  {
    kind: 'slack_token',
    pattern: /\bxox[a-z]-[A-Za-z0-9-]{10,}/g,
    replace: () => tag('slack_token'),
  },
  {
    kind: 'aws_access_key',
    pattern: /\b(?:AKIA|ASIA)[0-9A-Z]{16}\b/g,
    replace: () => tag('aws_access_key'),
  },
  {
    kind: 'bearer',
    pattern: /\b(Bearer)\s+[A-Za-z0-9\-._~+/]{12,}=*/gi,
    replace: (_m, word) => `${word} ${tag('bearer')}`,
  },
  {
    kind: 'basic_auth',
    pattern: /\b(Basic)\s+[A-Za-z0-9+/]{16,}={0,2}/g,
    replace: (_m, word) => `${word} ${tag('basic')}`,
  },
];

/** Object keys whose string values are always redacted in structured data. */
const SENSITIVE_KEY =
  /^(password|passwd|secret|client_secret|token|access_token|refresh_token|id_token|api_key|apikey|authorization|proxy_authorization|cookie|set-cookie|private_key|agenttoken|shiptoken)$/i;

const MIN_KNOWN_SECRET_LENGTH = 8;
const MAX_DEPTH = 64;

export class Redactor {
  private readonly known = new Set<string>();
  private sortedKnown: string[] = [];

  constructor(initialSecrets: Iterable<string | null | undefined> = []) {
    for (const value of initialSecrets) this.addSecret(value);
  }

  /** Registers an exact secret value (ignored when shorter than 8 chars to avoid mangling text). */
  addSecret(value: string | null | undefined): void {
    if (!value) return;
    const trimmed = value.trim();
    if (trimmed.length < MIN_KNOWN_SECRET_LENGTH || this.known.has(trimmed)) return;
    this.known.add(trimmed);
    // A postgres URL's password is also a secret on its own.
    const urlPassword = /^[a-z][a-z0-9+.-]*:\/\/[^:@/\s]+:([^@/\s]+)@/i.exec(trimmed)?.[1];
    if (urlPassword && urlPassword.length >= MIN_KNOWN_SECRET_LENGTH) {
      this.known.add(urlPassword);
      try {
        const decoded = decodeURIComponent(urlPassword);
        if (decoded.length >= MIN_KNOWN_SECRET_LENGTH) this.known.add(decoded);
      } catch {
        // not URI-encoded; keep the raw value only
      }
    }
    this.sortedKnown = [...this.known].sort((a, b) => b.length - a.length);
  }

  removeSecret(value: string | null | undefined): void {
    if (!value) return;
    if (this.known.delete(value.trim())) {
      this.sortedKnown = [...this.known].sort((a, b) => b.length - a.length);
    }
  }

  get knownCount(): number {
    return this.known.size;
  }

  redact(text: string): string {
    if (!text) return text;
    let out = text;
    for (const secret of this.sortedKnown) {
      if (out.includes(secret)) out = out.split(secret).join(tag('secret'));
    }
    for (const rule of REDACTION_RULES) {
      rule.pattern.lastIndex = 0;
      out = out.replace(rule.pattern, rule.replace);
    }
    return out;
  }

  /** Deep copy of `value` with every string redacted and sensitive keys blanked. */
  redactDeep<T>(value: T): T {
    return this.walk(value, 0) as T;
  }

  private walk(value: unknown, depth: number): unknown {
    if (typeof value === 'string') return this.redact(value);
    if (value === null || typeof value !== 'object') return value;
    if (depth >= MAX_DEPTH) return '[REDACTED:depth]';
    if (Array.isArray(value)) return value.map((item) => this.walk(item, depth + 1));
    if (!isPlainObject(value)) return value;
    const out: Record<string, unknown> = {};
    for (const [key, item] of Object.entries(value)) {
      if (SENSITIVE_KEY.test(key) && typeof item === 'string' && item.length > 0) {
        out[key] = tag('field');
      } else {
        out[key] = this.walk(item, depth + 1);
      }
    }
    return out;
  }
}

function isPlainObject(value: object): value is Record<string, unknown> {
  const proto = Object.getPrototypeOf(value) as unknown;
  return proto === Object.prototype || proto === null;
}
