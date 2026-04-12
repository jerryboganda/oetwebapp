import { vi } from 'vitest';
import '@testing-library/jest-dom/vitest';
import { createElement } from 'react';

vi.mock('@/lib/mobile/haptics', () => ({
	triggerImpactHaptic: vi.fn(),
	triggerNotificationHaptic: vi.fn(),
}));

vi.mock('next/link', () => ({
	default: ({ children, href, ...props }: { children: React.ReactNode; href?: string }) =>
		createElement('a', { href, ...props }, children),
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
