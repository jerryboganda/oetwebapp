const SAMPLE_RATE = 44_100;
const DURATION_SECONDS = 2;
const FREQUENCY = 440;
const AMPLITUDE = 0.18;

function createFallbackListeningWav() {
  const frameCount = SAMPLE_RATE * DURATION_SECONDS;
  const dataSize = frameCount * 2;
  const buffer = new ArrayBuffer(44 + dataSize);
  const view = new DataView(buffer);

  const writeString = (offset: number, value: string) => {
    for (let index = 0; index < value.length; index += 1) {
      view.setUint8(offset + index, value.charCodeAt(index));
    }
  };

  writeString(0, 'RIFF');
  view.setUint32(4, 36 + dataSize, true);
  writeString(8, 'WAVE');
  writeString(12, 'fmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, SAMPLE_RATE, true);
  view.setUint32(28, SAMPLE_RATE * 2, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  writeString(36, 'data');
  view.setUint32(40, dataSize, true);

  for (let frame = 0; frame < frameCount; frame += 1) {
    const time = frame / SAMPLE_RATE;
    const envelope = Math.min(1, time * 8, (DURATION_SECONDS - time) * 8);
    const sample = Math.sin(2 * Math.PI * FREQUENCY * time) * AMPLITUDE * Math.max(0, envelope);
    view.setInt16(44 + frame * 2, sample * 0x7fff, true);
  }

  return buffer;
}

export async function GET(_request: Request, context: { params: Promise<{ asset: string }> }) {
  const { asset } = await context.params;

  if (asset !== 'lt-001.mp3') {
    return new Response('Not found', { status: 404 });
  }

  return new Response(createFallbackListeningWav(), {
    headers: {
      'Content-Type': 'audio/wav',
      'Cache-Control': 'public, max-age=3600',
      'Content-Disposition': 'inline; filename="lt-001.wav"',
    },
  });
}
