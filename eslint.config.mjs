import { dirname } from "path";
import { fileURLToPath } from "url";
import { FlatCompat } from "@eslint/eslintrc";
import { globalIgnores } from "eslint/config";
import prettierPlugin from "eslint-plugin-prettier";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const compat = new FlatCompat({
  baseDirectory: __dirname,
});

const eslintConfig = [
  globalIgnores([".next/**", "out/**", "build/**", "next-env.d.ts"]),
  ...compat.extends("next", "next/core-web-vitals", "prettier"),

  {
    plugins: {
      prettier: prettierPlugin,
    },
    rules: {
      "@next/next/no-img-element": "off",
      "prettier/prettier": ["error", { endOfLine: "auto" }],
      // "react/no-unescaped-entities": "off",
    },
  },
];

export default eslintConfig;
