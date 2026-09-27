import type { AppConfig } from './config.js';
import type { Engine, EngineAdapter, EngineStatus } from './engines/types.js';
import { describeError } from './errors.js';
import type { Logger } from './log.js';
import type { Redactor } from './redact.js';
import { buildAgentEnv } from './env.js';

// Lazy engine adapters. The adapters (src/engines/claude.ts, src/engines/codex.ts)
// are loaded on first use so a broken or signed-out engine never prevents the
// control plane from starting, and so tests can inject fakes.

/** Extra context passed as the adapters' second argument. */
export interface EngineFactoryContext {
  logger: Logger;
  redactor: Redactor;
  /** Allow-listed agent env (src/env.ts), optionally attributed to a session. */
  buildEnv(sessionId?: string): Record<string, string>;
}

export type EngineLoader = (config: AppConfig, context: EngineFactoryContext) => Promise<EngineAdapter>;

interface ClaudeModule {
  createClaudeAdapter(config: AppConfig, context?: EngineFactoryContext): EngineAdapter | Promise<EngineAdapter>;
}
interface CodexModule {
  createCodexAdapter(config: AppConfig, context?: EngineFactoryContext): EngineAdapter | Promise<EngineAdapter>;
}

export const defaultEngineLoaders: Record<Engine, EngineLoader> = {
  claude: async (config, context) => {
    const mod = (await import('./engines/claude.js')) as unknown as ClaudeModule;
    return mod.createClaudeAdapter(config, context);
  },
  codex: async (config, context) => {
    const mod = (await import('./engines/codex.js')) as unknown as CodexModule;
    return mod.createCodexAdapter(config, context);
  },
};

const RETRY_AFTER_FAILURE_MS = 30_000;
const STATUS_TIMEOUT_MS = 8_000;
const STATUS_CACHE_MS = 5_000;

export class EngineUnavailableError extends Error {
  constructor(engine: Engine, cause: string) {
    super(`${engine} engine is unavailable: ${cause}`);
    this.name = 'EngineUnavailableError';
  }
}

export class EngineRegistry {
  private readonly adapters = new Map<Engine, Promise<EngineAdapter>>();
  private readonly failures = new Map<Engine, { at: number; message: string }>();
  private readonly statusCache = new Map<Engine, { at: number; value: EngineStatus }>();
  private readonly context: EngineFactoryContext;

  constructor(
    private readonly config: AppConfig,
    logger: Logger,
    redactor: Redactor,
    private readonly loaders: Record<Engine, EngineLoader> = defaultEngineLoaders,
    private readonly now: () => number = Date.now,
  ) {
    this.context = {
      logger,
      redactor,
      buildEnv: (sessionId?: string) => buildAgentEnv(config, sessionId ? { sessionId } : {}),
    };
  }

  get(engine: Engine): Promise<EngineAdapter> {
    const existing = this.adapters.get(engine);
    if (existing) return existing;
    const failure = this.failures.get(engine);
    if (failure && this.now() - failure.at < RETRY_AFTER_FAILURE_MS) {
      return Promise.reject(new EngineUnavailableError(engine, failure.message));
    }
    const loading = this.loaders[engine](this.config, this.context).catch((error: unknown) => {
      this.adapters.delete(engine);
      const message = describeError(error);
      this.failures.set(engine, { at: this.now(), message });
      this.context.logger.error({ engine, err: message }, 'engine adapter failed to load');
      throw new EngineUnavailableError(engine, message);
    });
    this.adapters.set(engine, loading);
    return loading;
  }

  /** Status that never throws; errors surface as auth.state = "error". */
  async status(engine: Engine, fresh = false): Promise<EngineStatus> {
    const cached = this.statusCache.get(engine);
    if (!fresh && cached && this.now() - cached.at < STATUS_CACHE_MS) return cached.value;
    let value: EngineStatus;
    try {
      const adapter = await this.get(engine);
      value = await withTimeout(adapter.status(), STATUS_TIMEOUT_MS, `${engine} status timed out`);
    } catch (error) {
      value = {
        engine,
        version: null,
        auth: { state: 'error', detail: describeError(error).slice(0, 300) },
        models: [],
        rateLimits: null,
      };
    }
    this.statusCache.set(engine, { at: this.now(), value });
    return value;
  }

  invalidate(engine: Engine): void {
    this.statusCache.delete(engine);
  }

  async shutdown(): Promise<void> {
    const loaded = [...this.adapters.values()];
    this.adapters.clear();
    await Promise.allSettled(loaded.map(async (p) => (await p).shutdown()));
  }
}

export function withTimeout<T>(promise: Promise<T>, ms: number, message: string): Promise<T> {
  let timer: NodeJS.Timeout | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(message)), ms);
    timer.unref();
  });
  return Promise.race([promise, timeout]).finally(() => {
    if (timer) clearTimeout(timer);
  });
}
