import { render } from '@testing-library/react';
import { CelebrationBurst } from '../celebration-burst';

const particles = () => document.querySelectorAll('.celebration-particle');

describe('CelebrationBurst', () => {
  beforeEach(() => window.sessionStorage.clear());

  it('bursts for a real win and remembers it for the session', () => {
    render(<div className="relative"><CelebrationBurst active onceKey="result-1" /></div>);

    expect(particles()).toHaveLength(20);
    expect(window.sessionStorage.getItem('oet_celebrated:result-1')).toBe('1');
  });

  it('stays quiet when inactive or already celebrated this session', () => {
    window.sessionStorage.setItem('oet_celebrated:result-2', '1');
    render(
      <>
        <CelebrationBurst active={false} onceKey="result-3" />
        <CelebrationBurst active onceKey="result-2" />
      </>,
    );

    expect(particles()).toHaveLength(0);
    expect(window.sessionStorage.getItem('oet_celebrated:result-3')).toBeNull();
  });
});
