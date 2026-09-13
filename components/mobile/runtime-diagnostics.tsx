'use client';

import { useEffect, useRef, useState } from 'react';
import { Capacitor } from '@capacitor/core';
import { getAppRuntimeKind } from '@/lib/runtime-signals';

/**
 * On-device probe for the Android keyboard / bottom-nav saga (rounds 1-3 all
 * shipped correct code that the device kept reporting as broken, and static
 * analysis ran out of layers to check — APK bytes, live chunks, live CSS,
 * cache headers, mount chain were all verified correct). This overlay answers
 * from INSIDE the device, live:
 *   - which deploy the shell is executing (NEXT_PUBLIC_DEPLOY_SHA),
 *   - whether the Capacitor bridge and initializeMobileRuntime actually ran,
 *   - what the runtime derivation sees (viewport metrics, dataset flag),
 *   - whether focus/plugin events fire at all,
 *   - whether the CSS actually hides the nav element.
 * Native shells only — renders nothing on web/desktop. Remove once the
 * keyboard behavior is confirmed fixed on real devices.
 */

interface LogEntry {
  time: string;
  kind: string;
  detail: string;
}

const NAV_SELECTOR = '.keyboard-safe-floating-bottom';

export function RuntimeDiagnostics() {
  const [open, setOpen] = useState(false);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [snapshot, setSnapshot] = useState<string>('');
  const pluginStateRef = useRef<string>('not loaded');
  const logRef = useRef<LogEntry[]>([]);

  const pushLog = (kind: string, detail: string) => {
    const entry: LogEntry = {
      time: new Date().toLocaleTimeString(),
      kind,
      detail,
    };
    logRef.current = [entry, ...logRef.current].slice(0, 40);
    setLog(logRef.current);
  };

  useEffect(() => {
    if (getAppRuntimeKind() !== 'capacitor-native') {
      return;
    }

    const cleanupFns: Array<() => void> = [];

    const focusIn = (event: FocusEvent) => {
      const target = event.target as HTMLElement | null;
      pushLog('focusin', `${target?.tagName ?? 'null'}${target?.isContentEditable ? ' (contentEditable)' : ''}`);
    };
    const focusOut = (event: FocusEvent) => {
      const target = event.target as HTMLElement | null;
      pushLog('focusout', target?.tagName ?? 'null');
    };
    const resize = () => {
      pushLog('resize', `innerHeight=${window.innerHeight} vv=${window.visualViewport?.height ?? 'n/a'}`);
    };
    document.addEventListener('focusin', focusIn);
    document.addEventListener('focusout', focusOut);
    window.addEventListener('resize', resize);
    cleanupFns.push(() => document.removeEventListener('focusin', focusIn));
    cleanupFns.push(() => document.removeEventListener('focusout', focusOut));
    cleanupFns.push(() => window.removeEventListener('resize', resize));

    void (async () => {
      try {
        const { Keyboard } = await import('@capacitor/keyboard');
        pluginStateRef.current = 'listeners registered';
        const handler = (name: string) => () => {
          pluginStateRef.current = `${name} fired`;
          pushLog('plugin', name);
        };
        const handles = await Promise.all([
          Keyboard.addListener('keyboardWillShow', handler('keyboardWillShow')),
          Keyboard.addListener('keyboardDidShow', handler('keyboardDidShow')),
          Keyboard.addListener('keyboardWillHide', handler('keyboardWillHide')),
          Keyboard.addListener('keyboardDidHide', handler('keyboardDidHide')),
        ]);
        cleanupFns.push(() => handles.forEach((handle) => void handle.remove()));
      } catch {
        pluginStateRef.current = 'import failed';
        pushLog('plugin', '@capacitor/keyboard import FAILED');
      }
    })();

    const tick = () => {
      const nav = document.querySelector(NAV_SELECTOR);
      let navStyle = 'ELEMENT NOT FOUND';
      if (nav) {
        const computed = getComputedStyle(nav);
        navStyle = `opacity=${computed.opacity} transform=${computed.transform === 'none' ? 'none' : 'translated'} pe=${computed.pointerEvents}`;
      }
      const ds = document.documentElement.dataset;
      const lines = [
        `deploy=${process.env.NEXT_PUBLIC_DEPLOY_SHA ?? 'unknown'}`,
        `bridge: window.Capacitor=${window.Capacitor ? 'yes' : 'NO'} native=${String(Capacitor.isNativePlatform())} platform=${Capacitor.getPlatform()}`,
        `bootstrap: runtimeKind=${ds.runtimeKind ?? '-'} capacitorNative=${ds.capacitorNative ?? '-'}`,
        `init: mobileRuntimeActive=${ds.mobileRuntimeActive ?? 'NO (bridge never ran)'}`,
        `state: keyboardVisible=${ds.keyboardVisible ?? '-'} activeEl=${document.activeElement?.tagName ?? 'null'}`,
        `metrics: innerHeight=${window.innerHeight} vv=${window.visualViewport?.height ?? 'n/a'} innerWidth=${window.innerWidth}`,
        `plugin: ${pluginStateRef.current}`,
        `nav: ${navStyle}`,
      ];
      setSnapshot(lines.join('\n'));
    };
    const interval = window.setInterval(tick, 500);
    tick();
    cleanupFns.push(() => window.clearInterval(interval));

    return () => {
      cleanupFns.forEach((fn) => {
        try {
          fn();
        } catch {
          // teardown is best-effort
        }
      });
    };
  }, []);

  if (getAppRuntimeKind() !== 'capacitor-native') {
    return null;
  }

  return (
    <div className="fixed bottom-2 left-2 z-[80] print:hidden">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        className="rounded-full bg-slate-900 px-3 py-1.5 text-[10px] font-bold tracking-wider text-amber-300 shadow-lg border border-amber-400/60"
      >
        {open ? '× KB DEBUG' : 'KB DEBUG'}
      </button>
      {open ? (
        <div
          className="mt-2 max-h-[60vh] w-[calc(100vw-2rem)] max-w-sm overflow-auto rounded-xl bg-slate-950/95 p-3 font-mono text-[11px] leading-relaxed text-emerald-200 shadow-2xl border border-emerald-500/30"
          role="status"
        >
          <pre className="whitespace-pre-wrap break-words">{snapshot || '…'}</pre>
          <div className="mt-2 border-t border-slate-700 pt-2 text-slate-300">
            <div className="font-bold text-amber-300">EVENT LOG (newest first)</div>
            {log.length === 0 ? (
              <div className="text-slate-500">no events yet — go focus the spelling input</div>
            ) : (
              log.map((entry, index) => (
                <div key={`${entry.time}-${index}`}>
                  {entry.time} {entry.kind}: {entry.detail}
                </div>
              ))
            )}
          </div>
        </div>
      ) : null}
    </div>
  );
}
