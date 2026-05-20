import { CONFIG } from './_lib.mjs';
console.log(JSON.stringify({
  el: !!CONFIG.elevenlabs.apiKey,
  elPrefix: (CONFIG.elevenlabs.apiKey || '').slice(0, 6),
  model: CONFIG.elevenlabs.model,
  api: CONFIG.apiBase,
  admin: CONFIG.adminEmail,
  aiKey: !!CONFIG.ai.apiKey,
}, null, 2));
