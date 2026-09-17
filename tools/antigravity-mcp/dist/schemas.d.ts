import * as z from "zod/v4";
export declare const WORKER_ROLES: readonly ["explore", "research", "implement", "test", "debug", "review", "health"];
export type WorkerRole = (typeof WORKER_ROLES)[number];
export declare const workerResultSchema: z.ZodObject<{
    status: z.ZodEnum<{
        SUCCESS: "SUCCESS";
        PARTIAL: "PARTIAL";
        BLOCKED: "BLOCKED";
        ERROR: "ERROR";
    }>;
    role: z.ZodEnum<{
        explore: "explore";
        research: "research";
        implement: "implement";
        test: "test";
        debug: "debug";
        review: "review";
        health: "health";
    }>;
    summary: z.ZodString;
    evidence: z.ZodArray<z.ZodObject<{
        path: z.ZodString;
        lineOrSymbol: z.ZodOptional<z.ZodString>;
        finding: z.ZodString;
    }, z.core.$strip>>;
    filesRead: z.ZodArray<z.ZodString>;
    filesChanged: z.ZodArray<z.ZodString>;
    commandsRun: z.ZodArray<z.ZodObject<{
        command: z.ZodString;
        exitCode: z.ZodNumber;
        result: z.ZodString;
    }, z.core.$strip>>;
    tests: z.ZodArray<z.ZodObject<{
        name: z.ZodString;
        status: z.ZodEnum<{
            PASS: "PASS";
            FAIL: "FAIL";
            NOT_RUN: "NOT_RUN";
        }>;
        evidence: z.ZodString;
    }, z.core.$strip>>;
    risks: z.ZodArray<z.ZodString>;
    blockers: z.ZodArray<z.ZodString>;
    recommendedNextStep: z.ZodString;
    confidence: z.ZodEnum<{
        high: "high";
        medium: "medium";
        low: "low";
    }>;
    worktreePath: z.ZodOptional<z.ZodString>;
}, z.core.$strip>;
export type WorkerResult = z.infer<typeof workerResultSchema>;
export declare function emptyResult(role: WorkerRole, patch: Partial<WorkerResult> & Pick<WorkerResult, "status" | "summary">): WorkerResult;
export declare function unwrapAgyPayload(text: string): unknown | null;
export declare function extractJsonObject(text: string): unknown | null;
export declare function parseWorkerResult(role: WorkerRole, stdout: string): WorkerResult;
