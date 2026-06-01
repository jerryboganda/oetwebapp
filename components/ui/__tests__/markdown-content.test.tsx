import { render, screen, within } from '@testing-library/react';
import { MarkdownContent } from '../markdown-content';

describe('MarkdownContent', () => {
  it('renders markdown tables with inline formatting and links', () => {
    render(
      <MarkdownContent
        markdown={`# Clinical summary

| Item | Result |
| --- | ---: |
| **Hemoglobin** | [13.4](https://example.com) |
| Platelets | 240 |
`}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Clinical summary' })).toBeInTheDocument();

    const table = screen.getByRole('table');
    expect(within(table).getByText('Item')).toBeInTheDocument();
    expect(within(table).getByText('Result')).toBeInTheDocument();
    expect(within(table).getByText('Hemoglobin')).toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '13.4' })).toHaveAttribute('href', 'https://example.com');
    expect(within(table).getByText('Platelets')).toBeInTheDocument();
    expect(within(table).getByText('240')).toBeInTheDocument();
  });
});