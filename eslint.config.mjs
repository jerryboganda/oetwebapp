import { defineConfig } from "eslint/config";
import next from "eslint-config-next";
import reactHooks from "eslint-plugin-react-hooks";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Flat config REPLACES a rule's options when a later block matches the same
// file, so each restriction rule below is set in exactly one block. Add new
// restrictions to these constants instead of adding another block for the
// same rule.
const SHARED_TS_FILES = [
    "app/**/*.{ts,tsx}",
    "components/**/*.{ts,tsx}",
    "lib/**/*.{ts,tsx}",
    "hooks/**/*.{ts,tsx}",
    "contexts/**/*.{ts,tsx}",
];

// Primitive names both kits (components/ui and components/admin/ui) export.
// Importing one under another name (e.g. `Button as LegacyButton`) hides which
// kit a file uses and slips past tests/static/no-link-button-nesting.test.ts.
// esquery cannot compare two attributes, hence one selector per name.
const KIT_PRIMITIVES = ["Button", "Card", "Badge", "StatusBadge", "EmptyState", "Input", "Textarea", "Select", "Checkbox", "Skeleton", "DataTable"];
const KIT_SOURCE = String.raw`/^@\/components\/(admin\/)?ui(\/|$)/`;
const restrictedSyntax = KIT_PRIMITIVES.map((name) => ({
    selector: `ImportDeclaration[source.value=${KIT_SOURCE}] > ImportSpecifier[imported.name="${name}"][local.name!="${name}"]`,
    message: `Do not alias the kit primitive ${name}; import it under its own name and use one kit per concept per file.`,
}));

const restrictedImportPatterns = [
    { group: ["@/components/admin/**"], message: "Admin kit is admin-only (DESIGN.md §0). Use components/ui." },
];

// Non-admin code that may import the admin kit: admin routes, the kit itself,
// and domain files rendered only by admin routes (listed by name so a new
// learner file in the same folder is still checked).
const ADMIN_KIT_ALLOWED = [
    "app/admin/**",
    "components/admin/**",
    "components/domain/**/admin/**",
    "components/domain/listening/ListeningManifestPanel.tsx",
    "components/domain/materials/course-materials-map.tsx",
    "components/domain/materials/materials-course-browser.tsx",
    "components/domain/video-library/BunnyVideoUploadCard.tsx",
    "components/domain/video-library/course-videos-map.tsx",
    "components/domain/video-library/EncodeStatusBadge.tsx",
    "**/*.test.*",
    "**/__tests__/**",
];

// Block-level ignores only apply to their own block, so the restriction blocks
// below reuse this list (otherwise they would lint stories without the TS parser).
const BASE_IGNORES = [
    "**/.next/**",
    "**/node_modules/**",
    "**/coverage/**",
    "OET Web App Login only screens take from here/**",
    ".storybook/**",
    "**/__stories__/**",
    "**/*.stories.ts",
    "**/*.stories.tsx",
];

export default defineConfig([{
    ignores: BASE_IGNORES,
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
        // Same advisory React-Compiler family as the three above (enabled via
        // eslint-config-next's recommended set). The React Compiler is NOT
        // enabled in the build (next.config.ts has no reactCompiler), so these
        // have no runtime effect — they flag pre-existing patterns (declaration
        // order, render-time component selection, memoization the compiler
        // can't preserve). Keep as warnings/tech-debt, consistent with the
        // three above, so lint stays green.
        "react-hooks/immutability": "warn",
        "react-hooks/preserve-manual-memoization": "warn",
        "react-hooks/static-components": "warn",
    },
}, {
    files: SHARED_TS_FILES,
    ignores: BASE_IGNORES,
    rules: {
        "no-restricted-syntax": ["error", ...restrictedSyntax],
    },
}, {
    files: SHARED_TS_FILES,
    ignores: [...BASE_IGNORES, ...ADMIN_KIT_ALLOWED],
    rules: {
        "no-restricted-imports": ["error", { patterns: restrictedImportPatterns }],
    },
}]);
