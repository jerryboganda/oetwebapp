const frontendBaseUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';
const apiBaseUrl = process.env.PLAYWRIGHT_API_URL ?? 'http://localhost:5198';

async function check(url, expectation, options = {}) {
  const { readBody = true } = options;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 30_000);

  try {
    const response = await fetch(url, {
      signal: controller.signal,
      headers: {
        Accept: 'application/json, text/html;q=0.9, */*;q=0.8',
      },
    });

    if (!response.ok) {
      throw new Error(`responded with ${response.status}`);
    }

    const body = readBody ? await readResponseSnippet(response) : '';
    if (!expectation(body)) {
      throw new Error('returned an unexpected payload');
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${url} is not ready: ${message}`);
  } finally {
    clearTimeout(timeout);
  }
}

async function readResponseSnippet(response) {
  const reader = response.body?.getReader();
  if (!reader) {
    return response.text();
  }

  const decoder = new TextDecoder();
  const chunks = [];
  let totalLength = 0;

  try {
    while (totalLength < 64_000) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      if (!value?.length) {
        continue;
      }

      chunks.push(decoder.decode(value, { stream: true }));
      totalLength += value.length;

      if (chunks.join('').length >= 32_000) {
        break;
      }
    }
  } finally {
    await reader.cancel().catch(() => {});
  }

  return chunks.join('');
}

try {
  await check(`${frontendBaseUrl}/sign-in`, () => true, { readBody: false });
  await check(`${apiBaseUrl}/health/ready`, (body) => /"status":"ok"/i.test(body));
  console.log(`Playwright readiness check passed for ${frontendBaseUrl} and ${apiBaseUrl}.`);
} catch (error) {
  const message = error instanceof Error ? error.message : String(error);
  console.error('Playwright readiness check failed.');
  console.error(message);
  console.error('Start the local learner stack before running E2E tests.');
  process.exit(1);
}
