import * as matchers from '@testing-library/jest-dom/matchers';
import React from 'react';

// Extend vitest expect with jest-dom matchers (using global expect since
// importing from 'vitest' is broken on Node 24 + Vitest 4.1.x)
expect.extend(matchers);

// Global mock for motion/react — uses a Proxy so that motion.div, motion.button,
// motion.span, etc. all resolve to simple HTML wrappers instead of real motion
// components. Individual tests can override via vi.mock('motion/react', ...).
vi.mock('motion/react', () => {
	const motionProxy = new Proxy(
		{},
		{
			get(_target, prop: string) {
				// Return a forwardRef component that renders the raw HTML tag and
				// strips motion-specific props so they don't leak into the DOM.
				const MotionTag = React.forwardRef(({ children, initial: _i, animate: _a, exit: _e, transition: _t, whileHover: _wh, whileTap: _wt, whileInView: _wi, whileFocus: _wf, whileDrag: _wd, layout: _l, layoutId: _li, variants: _v, ...rest }: any, ref: any) => {
					return React.createElement(prop, { ...rest, ref }, children);
				});
				MotionTag.displayName = `motion.${String(prop)}`;
				return MotionTag;
			},
		},
	);

	return {
		motion: motionProxy,
		AnimatePresence: ({ children }: { children: React.ReactNode }) => React.createElement(React.Fragment, null, children),
		MotionConfig: ({ children }: { children: React.ReactNode }) => React.createElement(React.Fragment, null, children),
		useReducedMotion: () => false,
		useAnimation: () => ({ start: vi.fn(), stop: vi.fn() }),
		useMotionValue: (initial: number) => ({ get: () => initial, set: vi.fn(), onChange: vi.fn() }),
		useTransform: (value: any) => value,
		useSpring: (value: any) => value,
		useScroll: () => ({ scrollY: { get: () => 0, onChange: vi.fn() }, scrollYProgress: { get: () => 0, onChange: vi.fn() } }),
		useInView: () => true,
		LayoutGroup: ({ children }: { children: React.ReactNode }) => React.createElement(React.Fragment, null, children),
	};
});

if (typeof window !== 'undefined' && !window.ResizeObserver) {
	class ResizeObserver {
		observe() {}
		unobserve() {}
		disconnect() {}
	}

	Object.defineProperty(window, 'ResizeObserver', {
		configurable: true,
		writable: true,
		value: ResizeObserver,
	});
}

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
