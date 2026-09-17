import type { WorkerRole } from "./schemas.js";

export interface StoredSession {
    conversationId: string;
    sessionKey: string;
    role: WorkerRole;
    workspace: string;
    model: string;
    effort: string;
    timestamp: string;
    turns: number;
    lastStatus: string;
}

export interface SessionStore {
    version: number;
    sessions: Record<string, StoredSession>;
}

export interface SessionOptions {
    maxSessions?: number;
    ttlMs?: number;
}

export declare function loadSessionStore(filePath?: string): SessionStore;
export declare function pruneSessions(store: SessionStore, options?: SessionOptions): SessionStore;
export declare function saveSessionStore(store: SessionStore, filePath?: string, options?: SessionOptions): void;
export declare function getSession(sessionKey: string, filePath?: string): StoredSession | null;
export declare function saveSession(sessionData: {
    sessionKey: string;
    conversationId: string;
    role: WorkerRole;
    workspace: string;
    model?: string;
    effort?: string;
    timestamp?: string;
    turns?: number;
    lastStatus?: string;
}, filePath?: string, options?: SessionOptions): StoredSession | null;
export declare function recordSessionFromEnvelope(opts: {
    sessionKey: string;
    envelope: unknown;
    role: WorkerRole;
    workspace: string;
    model?: string;
    effort?: string;
    lastStatus?: string;
}, filePath?: string, options?: SessionOptions): StoredSession | null;
export declare function findResumableConversation(opts: {
    sessionKey: string;
    role: string;
    workspace: string;
}, filePath?: string): string | null;
export declare function listSessions(filePath?: string): StoredSession[];
export declare function clearSessions(sessionKey?: string, filePath?: string): {
    clearedCount: number;
    remainingCount: number;
};
