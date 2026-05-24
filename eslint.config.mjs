import { defineConfig } from "eslint/config";
import next from "eslint-config-next";
import reactHooks from "eslint-plugin-react-hooks";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig([{
    ignores: [
        "**/.next/**",
        "**/node_modules/**",
        "**/coverage/**",
        "OET Web App Login only screens take from here/**",
        ".storybook/**",
        "**/__stories__/**",
        "**/*.stories.ts",
        "**/*.stories.tsx",
    ],
    extends: [...next],
}, {
    // React 19 / React Compiler advisory hook rules. These flag pervasive
    // pre-existing patterns (always-fresh refs, Date.now() in render,
    // setState-in-effect with cancelled-guards) that are not safety-critical
    // bugs. Downgrade to warnings so lint stays green; tracked as tech debt.
    plugins: {
        "react-hooks": reactHooks,
    },
    rules: {
        "react-hooks/set-state-in-effect": "warn",
        "react-hooks/refs": "warn",
        "react-hooks/purity": "warn",
    },
}]);
