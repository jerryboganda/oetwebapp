import { readFileSync } from 'node:fs';
import path from 'node:path';

function source(relativePath: string) {
  return readFileSync(path.join(process.cwd(), relativePath), 'utf8');
}

describe('frontend heavy import boundaries', () => {
  it('keeps country flags tree-shakeable and react-select behind the auth lazy boundary', () => {
    const selector = source('components/auth/country-code-select.tsx');
    const lazySelector = source('components/auth/lazy-country-code-select.tsx');
    const consumers = [
      source('components/auth/register/register-personal-step.tsx'),
      source('components/auth/register/register-original-form.tsx'),
    ].join('\n');

    expect(selector).not.toContain('country-flag-icons');
    expect(selector).not.toMatch(/import\s+\*\s+as/);
    expect(lazySelector).toContain("import('./country-code-select')");
    expect(lazySelector).toContain('ssr: false');
    expect(consumers).toContain('lazy-country-code-select');
    expect(consumers).not.toContain("from '@/components/auth/country-code-select'");
  });

  it('loads WaveSurfer and SignalR only when their client effects connect', () => {
    const audioPlayer = source('components/domain/audio-player-waveform.tsx');
    const assistantHook = source('hooks/use-ai-assistant.ts');
    const signalrHelper = source('lib/ai-assistant/signalr.ts');

    expect(audioPlayer).toContain("await import('wavesurfer.js')");
    expect(audioPlayer).not.toMatch(/import\s+WaveSurfer\s+from\s+['"]wavesurfer\.js['"]/);
    expect(assistantHook).toContain("import type { HubConnection } from '@microsoft/signalr'");
    expect(assistantHook).not.toMatch(/import\s+\{[\s\S]*HubConnectionState[\s\S]*\}\s+from\s+['"]@microsoft\/signalr['"]/);
    expect(signalrHelper).toContain("await import('@microsoft/signalr')");
    expect(signalrHelper).not.toMatch(/import\s+\{(?!\s*type\b)[\s\S]*\}\s+from\s+['"]@microsoft\/signalr['"]/);
  });

  it('keeps the progress route and auth surfaces off their former heavy static imports', () => {
    const progressPage = source('app/progress/page.tsx');
    const authFiles = [
      'app/(auth)/mfa/recovery/page.tsx',
      'app/(auth)/register/success/page.tsx',
      'app/(auth)/reset-password/success/page.tsx',
      'components/auth/auth-mode-switch.tsx',
      'components/auth/mfa-challenge-form.tsx',
      'components/auth/mfa-setup-card.tsx',
      'components/auth/register-form.tsx',
      'components/auth/register/register-original-form.tsx',
      'components/auth/register/register-security-step.tsx',
      'components/auth/sign-in-form.tsx',
      'components/auth/themed-password-input.tsx',
    ].map(source).join('\n');

    expect(progressPage).toContain("@/components/charts/dynamic-recharts");
    expect(progressPage).not.toMatch(/from\s+['"]recharts['"]/);
    expect(authFiles).not.toContain('@tabler/icons-react');
  });
});
