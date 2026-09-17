export class ConcurrencyGate {
    maxReads;
    maxWrites;
    maxWorktrees;
    activeReads = 0;
    activeWrites = 0;
    activeWorktrees = 0;
    waiters = [];
    constructor(maxReads = 4, maxWrites = 1, maxWorktrees = 2) {
        this.maxReads = maxReads;
        this.maxWrites = maxWrites;
        this.maxWorktrees = maxWorktrees;
    }
    snapshot() {
        return {
            activeReads: this.activeReads,
            activeWrites: this.activeWrites,
            activeWorktrees: this.activeWorktrees,
            waiting: this.waiters.length,
            maxReads: this.maxReads,
            maxWrites: this.maxWrites,
            maxWorktrees: this.maxWorktrees,
        };
    }
    async acquire(kind) {
        for (;;) {
            if (this.canAcquire(kind)) {
                this.bump(kind, 1);
                let released = false;
                return {
                    release: () => {
                        if (released)
                            return;
                        released = true;
                        this.bump(kind, -1);
                        this.flush();
                    },
                };
            }
            await new Promise((resolve) => this.waiters.push(resolve));
        }
    }
    canAcquire(kind) {
        if (kind === "read")
            return this.activeReads < this.maxReads && this.activeWrites === 0;
        if (kind === "write") {
            return this.activeWrites < this.maxWrites && this.activeReads === 0 && this.activeWorktrees === 0;
        }
        return this.activeWorktrees < this.maxWorktrees && this.activeWrites === 0;
    }
    bump(kind, delta) {
        if (kind === "read")
            this.activeReads += delta;
        else if (kind === "write")
            this.activeWrites += delta;
        else
            this.activeWorktrees += delta;
    }
    flush() {
        const pending = this.waiters.splice(0);
        for (const waiter of pending)
            waiter();
    }
}
export const defaultGate = new ConcurrencyGate();
