export declare function getRepoRoot(workspace: string, timeoutMs?: number): string | null;
export declare function isGitRepo(workspace: string, timeoutMs?: number): boolean;
export declare function createWorktree(workspace: string, runId?: string, options?: {
    root?: string;
    timeoutMs?: number;
}): string | "not-a-git-repo";
export declare function removeWorktree(worktreePath: string, workspace?: string | null, options?: {
    timeoutMs?: number;
}): void;
export declare function cleanupStaleWorktrees(options?: {
    root?: string;
    maxAgeMs?: number;
    workspace?: string;
    timeoutMs?: number;
}): Array<{ path: string; ageMs: number }>;
