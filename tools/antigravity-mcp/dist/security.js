import fs from "node:fs";
import os from "node:os";
import path from "node:path";
export const PINNED_MODEL = "gemini-3.8-flash-high";
export const PINNED_EFFORT = "high";
const ALLOWED_BINS = new Set([
    "git",
    "pnpm",
    "npm",
    "npx",
    "node",
    "next",
    "vitest",
    "eslint",
    "playwright",
    "docker",
    "dotnet",
    "python",
    "python3",
    "py",
    "pytest",
    "uv",
    "agy",
]);
const BLOCKED_SUBSTRINGS = [
    "rm -rf",
    "git reset --hard",
    "git push --force",
    "drop database",
    "docker compose down -v",
    "docker volume rm",
];
export function defaultWorkspaceRoot() {
    return process.env.AGY_WORKSPACE_ROOT?.trim() || process.cwd();
}
export function resolveWorkspace(candidate) {
    const root = path.resolve(candidate?.trim() || defaultWorkspaceRoot());
    if (!fs.existsSync(root)) {
        throw new Error(`Workspace does not exist: ${root}`);
    }
    return fs.realpathSync.native ? fs.realpathSync.native(root) : fs.realpathSync(root);
}
export function assertPathInsideWorkspace(workspace, target) {
    const resolved = path.resolve(workspace, target);
    const rel = path.relative(workspace, resolved);
    if (rel.startsWith("..") || path.isAbsolute(rel)) {
        throw new Error(`Path escapes workspace: ${target}`);
    }
    return resolved;
}
export function parseCommandBin(command) {
    const trimmed = command.trim();
    if (!trimmed)
        throw new Error("Empty command");
    const token = trimmed.split(/\s+/)[0] ?? "";
    const base = path.basename(token).replace(/\.exe$/i, "").toLowerCase();
    return base;
}
export function assertAllowlistedCommand(command) {
    const lower = command.toLowerCase();
    for (const blocked of BLOCKED_SUBSTRINGS) {
        if (lower.includes(blocked)) {
            throw new Error(`Blocked command pattern: ${blocked}`);
        }
    }
    const bin = parseCommandBin(command);
    if (!ALLOWED_BINS.has(bin)) {
        throw new Error(`Command not on allowlist: ${bin}`);
    }
}
export function isReadOnlyRole(role) {
    return role === "explore" || role === "research" || role === "review" || role === "health";
}
export function highAutonomyAllowed(workspace) {
    if (process.env.AGY_ALLOW_HIGH_AUTONOMY === "1")
        return true;
    const marker = path.join(workspace, ".agy-disposable");
    return fs.existsSync(marker);
}
export function logsDir() {
    const dir = process.env.AGY_LOG_DIR?.trim() ||
        path.join(defaultWorkspaceRoot(), ".tools-state", "antigravity-mcp");
    fs.mkdirSync(dir, { recursive: true });
    return dir;
}
export function antigravitySettingsPath() {
    return path.join(os.homedir(), ".gemini", "antigravity-cli", "settings.json");
}
export function readUseG1Credits() {
    const p = antigravitySettingsPath();
    if (!fs.existsSync(p))
        return null;
    try {
        const parsed = JSON.parse(fs.readFileSync(p, "utf8"));
        return typeof parsed.useG1Credits === "boolean" ? parsed.useG1Credits : null;
    }
    catch {
        return null;
    }
}
export function ensureUseG1CreditsFalse() {
    const p = antigravitySettingsPath();
    fs.mkdirSync(path.dirname(p), { recursive: true });
    let current = {};
    if (fs.existsSync(p)) {
        try {
            current = JSON.parse(fs.readFileSync(p, "utf8"));
        }
        catch {
            current = {};
        }
    }
    if (current.useG1Credits !== false) {
        current.useG1Credits = false;
        fs.writeFileSync(p, `${JSON.stringify(current, null, 2)}\n`, "utf8");
    }
    return { path: p, useG1Credits: false };
}
export function assertNoByokPrimaryRoute() {
    if (process.env.AGY_ALLOW_BYOK === "1")
        return;
    const provider = process.env.ANTIGRAVITY_MODEL_PROVIDER?.toLowerCase();
    if (provider && provider !== "account" && provider !== "google") {
        throw new Error("BYOK / non-account modelProvider is forbidden for this bridge");
    }
}
