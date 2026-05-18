import { copyFileSync, existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const serverDir = join(process.cwd(), '.next', 'server');
const chunksDir = join(serverDir, 'chunks');

if (!existsSync(chunksDir)) {
  process.exit(0);
}

for (const fileName of readdirSync(chunksDir)) {
  if (fileName.endsWith('.js')) {
    copyFileSync(join(chunksDir, fileName), join(serverDir, fileName));
  }
}
