import { defineConfig } from "eslint/config";
import next from "eslint-config-next";
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
}]);
