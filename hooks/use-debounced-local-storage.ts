'use client';
import { useEffect, useRef } from 'react';

type Options<T> = {
  delayMs?: number;
  serialize?: (value: T) => string;
};

export function useDebouncedLocalStorage<T>(key: string, value: T, options?: Options<T>) {
  const { delayMs = 500, serialize } = options ?? {};
  const handleRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (handleRef.current) clearTimeout(handleRef.current);
    handleRef.current = setTimeout(() => {
      try {
        const payload = serialize ? serialize(value) : JSON.stringify(value);
        window.localStorage.setItem(key, payload);
      } catch {
        /* quota or privacy mode */
      }
    }, delayMs);
    return () => {
      if (handleRef.current) clearTimeout(handleRef.current);
    };
  }, [key, value, delayMs, serialize]);
}
