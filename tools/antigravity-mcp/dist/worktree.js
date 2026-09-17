import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { WORKTREE_CONFIG } from "./config.js";

/**
 * Check if the workspace is inside a git repository.
 * Returns the top-level repo path string, or null if not a git repository.
 */
export function getRepoRoot(workspace, timeoutMs = WORKTREE_CONFIG.gitTimeoutMs) {
    try {
        const out = execFileSync("git", ["-C", workspace, "rev-parse", "--show-toplevel"], {
            timeout: timeoutMs,
            stdio: ["ignore", "pipe", "pipe"],
            windowsHide: true,
            encoding: "utf8",
        });
        const trimmed = out.trim();
        return trimmed.length > 0 ? path.resolve(trimmed) : null;
    } catch {
        return null;
    }
}

/**
 * Returns true if workspace is inside a git repository.
 */
export function isGitRepo(workspace, timeoutMs = WORKTREE_CONFIG.gitTimeoutMs) {
    return Boolean(getRepoRoot(workspace, timeoutMs));
}

/**
 * Creates an isolated git worktree for a worker run.
 * If workspace is not inside a git repository, returns 'not-a-git-repo'.
 * If successful, returns the absolute path to the newly created worktree.
 */
export function createWorktree(workspace, runId, options = {}) {
    const timeoutMs = options.timeoutMs ?? WORKTREE_CONFIG.gitTimeoutMs;
    const repoRoot = getRepoRoot(workspace, timeoutMs);
    if (!repoRoot) {
        return "not-a-git-repo";
    }

    const worktreeRoot = options.root ?? WORKTREE_CONFIG.root;
    fs.mkdirSync(worktreeRoot, { recursive: true });

    const rawId = runId ? String(runId) : `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const safeId = rawId.replace(/[^a-zA-Z0-9._-]/g, "-");
    const branchName = `agy-wt-${safeId}`;
    const worktreePath = path.resolve(worktreeRoot, branchName);

    // If an orphaned directory or worktree with this path exists, clean it up safely first
    if (fs.existsSync(worktreePath)) {
        removeWorktree(worktreePath, repoRoot, { timeoutMs });
    }

    // Ensure stale branch with this name is deleted if left over from an aborted run
    try {
        execFileSync("git", ["-C", repoRoot, "branch", "-D", branchName], {
            timeout: timeoutMs,
            stdio: ["ignore", "pipe", "pipe"],
            windowsHide: true,
        });
    } catch {
        /* ignore branch not found */
    }

    // Create the worktree on a new branch from HEAD
    execFileSync("git", ["-C", repoRoot, "worktree", "add", "-b", branchName, worktreePath, "HEAD"], {
        timeout: timeoutMs,
        stdio: ["ignore", "pipe", "pipe"],
        windowsHide: true,
    });

    return path.resolve(worktreePath);
}

/**
 * Removes an isolated git worktree and prunes git worktree metadata.
 * Safe to call multiple times (idempotent).
 */
export function removeWorktree(worktreePath, workspace, options = {}) {
    const timeoutMs = options.timeoutMs ?? WORKTREE_CONFIG.gitTimeoutMs;
    const absWorktree = path.resolve(worktreePath);
    const absWorkspace = workspace ? path.resolve(workspace) : null;

    if (absWorkspace) {
        try {
            execFileSync("git", ["-C", absWorkspace, "worktree", "remove", "--force", absWorktree], {
                timeout: timeoutMs,
                stdio: ["ignore", "pipe", "pipe"],
                windowsHide: true,
            });
        } catch {
            /* safe if already removed or not a working tree */
        }

        try {
            execFileSync("git", ["-C", absWorkspace, "worktree", "prune"], {
                timeout: timeoutMs,
                stdio: ["ignore", "pipe", "pipe"],
                windowsHide: true,
            });
        } catch {
            /* safe to ignore */
        }

        const baseName = path.basename(absWorktree);
        if (baseName.startsWith("agy-wt-")) {
            try {
                execFileSync("git", ["-C", absWorkspace, "branch", "-D", baseName], {
                    timeout: timeoutMs,
                    stdio: ["ignore", "pipe", "pipe"],
                    windowsHide: true,
                });
            } catch {
                /* safe if branch does not exist */
            }
        }
    }

    // Ensure the filesystem directory is completely deleted
    try {
        if (fs.existsSync(absWorktree)) {
            fs.rmSync(absWorktree, { recursive: true, force: true, maxRetries: 3, retryDelay: 200 });
        }
    } catch {
        /* ignore removal failures on locked files */
    }
}

/**
 * Cleans up worktrees under root whose age exceeds maxAgeMs.
 * Returns array of cleaned worktree summaries.
 */
export function cleanupStaleWorktrees(options = {}) {
    const root = options.root ?? WORKTREE_CONFIG.root;
    const maxAgeMs = options.maxAgeMs ?? WORKTREE_CONFIG.maxAgeMs;
    const timeoutMs = options.timeoutMs ?? WORKTREE_CONFIG.gitTimeoutMs;
    const cleaned = [];

    if (!fs.existsSync(root)) {
        return cleaned;
    }

    let entries = [];
    try {
        entries = fs.readdirSync(root, { withFileTypes: true });
    } catch {
        return cleaned;
    }

    const now = Date.now();
    for (const entry of entries) {
        if (!entry.isDirectory()) continue;
        const entryPath = path.join(root, entry.name);
        try {
            const stat = fs.statSync(entryPath);
            const ageMs = now - stat.mtimeMs;
            if (ageMs >= maxAgeMs) {
                let repo = options.workspace ?? null;
                const gitFile = path.join(entryPath, ".git");
                if (!repo && fs.existsSync(gitFile)) {
                    try {
                        const content = fs.readFileSync(gitFile, "utf8");
                        const m = content.match(/gitdir:\s*(.+)/i);
                        if (m?.[1]) {
                            const gitdir = m[1].trim();
                            const candidate = path.resolve(gitdir, "..", "..");
                            if (fs.existsSync(candidate)) {
                                repo = candidate;
                            }
                        }
                    } catch {
                        /* ignore parse errors */
                    }
                }

                removeWorktree(entryPath, repo, { timeoutMs });
                cleaned.push({ path: entryPath, ageMs });
            }
        } catch {
            /* ignore individual stat/cleanup errors */
        }
    }

    if (options.workspace) {
        try {
            execFileSync("git", ["-C", options.workspace, "worktree", "prune"], {
                timeout: timeoutMs,
                stdio: ["ignore", "pipe", "pipe"],
                windowsHide: true,
            });
        } catch {
            /* ignore prune failure */
        }
    }

    return cleaned;
}
