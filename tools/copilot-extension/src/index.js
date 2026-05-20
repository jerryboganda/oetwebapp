import { createServer } from 'node:http';
import { config } from './config.js';
import { authenticateRequest } from './auth.js';
import { detectIntent, routeIntent } from './handlers.js';

/**
 * Write an SSE chunk in the Copilot Extension protocol format.
 */
function writeSseChunk(res, content) {
  const payload = JSON.stringify({
    choices: [{ index: 0, delta: { content } }],
  });
  res.write(`data: ${payload}\n\n`);
}

/**
 * End the SSE stream.
 */
function writeSseDone(res) {
  res.write('data: [DONE]\n\n');
  res.end();
}

/**
 * Collect the full request body as a string.
 */
function collectBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', (chunk) => chunks.push(chunk));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

/**
 * Handle the main Copilot Extension POST endpoint.
 */
async function handleCopilotRequest(req, res) {
  const rawBody = await collectBody(req);

  // Authenticate
  const authResult = await authenticateRequest(req, rawBody);
  if (authResult.error) {
    res.writeHead(authResult.status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: authResult.error }));
    return;
  }

  const { user } = authResult;
  console.log(`[copilot] Request from ${user.login} (${user.id})`);

  // Parse body
  let body;
  try {
    body = JSON.parse(rawBody);
  } catch {
    res.writeHead(400, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'Invalid JSON body' }));
    return;
  }

  const messages = body.messages || [];
  const lastMessage = messages.filter((m) => m.role === 'user').pop();
  const userContent = lastMessage?.content || '';

  if (!userContent) {
    res.writeHead(400, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'No user message found' }));
    return;
  }

  // Set up SSE response
  res.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
    'X-Accel-Buffering': 'no',
  });

  try {
    // Detect intent and route
    const intent = detectIntent(userContent);
    console.log(`[copilot] Intent: ${intent.handler} | User: ${user.login}`);

    // Send a "thinking" indicator
    writeSseChunk(res, `🔍 Processing your request...\n\n`);

    // Execute the handler
    const result = await routeIntent(intent);

    // Stream the result in chunks for better UX
    const chunkSize = 500;
    for (let i = 0; i < result.length; i += chunkSize) {
      writeSseChunk(res, result.slice(i, i + chunkSize));
    }
  } catch (err) {
    console.error('[copilot] Handler error:', err);
    writeSseChunk(res, `\n\n❌ An error occurred: ${err.message}`);
  }

  writeSseDone(res);
}

/**
 * Handle health check.
 */
function handleHealth(_req, res) {
  res.writeHead(200, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify({ status: 'ok', service: 'oet-copilot-extension', version: '1.0.0' }));
}

/**
 * Main HTTP request router.
 */
const server = createServer(async (req, res) => {
  const url = new URL(req.url, `http://localhost:${config.PORT}`);

  try {
    if (req.method === 'GET' && url.pathname === '/health') {
      handleHealth(req, res);
    } else if (req.method === 'POST' && url.pathname === '/') {
      await handleCopilotRequest(req, res);
    } else {
      res.writeHead(404, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'Not found' }));
    }
  } catch (err) {
    console.error('[server] Unhandled error:', err);
    if (!res.headersSent) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'Internal server error' }));
    }
  }
});

server.listen(config.PORT, () => {
  console.log(`[oet-copilot-extension] Listening on port ${config.PORT}`);
  console.log(`[oet-copilot-extension] Backend API: ${config.API_URL}`);
});
