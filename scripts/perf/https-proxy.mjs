import fs from 'node:fs';
import http from 'node:http';
import https from 'node:https';

function readOption(name) {
  const index = process.argv.indexOf(name);
  const value = index >= 0 ? process.argv[index + 1] : undefined;
  if (!value) {
    throw new Error(`Missing required option ${name}.`);
  }
  return value;
}

const listenPort = Number(readOption('--listen'));
const target = new URL(readOption('--target'));
const certificatePath = readOption('--cert');
const keyPath = readOption('--key');

if (target.protocol !== 'http:') {
  throw new Error(`Only HTTP upstreams are supported; received ${target.protocol}.`);
}

function proxyRequest(request, response) {
  const externalHost = request.headers.host ?? `127.0.0.1:${listenPort}`;
  const upstream = http.request({
    hostname: target.hostname,
    port: target.port || 80,
    method: request.method,
    path: new URL(request.url ?? '/', target).pathname + new URL(request.url ?? '/', target).search,
    headers: {
      ...request.headers,
      // Next's request-origin CSRF guard must see the browser-facing HTTPS
      // origin, even though this process forwards to the local HTTP server.
      // Keep the upstream host usable while forwarding the public host/proto
      // explicitly for frameworks that reconstruct request.url from them.
      host: externalHost,
      'x-forwarded-host': externalHost,
      'x-forwarded-proto': 'https',
      'x-forwarded-port': String(listenPort),
    },
  }, (upstreamResponse) => {
    response.writeHead(upstreamResponse.statusCode ?? 502, upstreamResponse.headers);
    upstreamResponse.pipe(response);
  });

  upstream.on('error', (error) => {
    if (!response.headersSent) {
      response.writeHead(502, { 'content-type': 'text/plain; charset=utf-8' });
    }
    response.end(`Upstream proxy error: ${error.message}`);
  });

  request.pipe(upstream);
}

const server = https.createServer({
  cert: fs.readFileSync(certificatePath),
  key: fs.readFileSync(keyPath),
}, proxyRequest);

server.on('clientError', (error, socket) => {
  socket.end(`HTTP/1.1 400 Bad Request\r\n\r\n${error.message}`);
});

server.listen(listenPort, '127.0.0.1', () => {
  console.log(`HTTPS proxy listening on https://127.0.0.1:${listenPort} -> ${target.origin}`);
});
