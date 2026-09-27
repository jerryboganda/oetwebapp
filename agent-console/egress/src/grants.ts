// Per-session dynamic allowlist: an owner "approve for session" on an egress
// card adds host:port for that session (CONTRACT.md §6). Grants live in memory
// only (a proxy restart drops them — fail closed), expire after a TTL, and are
// cleared explicitly when the sidecar ends a session (DELETE /internal/sessions/:id).

export class SessionGrants {
  private readonly bySession = new Map<string, Map<string, number>>();
  private readonly now: () => number;

  constructor(
    private readonly ttlMs: number,
    private readonly maxPerSession = 256,
    now?: () => number,
  ) {
    this.now = now ?? Date.now;
  }

  add(sessionId: string, key: string): void {
    let grants = this.bySession.get(sessionId);
    if (!grants) {
      grants = new Map();
      this.bySession.set(sessionId, grants);
    }
    grants.delete(key);
    if (grants.size >= this.maxPerSession) {
      const oldest = grants.keys().next();
      if (!oldest.done) grants.delete(oldest.value);
    }
    grants.set(key, this.now() + this.ttlMs);
  }

  has(sessionId: string, key: string): boolean {
    const grants = this.bySession.get(sessionId);
    const until = grants?.get(key);
    if (grants === undefined || until === undefined) return false;
    if (until <= this.now()) {
      grants.delete(key);
      if (grants.size === 0) this.bySession.delete(sessionId);
      return false;
    }
    return true;
  }

  clearSession(sessionId: string): number {
    const count = this.bySession.get(sessionId)?.size ?? 0;
    this.bySession.delete(sessionId);
    return count;
  }

  clearAll(): number {
    let count = 0;
    for (const grants of this.bySession.values()) count += grants.size;
    this.bySession.clear();
    return count;
  }

  prune(): void {
    const now = this.now();
    for (const [sessionId, grants] of this.bySession) {
      for (const [key, until] of grants) {
        if (until <= now) grants.delete(key);
      }
      if (grants.size === 0) this.bySession.delete(sessionId);
    }
  }
}
