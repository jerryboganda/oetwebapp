import assert from 'node:assert/strict';
import test from 'node:test';
import {
  RS, classifyFrame, handshakeFrame, invocationFrame, isHandshakeResponse, negotiateUrl, parseFrames,
  parseNegotiate, pingFrame, summariseFrames, toWebSocketUrl, transportUrl,
} from './signalr-frames.mjs';

test('frames are JSON terminated by the 0x1E record separator', () => {
  assert.equal(handshakeFrame(), `{"protocol":"json","version":1}${RS}`);
  assert.equal(pingFrame(), `{"type":6}${RS}`);
  assert.equal(
    invocationFrame(3, 'JoinRoom', ['lvrm_1']),
    `{"type":1,"invocationId":"3","target":"JoinRoom","arguments":["lvrm_1"]}${RS}`,
  );
});

test('parseFrames splits a poll body that carries several messages', () => {
  const body = `{}${RS}{"type":6}${RS}{"type":1,"target":"ReceiveNotification","arguments":[{"id":"n1"}]}${RS}`;
  const { frames, invalid } = parseFrames(body);
  assert.equal(invalid, 0);
  assert.equal(frames.length, 3);
  assert.deepEqual(frames.map(classifyFrame), ['handshake-ok', 'ping', 'invocation']);
});

test('parseFrames tolerates empty, truncated and foreign bodies without throwing', () => {
  assert.deepEqual(parseFrames(''), { frames: [], invalid: 0 });
  assert.deepEqual(parseFrames(undefined), { frames: [], invalid: 0 });
  assert.equal(parseFrames('<html>502 Bad Gateway</html>').invalid, 1);
  assert.equal(parseFrames(`{"type":6}${RS}{"type":1,"tar`).invalid, 1);
  assert.equal(parseFrames(`[1,2]${RS}`).invalid, 1);
});

test('handshake responses have no type; an error handshake is refused', () => {
  assert.equal(isHandshakeResponse({}), true);
  assert.equal(isHandshakeResponse({ type: 6 }), false);
  assert.equal(classifyFrame({ error: 'Requested protocol is not supported' }), 'handshake-error');
});

test('summariseFrames reports completions, pings, invocations and close', () => {
  const { frames } = parseFrames(
    `{}${RS}{"type":6}${RS}{"type":6}${RS}`
    + `{"type":3,"invocationId":"1","result":null}${RS}`
    + `{"type":3,"invocationId":"2","error":"forbidden"}${RS}`
    + `{"type":1,"target":"CueRaised","arguments":[{"cueIndex":"2"}]}${RS}`
    + `{"type":7,"error":"bye"}${RS}`,
  );
  const s = summariseFrames(frames);
  assert.equal(s.handshakeOk, true);
  assert.equal(s.pings, 2);
  assert.deepEqual(s.completions, [
    { invocationId: '1', error: null, result: null },
    { invocationId: '2', error: 'forbidden', result: null },
  ]);
  assert.equal(s.invocations[0].target, 'CueRaised');
  assert.equal(s.closed, true);
  assert.equal(s.closeError, 'bye');
});

test('parseNegotiate reads negotiateVersion 1 and falls back to the connection id', () => {
  const v1 = parseNegotiate(JSON.stringify({
    negotiateVersion: 1,
    connectionId: 'abc',
    connectionToken: 'tok-1',
    availableTransports: [{ transport: 'WebSockets' }, { transport: 'LongPolling' }],
  }));
  assert.deepEqual(v1, {
    connectionId: 'abc', connectionToken: 'tok-1', transports: ['WebSockets', 'LongPolling'],
    supportsLongPolling: true, supportsWebSockets: true,
  });
  const v0 = parseNegotiate({ connectionId: 'only-id', availableTransports: [{ transport: 'LongPolling' }] });
  assert.equal(v0.connectionToken, 'only-id');
  assert.equal(v0.supportsWebSockets, false);
  assert.equal(parseNegotiate('not json'), null);
  assert.equal(parseNegotiate('{}'), null);
  assert.equal(parseNegotiate('null'), null);
});

test('url helpers', () => {
  assert.equal(negotiateUrl('https://app/api/backend/v1/notifications/hub'), 'https://app/api/backend/v1/notifications/hub/negotiate?negotiateVersion=1');
  assert.equal(transportUrl('https://h', 'a b/c'), 'https://h?id=a%20b%2Fc');
  assert.equal(toWebSocketUrl('https://api.example'), 'wss://api.example');
  assert.equal(toWebSocketUrl('http://localhost:5198'), 'ws://localhost:5198');
});
