// In-memory grant stores (a proxy restart drops them — fail closed).
//
// SessionGrants: an owner "approve for session" on a Docker card covers the
// same grant key (e.g. `exec:oet-postgres`, `read:<co-tenant>`) for that
// session until the TTL expires or the sidecar clears the session
// (DELETE /internal/sessions/:id). Create/delete/prune/network changes are
// never session-grantable (see policy.ts).
//
// TtlSet: exec instances whose create was approved; their start/resize/inspect
// calls are then allowed without a second card.

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

export class TtlSet {
  private readonly entries = new Map<string, number>();
  private readonly now: () => number;

  constructor(
    private readonly ttlMs: number,
    private readonly maxEntries = 4096,
    now?: () => number,
  ) {
    this.now = now ?? Date.now;
  }

  add(key: string): void {
    this.entries.delete(key);
    if (this.entries.size >= this.maxEntries) {
      const oldest = this.entries.keys().next();
      if (!oldest.done) this.entries.delete(oldest.value);
    }
    this.entries.set(key, this.now() + this.ttlMs);
  }

  has(key: string): boolean {
    const until = this.entries.get(key);
    if (until === undefined) return false;
    if (until <= this.now()) {
      this.entries.delete(key);
      return false;
    }
    return true;
  }

  clear(): void {
    this.entries.clear();
  }

  prune(): void {
    const now = this.now();
    for (const [key, until] of this.entries) {
      if (until <= now) this.entries.delete(key);
    }
  }
}
