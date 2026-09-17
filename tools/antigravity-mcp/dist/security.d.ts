export declare const PINNED_MODEL = "gemini-3.8-flash-high";
export declare const PINNED_EFFORT = "high";
export declare function defaultWorkspaceRoot(): string;
export declare function resolveWorkspace(candidate?: string): string;
export declare function assertPathInsideWorkspace(workspace: string, target: string): string;
export declare function parseCommandBin(command: string): string;
export declare function assertAllowlistedCommand(command: string): void;
export declare function isReadOnlyRole(role: string): boolean;
export declare function highAutonomyAllowed(workspace: string): boolean;
export declare function logsDir(): string;
export declare function antigravitySettingsPath(): string;
export declare function readUseG1Credits(): boolean | null;
export declare function ensureUseG1CreditsFalse(): {
    path: string;
    useG1Credits: boolean;
};
export declare function assertNoByokPrimaryRoute(): void;
