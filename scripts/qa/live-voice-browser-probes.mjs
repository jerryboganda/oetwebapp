// In-page probes for the live-voice production E2E (speaking-live-voice-browser-e2e.mjs).
// Installed with context.addInitScript(installProbes, provider), so the function must stay
// self-contained: Playwright serialises it with toString(), no closure or import survives.
//
// Records, identically for Gemini Live and OpenAI GPT-Live:
//  - speech spans [start, end] (epoch ms) of the candidate's mic and of the patient's audio
//    as actually audible (WebRTC track for GPT-Live, Web Audio playback for Gemini);
//  - a recording of the patient's audio (webm/opus) for the owner's listening test;
//  - GPT-Live data-channel events and RTCPeerConnection state changes;
//  - Gemini WebSocket close code / reason / wasClean.
export function installProbes(provider) {
  window.__voiceEvents = [];
  window.__audioOnsets = [];
  window.__micStartedAt = null;
  window.__docId = Math.random().toString(36).slice(2);
  window.__micSpans = [];
  window.__patientSpans = [];
  window.__rtcStates = [];
  window.__wsdiag = [];
  window.__geminiWs = false;

  // Speech spans from the audio itself (barge-in, talk-over, silence and latency checks).
  const watchAnalyser = (analyser, list) => {
    const buf = new Float32Array(analyser.fftSize);
    let open = false;
    let lastLoud = 0;
    setInterval(() => {
      analyser.getFloatTimeDomainData(buf);
      const now = Date.now();
      if (Math.sqrt(buf.reduce((s, v) => s + v * v, 0) / buf.length) > 0.01) {
        if (!open) list.push([now, now]);
        open = true;
        lastLoud = now;
        list[list.length - 1][1] = now;
      } else if (open && now - lastLoud > 400) open = false;
    }, 25);
  };
  const spans = (stream, list) => {
    const ctx = new AudioContext();
    const analyser = ctx.createAnalyser();
    ctx.createMediaStreamSource(stream).connect(analyser);
    watchAnalyser(analyser, list);
  };
  const getUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
  navigator.mediaDevices.getUserMedia = async (constraints) => {
    const stream = await getUserMedia(constraints);
    window.__micStartedAt ??= Date.now();
    try { spans(stream, window.__micSpans); } catch { /* best effort */ }
    return stream;
  };

  // One recording per patient audio stream (a card / session each): webm streams from
  // different recorders cannot be concatenated.
  const recordings = [];
  const record = (stream) => {
    try {
      const chunks = [];
      const recorder = new MediaRecorder(stream, { mimeType: 'audio/webm;codecs=opus' });
      recorder.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
      recorder.start(1000);
      recordings.push(chunks);
    } catch { /* recording is best effort */ }
  };
  window.__patientAudios = () => Promise.all(recordings.map(async (chunks) => {
    const bytes = new Uint8Array(await new Blob(chunks).arrayBuffer());
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(binary);
  }));

  // GPT-Live: the patient arrives as a WebRTC remote track.
  const watchRemote = (stream) => {
    record(stream);
    try {
      spans(stream, window.__patientSpans);
      const ctx = new AudioContext();
      const analyser = ctx.createAnalyser();
      ctx.createMediaStreamSource(stream).connect(analyser);
      const buf = new Float32Array(analyser.fftSize);
      let silentSince = Date.now();
      let speaking = false;
      setInterval(() => {
        analyser.getFloatTimeDomainData(buf);
        const rms = Math.sqrt(buf.reduce((s, v) => s + v * v, 0) / buf.length);
        if (rms > 0.01) {
          if (!speaking && Date.now() - silentSince > 500) window.__audioOnsets.push(Date.now());
          speaking = true;
        } else {
          if (speaking) silentSince = Date.now();
          speaking = false;
        }
      }, 25);
    } catch { /* best effort */ }
  };
  const Native = window.RTCPeerConnection;
  window.RTCPeerConnection = class extends Native {
    constructor(...args) {
      super(...args);
      this.addEventListener('track', (e) => watchRemote(e.streams[0] ?? new MediaStream([e.track])));
      this.addEventListener('connectionstatechange', () => window.__rtcStates.push({ at: Date.now(), state: this.connectionState }));
      this.addEventListener('iceconnectionstatechange', () => window.__rtcStates.push({ at: Date.now(), ice: this.iceConnectionState }));
    }
  };
  const createDataChannel = Native.prototype.createDataChannel;
  Native.prototype.createDataChannel = function (...args) {
    const channel = createDataChannel.apply(this, args);
    channel.addEventListener('message', (e) => {
      try {
        const event = { ...JSON.parse(e.data), __at: Date.now() };
        window.__voiceEvents.push(event);
        // session.closed (final billed seconds) lands while the page navigates
        // to the results; keep usage events where the next document can read them.
        if (event.type === 'session.closed' || event.type === 'session.usage.updated') {
          const kept = JSON.parse(localStorage.getItem('__oai_usage') || '[]');
          kept.push({ type: event.type, seconds: event.usage?.seconds ?? null, reason: event.reason ?? null, at: event.__at });
          localStorage.setItem('__oai_usage', JSON.stringify(kept));
        }
      } catch { /* non-JSON */ }
    });
    return channel;
  };

  // Gemini Live: WebSocket close diagnostics, and the patient plays through Web Audio buffer
  // sources (not a track), so tap those sources for spans and the recording. Only while a
  // Gemini socket is open, so page sounds are never mistaken for the patient.
  const NativeWebSocket = window.WebSocket;
  window.WebSocket = class extends NativeWebSocket {
    constructor(...args) {
      super(...args);
      if (!/generativelanguage/.test(String(args[0]))) return;
      window.__geminiWs = true;
      this.addEventListener('close', (e) => {
        window.__wsdiag.push({ at: Date.now(), type: 'close', code: e.code, reason: e.reason, wasClean: e.wasClean });
        window.__geminiWs = false;
      });
      this.addEventListener('error', () => window.__wsdiag.push({ at: Date.now(), type: 'error' }));
    }
  };
  const connect = AudioNode.prototype.connect;
  const taps = new WeakMap();
  AudioNode.prototype.connect = function (destination, ...rest) {
    try {
      if (window.__geminiWs && this instanceof AudioBufferSourceNode && destination instanceof AudioDestinationNode) {
        let tap = taps.get(this.context);
        if (!tap) {
          tap = { analyser: this.context.createAnalyser(), sink: this.context.createMediaStreamDestination() };
          taps.set(this.context, tap);
          watchAnalyser(tap.analyser, window.__patientSpans);
          record(tap.sink.stream);
        }
        connect.call(this, tap.analyser);
        connect.call(this, tap.sink);
      }
    } catch { /* best effort */ }
    return connect.call(this, destination, ...rest);
  };

  if (!provider) return;
  // ?voiceProvider=<p> on the session/exam page selects the provider; add it to the
  // client-side navigation.
  for (const method of ['pushState', 'replaceState']) {
    const original = history[method].bind(history);
    history[method] = (state, title, url) => {
      const target = url == null ? url : String(url);
      const live = target && /\/speaking\/(sessions|exam)\/[^/?]+$/.test(target);
      return original(state, title, live ? `${target}?voiceProvider=${provider}` : target);
    };
  }
}
