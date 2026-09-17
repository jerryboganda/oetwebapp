export const ERROR_CODES = [
    "AGY_NOT_INSTALLED",
    "AUTH_REQUIRED",
    "MODEL_UNAVAILABLE",
    "QUOTA_EXHAUSTED",
    "RATE_LIMITED",
    "SERVICE_UNAVAILABLE",
    "PERMISSION_DENIED",
    "TIMEOUT",
    "WORKER_TASK_FAILED",
    "INVALID_OUTPUT",
    "CANCELED",
    "INTERNAL_BRIDGE_ERROR",
];
export const FALLBACK_CODES = new Set([
    "QUOTA_EXHAUSTED",
    "RATE_LIMITED",
    "SERVICE_UNAVAILABLE",
    "MODEL_UNAVAILABLE",
    "TIMEOUT",
]);
export class BridgeError extends Error {
    code;
    details;
    constructor(code, message, details) {
        super(message);
        this.code = code;
        this.details = details;
        this.name = "BridgeError";
    }
}
export function classifyAgyFailure(input) {
    if (input.agyMissing)
        return "AGY_NOT_INSTALLED";
    if (input.timedOut)
        return "TIMEOUT";
    if (input.signal === "SIGTERM" || input.signal === "SIGINT")
        return "CANCELED";
    const blob = `${input.stdout}\n${input.stderr}`.toLowerCase();
    if (/not logged in|login required|auth(entication)? required|unauthenticated|sign[- ]?in|oauth/.test(blob)) {
        return "AUTH_REQUIRED";
    }
    if (/quota|billing|usage limit|resource exhausted|exceeded.*quota/.test(blob)) {
        return "QUOTA_EXHAUSTED";
    }
    if (/rate limit|too many requests|429/.test(blob))
        return "RATE_LIMITED";
    if (/model (not found|unavailable|not available)|unknown model/.test(blob)) {
        return "MODEL_UNAVAILABLE";
    }
    if (/permission denied|access denied|not allowed|sandbox/.test(blob)) {
        return "PERMISSION_DENIED";
    }
    if (/service unavailable|503|502|bad gateway|temporarily unavailable/.test(blob)) {
        return "SERVICE_UNAVAILABLE";
    }
    if (/timed? ?out/.test(blob))
        return "TIMEOUT";
    if (input.exitCode === 127)
        return "AGY_NOT_INSTALLED";
    if (input.exitCode === 0)
        return "INVALID_OUTPUT";
    return "WORKER_TASK_FAILED";
}
export function shouldFallbackToMai(code) {
    return FALLBACK_CODES.has(code);
}
