// SignalR JSON hub protocol framing (pure; shared by the k6 client and node tests). k6 has no native
// SignalR client, so the load harness speaks the wire protocol itself:
//   https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md
//   https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/TransportProtocols.md
// Every JSON message is terminated by the record separator 0x1E.

export const RS = '\u001e';

export const MessageType = Object.freeze({
  Invocation: 1,
  StreamItem: 2,
  Completion: 3,
  StreamInvocation: 4,
  CancelInvocation: 5,
  Ping: 6,
  Close: 7,
});

export const encodeFrame = (message) => JSON.stringify(message) + RS;

/** First message a client sends after the transport is up. */
export const handshakeFrame = () => encodeFrame({ protocol: 'json', version: 1 });

export const pingFrame = () => encodeFrame({ type: MessageType.Ping });

export function invocationFrame(invocationId, target, args = []) {
  return encodeFrame({
    type: MessageType.Invocation,
    invocationId: String(invocationId),
    target,
    arguments: args,
  });
}

/**
 * Split a transport payload into messages. Returns { frames, invalid }: `frames` are the parsed JSON
 * objects in order, `invalid` counts non-empty pieces that were not valid JSON objects (a truncated
 * or foreign body, for example an HTML error page behind a proxy). Never throws.
 */
export function parseFrames(text) {
  const frames = [];
  let invalid = 0;
  if (typeof text !== 'string' || text.length === 0) return { frames, invalid };
  for (const piece of text.split(RS)) {
    const trimmed = piece.trim();
    if (trimmed === '') continue;
    try {
      const value = JSON.parse(trimmed);
      if (value !== null && typeof value === 'object' && !Array.isArray(value)) frames.push(value);
      else invalid += 1;
    } catch (error) {
      invalid += 1;
    }
  }
  return { frames, invalid };
}

/** `{}` (no `type`) is a successful handshake response; `{error}` is a refused one. */
export function isHandshakeResponse(frame) {
  return frame !== null && typeof frame === 'object' && !('type' in frame);
}

export function classifyFrame(frame) {
  if (isHandshakeResponse(frame)) return typeof frame.error === 'string' ? 'handshake-error' : 'handshake-ok';
  switch (frame.type) {
    case MessageType.Invocation: return 'invocation';
    case MessageType.Completion: return 'completion';
    case MessageType.Ping: return 'ping';
    case MessageType.Close: return 'close';
    case MessageType.StreamItem: return 'stream-item';
    default: return 'other';
  }
}

/** Summarise a batch of frames from one poll / socket message. */
export function summariseFrames(frames) {
  const summary = {
    handshakeOk: false, handshakeError: null, pings: 0, invocations: [], completions: [], closed: false,
    closeError: null, other: 0,
  };
  for (const frame of frames) {
    switch (classifyFrame(frame)) {
      case 'handshake-ok': summary.handshakeOk = true; break;
      case 'handshake-error': summary.handshakeError = frame.error; break;
      case 'ping': summary.pings += 1; break;
      case 'invocation': summary.invocations.push({ target: frame.target, arguments: frame.arguments ?? [] }); break;
      case 'completion': summary.completions.push({ invocationId: frame.invocationId, error: frame.error ?? null, result: frame.result ?? null }); break;
      case 'close': summary.closed = true; summary.closeError = frame.error ?? null; break;
      default: summary.other += 1;
    }
  }
  return summary;
}

/** Parse the negotiate response (negotiateVersion=1). Returns null when it is not usable. */
export function parseNegotiate(body) {
  let value;
  try {
    value = typeof body === 'string' ? JSON.parse(body) : body;
  } catch (error) {
    return null;
  }
  if (value === null || typeof value !== 'object') return null;
  const connectionToken = typeof value.connectionToken === 'string' && value.connectionToken
    ? value.connectionToken
    : (typeof value.connectionId === 'string' ? value.connectionId : null);
  if (!connectionToken) return null;
  const transports = Array.isArray(value.availableTransports)
    ? value.availableTransports.map((t) => String(t?.transport ?? '')).filter(Boolean)
    : [];
  return {
    connectionId: typeof value.connectionId === 'string' ? value.connectionId : connectionToken,
    connectionToken,
    transports,
    supportsLongPolling: transports.length === 0 || transports.includes('LongPolling'),
    supportsWebSockets: transports.includes('WebSockets'),
  };
}

export const negotiateUrl = (hubUrl) => `${hubUrl}/negotiate?negotiateVersion=1`;

export const transportUrl = (hubUrl, connectionToken) => `${hubUrl}?id=${encodeURIComponent(connectionToken)}`;

/** `https://api` -> `wss://api`, `http://api` -> `ws://api`; used for the WebSocket transport. */
export function toWebSocketUrl(url) {
  if (url.startsWith('https://')) return `wss://${url.slice('https://'.length)}`;
  if (url.startsWith('http://')) return `ws://${url.slice('http://'.length)}`;
  return url;
}
