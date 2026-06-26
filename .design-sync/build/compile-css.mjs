// design-sync: compile the product's Tailwind v4 stylesheet to a static file.
// Run from repo root: node .design-sync/build/compile-css.mjs
import postcss from 'postcss';
import tailwindcss from '@tailwindcss/postcss';
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const input = path.join(root, '.design-sync/build/tw-input.css');
const output = path.join(root, '.design-sync/build/styles.compiled.css');

const css = fs.readFileSync(input, 'utf8');
const result = await postcss([tailwindcss()]).process(css, {
  from: input,
  to: output,
});
fs.writeFileSync(output, result.css);
const kb = (Buffer.byteLength(result.css) / 1024).toFixed(1);
console.log(`[CSS] wrote ${output} (${kb} KiB)`);
