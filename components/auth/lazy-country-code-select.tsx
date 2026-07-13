'use client';

import dynamic from 'next/dynamic';
import type { CountryCodeSelectProps } from './country-code-select';

export function CountryCodeSelectFallback() {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Loading country calling code selector"
      style={{
        alignItems: 'center',
        background: 'rgba(255, 255, 255, 0.85)',
        border: '1px solid rgba(117, 131, 178, 0.28)',
        borderRadius: '1.1rem 0 0 1.1rem',
        boxSizing: 'border-box',
        color: '#66708f',
        display: 'inline-flex',
        flexShrink: 0,
        fontSize: 13,
        fontWeight: 700,
        gap: 8,
        height: 54,
        padding: '0 12px',
        width: 150,
      }}
    >
      <span aria-hidden="true">🌐</span>
      <span aria-hidden="true">+••</span>
      <span className="sr-only">Loading country calling code selector</span>
    </div>
  );
}

const CountryCodeSelect = dynamic(() => import('./country-code-select'), {
  loading: CountryCodeSelectFallback,
  ssr: false,
});

export default function LazyCountryCodeSelect(props: CountryCodeSelectProps) {
  return <CountryCodeSelect {...props} />;
}
