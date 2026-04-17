import { vi } from 'vitest';
import '@testing-library/jest-dom/vitest';
import { Children, createElement, Fragment, useEffect, useRef, type ReactNode } from 'react';

vi.mock('@/lib/mobile/haptics', () => ({
	triggerImpactHaptic: vi.fn(),
	triggerNotificationHaptic: vi.fn(),
}));

/* ── next/link mock ──
 * Filter out non-standard HTML attributes (e.g. prefetch) that Next.js Link
 * accepts but would cause React DOM warnings on a plain <a> element.
 */
const NEXTLINK_NON_HTML_PROPS = ['prefetch', 'scroll', 'shallow', 'passHref', 'replace', 'legacyBehavior', 'locale'];
vi.mock('next/link', () => ({
	default: ({ children, href, ...props }: { children: React.ReactNode; href?: string; [key: string]: unknown }) => {
		const htmlProps: Record<string, unknown> = {};
		for (const [key, value] of Object.entries(props)) {
			if (!NEXTLINK_NON_HTML_PROPS.includes(key)) {
				htmlProps[key] = value;
			}
		}
		return createElement('a', { href, ...htmlProps }, children);
	},
}));

/* ── motion/react global mock ──
 * Uses a Proxy to handle any element tag (`motion.div`, `motion.section`, etc.)
 * and strips motion-specific props so they don't leak to DOM elements and cause
 * React warnings like "whileHover is not a valid DOM prop".
 *
 * NOTE on `style`: motion's `style` prop can contain MotionValues (functions
 * whose `.get()` yields the current value) that React cannot render on a
 * plain DOM node. We keep the `style` prop but strip any function/MotionValue
 * entries — this preserves inline styles tests assert on (widths, transforms,
 * etc.) while avoiding React warnings.
 */
const MOTION_PROPS = [
	'initial', 'animate', 'exit', 'transition', 'variants',
	'whileHover', 'whileTap', 'whileFocus', 'whileDrag', 'whileInView',
	'custom', 'layout', 'layoutId', 'layoutScroll', 'layoutDependency',
	'onAnimationStart', 'onAnimationComplete', 'onLayoutAnimationStart', 'onLayoutAnimationComplete',
];

function sanitizeStyle(style: unknown): Record<string, unknown> | undefined {
	if (style === null || style === undefined) return undefined;
	if (typeof style !== 'object') return undefined;
	const plain: Record<string, unknown> = {};
	for (const [k, v] of Object.entries(style as Record<string, unknown>)) {
		// Skip MotionValue-like objects (have .get()) and raw functions.
		if (typeof v === 'function') continue;
		if (v && typeof v === 'object' && typeof (v as { get?: unknown }).get === 'function') continue;
		plain[k] = v;
	}
	return plain;
}

function stripMotionProps(props: Record<string, unknown>) {
	const clean: Record<string, unknown> = {};
	for (const [key, value] of Object.entries(props)) {
		if (MOTION_PROPS.includes(key)) continue;
		if (key === 'style') {
			const safe = sanitizeStyle(value);
			if (safe) clean.style = safe;
			continue;
		}
		clean[key] = value;
	}
	return clean;
}

function makeMotionElement(tag: string) {
	const MotionStub = ({ children, ...props }: { children?: React.ReactNode; [key: string]: unknown }) =>
		createElement(tag, stripMotionProps(props), children);
	MotionStub.displayName = `motion.${tag}`;
	return MotionStub;
}

/**
 * AnimatePresence mock that calls onExitComplete when children transition from
 * present → absent, mimicking the real motion library's exit animation callback.
 * Uses setTimeout(0) to defer the callback so that parent useEffects (e.g.
 * Modal's shouldRestoreFocusRef flag) run first — matching real animation timing.
 */
function AnimatePresenceMock({
	children,
	onExitComplete,
}: {
	children?: ReactNode;
	onExitComplete?: () => void;
	[key: string]: unknown;
}) {
	const hadContentRef = useRef(false);
	const hasContent = Children.count(children) > 0;

	useEffect(() => {
		if (hadContentRef.current && !hasContent && onExitComplete) {
			setTimeout(onExitComplete, 0);
		}
		hadContentRef.current = hasContent;
	});

	return createElement(Fragment, null, children);
}

vi.mock('motion/react', () => ({
	motion: new Proxy({}, { get: (_target, prop: string) => makeMotionElement(prop) }),
	useReducedMotion: () => false,
	AnimatePresence: AnimatePresenceMock,
	MotionConfig: ({ children }: { children?: ReactNode }) => createElement(Fragment, null, children),
}));

if (typeof window !== 'undefined' && !window.matchMedia) {
	Object.defineProperty(window, 'matchMedia', {
		writable: true,
		value: vi.fn().mockImplementation((query: string) => ({
			matches: false,
			media: query,
			onchange: null,
			addEventListener: vi.fn(),
			removeEventListener: vi.fn(),
			addListener: vi.fn(),
			removeListener: vi.fn(),
			dispatchEvent: vi.fn(),
		})),
	});
}
