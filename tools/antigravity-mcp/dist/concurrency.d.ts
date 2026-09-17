type Slot = {
    release: () => void;
};
export declare class ConcurrencyGate {
    private readonly maxReads;
    private readonly maxWrites;
    private readonly maxWorktrees;
    private activeReads;
    private activeWrites;
    private activeWorktrees;
    private waiters;
    constructor(maxReads?: number, maxWrites?: number, maxWorktrees?: number);
    snapshot(): {
        activeReads: number;
        activeWrites: number;
        activeWorktrees: number;
        waiting: number;
        maxReads: number;
        maxWrites: number;
        maxWorktrees: number;
    };
    acquire(kind: "read" | "write" | "worktree"): Promise<Slot>;
    private canAcquire;
    private bump;
    private flush;
}
export declare const defaultGate: ConcurrencyGate;
export {};
