import * as matchers from '@testing-library/jest-dom/matchers';

// Extend vitest expect with jest-dom matchers (using global expect since
// importing from 'vitest' is broken on Node 24 + Vitest 4.1.x)
expect.extend(matchers);

if (typeof window !== 'undefined' && !window.ResizeObserver) {
	class ResizeObserver {
		observe() {}
		unobserve() {}
		disconnect() {}
	}

	Object.defineProperty(window, 'ResizeObserver', {
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
