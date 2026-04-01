const { spawn } = require('child_process');
const net = require('net');

const npmCommand = process.platform === 'win32' ? 'cmd.exe' : 'npm';
const preferredPort = Number(process.env.PORT || 3000);

function npmArgs(args) {
  return process.platform === 'win32' ? ['/c', 'npm', ...args] : args;
}

function isPortAvailable(port) {
  return new Promise((resolve) => {
    const server = net.createServer();

    server.unref();
    server.once('error', () => {
      resolve(false);
    });
    server.once('listening', () => {
      server.close(() => resolve(true));
    });
    server.listen(port);
  });
}

async function findAvailablePort(startPort) {
  for (let port = startPort; port < startPort + 25; port += 1) {
    if (await isPortAvailable(port)) {
      return port;
    }
  }

  throw new Error(`Unable to find an open port starting from ${startPort}`);
}

let rendererProcess = null;
let electronProcess = null;
let shuttingDown = false;

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForRenderer(url) {
  const healthUrl = new URL('/api/health', url).toString();
  for (let attempt = 0; attempt < 120; attempt += 1) {
    try {
      const response = await fetch(healthUrl, { cache: 'no-store' });
      if (response.ok) {
        return;
      }
    } catch {
      // keep waiting
    }

    await wait(1000);
  }

  throw new Error(`Timed out waiting for the renderer at ${healthUrl}`);
}

function shutdown(code = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;

  if (electronProcess && !electronProcess.killed) {
    electronProcess.kill();
  }

  if (rendererProcess && !rendererProcess.killed) {
    rendererProcess.kill();
  }

  process.exit(code);
}

async function main() {
  const rendererPort = await findAvailablePort(preferredPort);
  const rendererUrl = `http://localhost:${rendererPort}`;

  rendererProcess = spawn(npmCommand, npmArgs(['run', 'dev', '--', '--port', String(rendererPort)]), {
    stdio: 'inherit',
    env: {
      ...process.env,
      PORT: String(rendererPort),
    },
    shell: false,
  });

  rendererProcess.on('exit', (code) => {
    if (!shuttingDown) {
      shutdown(typeof code === 'number' ? code : 1);
    }
  });

  await waitForRenderer(rendererUrl);

  electronProcess = spawn(npmCommand, npmArgs(['exec', '--', 'electron', '.']), {
    stdio: 'inherit',
    env: {
      ...process.env,
      ELECTRON_RENDERER_URL: rendererUrl,
    },
    shell: false,
  });

  electronProcess.on('exit', (code) => {
    shutdown(typeof code === 'number' ? code : 0);
  });
}

process.on('SIGINT', () => shutdown(0));
process.on('SIGTERM', () => shutdown(0));

main().catch((error) => {
  console.error('[electron-dev] failed to start desktop shell', error);
  shutdown(1);
});
