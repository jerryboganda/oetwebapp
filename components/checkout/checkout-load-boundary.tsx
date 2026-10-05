'use client';

import { Component, type ReactNode } from 'react';

type CheckoutLoadBoundaryProps = {
  /** The embedded checkout could not be shown; the caller switches to its hosted/redirect flow. */
  onFailed?: () => void;
  children: ReactNode;
};

/**
 * Guards a lazily loaded payment component. Its code is fetched at the moment it is about to be
 * shown, so a failed download (a stale tab after a deploy, a flaky network) would otherwise reach
 * the nearest error boundary and leave the payer on an error page, with the component's own
 * "unavailable" fallback never running because the component never mounted. Here a failure to
 * load (or to render) calls `onFailed` once and renders nothing.
 */
export class CheckoutLoadBoundary extends Component<CheckoutLoadBoundaryProps, { failed: boolean }> {
  state = { failed: false };

  private reported = false;

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch() {
    if (this.reported) return;
    this.reported = true;
    this.props.onFailed?.();
  }

  render() {
    return this.state.failed ? null : this.props.children;
  }
}
