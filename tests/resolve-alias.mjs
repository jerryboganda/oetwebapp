import { pathToFileURL, fileURLToPath } from 'node:url';
import { resolve as pathResolve, dirname } from 'node:path';
import { existsSync, statSync } from 'node:fs';

const projectRoot = pathResolve(import.meta.dirname, '..');

function tryExtensions(basePath) {
  if (existsSync(basePath)) {
    if (statSync(basePath).isDirectory()) {
      if (existsSync(pathResolve(basePath, 'index.ts'))) return pathResolve(basePath, 'index.ts');
      if (existsSync(pathResolve(basePath, 'index.tsx'))) return pathResolve(basePath, 'index.tsx');
      if (existsSync(pathResolve(basePath, 'index.js'))) return pathResolve(basePath, 'index.js');
      if (existsSync(basePath + '.ts')) return basePath + '.ts';
    } else {
      return basePath;
    }
  } else {
    if (existsSync(basePath + '.ts')) return basePath + '.ts';
    if (existsSync(basePath + '.tsx')) return basePath + '.tsx';
    if (existsSync(basePath + '.js')) return basePath + '.js';
  }
  return basePath;
}

export async function resolve(specifier, context, nextResolve) {
  if (specifier.startsWith('@/')) {
    const rel = specifier.slice(2);
    const full = tryExtensions(pathResolve(projectRoot, rel));
    return nextResolve(pathToFileURL(full).href, context);
  }

  if ((specifier.startsWith('./') || specifier.startsWith('../')) && context.parentURL) {
    const parentDir = dirname(fileURLToPath(context.parentURL));
    const full = tryExtensions(pathResolve(parentDir, specifier));
    return nextResolve(pathToFileURL(full).href, context);
  }

  return nextResolve(specifier, context);
}
