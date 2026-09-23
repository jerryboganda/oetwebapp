import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Headphones } from 'lucide-react';

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { FreeSampleCard } from './free-sample-card';

describe('FreeSampleCard', () => {
  it('renders a link with the FREE SAMPLE badge, title and one-line description', () => {
    render(
      <FreeSampleCard
        testId="fs-card"
        icon={Headphones}
        title="Free Listening Mock"
        description="Try one complete OET Listening mock for free."
        href="/listening/paper/atlas-st3"
      />,
    );

    const card = screen.getByTestId('fs-card');
    expect(card.tagName).toBe('A');
    expect(card).toHaveAttribute('href', '/listening/paper/atlas-st3');
    expect(card).toHaveTextContent('Free Listening Mock');
    expect(card).toHaveTextContent('Free sample');
    expect(card).toHaveTextContent('Try one complete OET Listening mock for free.');
  });

  it('renders a button when there is no href', async () => {
    const onClick = vi.fn();
    render(
      <FreeSampleCard testId="fs-card" icon={Headphones} title="Free Writing Mock" description="Pick your profession." onClick={onClick} />,
    );

    const card = screen.getByRole('button', { name: /free writing mock/i });
    expect(card).toHaveAttribute('type', 'button');
    await userEvent.setup().click(card);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('shows a translated badge and keeps a spent sample visible but inert', async () => {
    const onClick = vi.fn();
    render(
      <FreeSampleCard
        testId="fs-card"
        icon={Headphones}
        title="اختبار كتابة مجاني"
        description="جرّب رسالة واحدة."
        badgeLabel="عيّنة مجانية"
        disabled
        onClick={onClick}
        footer={<span>تم الاستخدام</span>}
      />,
    );

    const card = screen.getByTestId('fs-card');
    expect(card).toBeDisabled();
    expect(card).toHaveTextContent('عيّنة مجانية');
    expect(card).not.toHaveTextContent('Free sample');
    expect(card).toHaveTextContent('تم الاستخدام');
    await userEvent.setup().click(card);
    expect(onClick).not.toHaveBeenCalled();
  });
});
