import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

function source(relativePath: string) {
  return readFileSync(path.join(process.cwd(), relativePath), 'utf8');
}

function optionalSource(relativePath: string) {
  const absolutePath = path.join(process.cwd(), relativePath);
  return existsSync(absolutePath) ? readFileSync(absolutePath, 'utf8') : '';
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

  it('keeps authenticated workspace providers out of the auth route client graph', () => {
    const coreProviders = source('app/providers.tsx');
    const authenticatedBoundary = source('components/providers/authenticated-notification-center.tsx');
    const workspaceProviders = optionalSource('components/providers/authenticated-workspace-providers.tsx');

    expect(coreProviders).not.toContain('@/components/onboarding/tour-provider');
    expect(coreProviders).not.toContain('@/components/system/LearnerPasteGuard');

    expect(authenticatedBoundary).toContain("import('./authenticated-workspace-providers')");
    expect(authenticatedBoundary).toContain('ssr: false');

    expect(workspaceProviders).toContain('@/contexts/notification-center-context');
    expect(workspaceProviders).toContain('NotificationCenterProvider');
    expect(workspaceProviders).toContain('@/components/onboarding/tour-provider');
    expect(workspaceProviders).toContain('@/components/system/LearnerPasteGuard');
  });

  it('keeps native shell and settings-only startup code behind runtime boundaries', () => {
    const coreProviders = source('app/providers.tsx');
    const mobileGate = source('components/mobile/mobile-runtime-gate.tsx');
    const shellBridges = source('components/shell/runtime-shell-bridges.tsx');
    const versionGate = source('app/providers/AppVersionGateProvider.tsx');
    const accessibility = source('contexts/accessibility-context.tsx');
    const mediaPreferences = source('hooks/use-media-preferences.ts');
    const promoSlider = source('components/domain/catalog/promo-hero-slider.tsx');

    expect(coreProviders).not.toContain('@/components/mobile/mobile-runtime-bridge');
    expect(coreProviders).not.toContain('@/components/shell/ShellControls');
    expect(coreProviders).toContain('@/components/mobile/mobile-runtime-gate');
    expect(coreProviders).toContain('@/components/shell/runtime-shell-bridges');
    expect(mobileGate).toContain("import('./mobile-runtime-bridge')");
    expect(mobileGate).toContain('ssr: false');
    expect(shellBridges).toContain("import('./ShellControls')");
    expect(shellBridges).toContain('ssr: false');
    expect(versionGate).not.toContain("from '@/lib/api'");
    expect(versionGate).toContain("import('@/lib/api')");
    expect(accessibility).not.toContain("from '@/lib/api'");
    expect(accessibility).toContain("import('@/lib/api')");
    expect(mediaPreferences).not.toContain("from '@/lib/api'");
    expect(mediaPreferences).toContain("import('@/lib/api')");
    expect(promoSlider).toContain('renderedSlideIndices.map');
    expect(promoSlider).not.toContain('{SLIDES.map((src, i) => (');
  });
});
