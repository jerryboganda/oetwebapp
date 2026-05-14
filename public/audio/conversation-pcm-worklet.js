class ConversationPcmWorklet extends AudioWorkletProcessor {
  constructor() {
    super();
    this._targetRate = 16000;
    this._carry = [];
    this._chunkSamples = 1600;
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || input.length === 0 || input[0].length === 0) return true;
    const channels = input.length;
    const source = input[0];
    const ratio = sampleRate / this._targetRate;
    const outputSamples = Math.floor(source.length / ratio);
    for (let index = 0; index < outputSamples; index += 1) {
      const sourceIndex = Math.min(source.length - 1, Math.floor(index * ratio));
      let mixed = 0;
      for (let channel = 0; channel < channels; channel += 1) mixed += input[channel][sourceIndex] || 0;
      mixed /= channels;
      const clamped = Math.max(-1, Math.min(1, mixed));
      this._carry.push(clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff);
    }

    while (this._carry.length >= this._chunkSamples) {
      const samples = this._carry.splice(0, this._chunkSamples);
      const bytes = new ArrayBuffer(samples.length * 2);
      const view = new DataView(bytes);
      for (let index = 0; index < samples.length; index += 1) {
        view.setInt16(index * 2, samples[index], true);
      }
      this.port.postMessage(bytes, [bytes]);
    }

    return true;
  }
}

registerProcessor('conversation-pcm-worklet', ConversationPcmWorklet);