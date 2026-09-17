export declare const ERROR_CODES: readonly ["AGY_NOT_INSTALLED", "AUTH_REQUIRED", "MODEL_UNAVAILABLE", "QUOTA_EXHAUSTED", "RATE_LIMITED", "SERVICE_UNAVAILABLE", "PERMISSION_DENIED", "TIMEOUT", "WORKER_TASK_FAILED", "INVALID_OUTPUT", "CANCELED", "INTERNAL_BRIDGE_ERROR"];
export type ErrorCode = (typeof ERROR_CODES)[number];
export declare const FALLBACK_CODES: ReadonlySet<ErrorCode>;
export declare class BridgeError extends Error {
    readonly code: ErrorCode;
    readonly details?: string | undefined;
    constructor(code: ErrorCode, message: string, details?: string | undefined);
}
export declare function classifyAgyFailure(input: {
    exitCode: number | null;
    stdout: string;
    stderr: string;
    timedOut?: boolean;
    signal?: NodeJS.Signals | null;
    agyMissing?: boolean;
}): ErrorCode;
export declare function shouldFallbackToMai(code: ErrorCode): boolean;
