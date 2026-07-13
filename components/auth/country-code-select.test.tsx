import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import CountryCodeSelect from './country-code-select';
import { CountryCodeSelectFallback } from './lazy-country-code-select';

describe('CountryCodeSelect', () => {
  it('keeps the searchable selector keyboard accessible and reports the selected country', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <CountryCodeSelect
        inputId="registration-country-code"
        value="pk"
        onChange={onChange}
      />,
    );

    const selector = screen.getByRole('combobox', { name: 'Country calling code' });
    expect(selector).toHaveAttribute('id', 'registration-country-code');
    expect(screen.getByText('+92')).toBeInTheDocument();

    await user.click(selector);
    await user.type(selector, 'United Kingdom');
    await user.keyboard('{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({
        dialCode: '+44',
        isoCode: 'GB',
        label: 'United Kingdom',
        value: 'gb',
      }),
    );
  });

  it('reserves the selector dimensions with an accessible loading fallback', () => {
    render(<CountryCodeSelectFallback />);

    const fallback = screen.getByRole('status', {
      name: 'Loading country calling code selector',
    });
    expect(fallback).toHaveAttribute('aria-busy', 'true');
    expect(fallback).toHaveStyle({ height: '54px', width: '150px' });
  });
});
