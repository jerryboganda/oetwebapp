import { render } from '@testing-library/react';
import { RuntimeDiagnostics } from '@/components/mobile/runtime-diagnostics';

// The overlay is a native-shell-only instrument. On plain web (jsdom's
// default: no desktopBridge, Capacitor.isNativePlatform() === false) it must
// render nothing — it must never reach learners on browsers or desktop.
describe('RuntimeDiagnostics', () => {
  it('renders nothing outside a native shell', () => {
    const { container } = render(<RuntimeDiagnostics />);
    expect(container).toBeEmptyDOMElement();
  });
});
