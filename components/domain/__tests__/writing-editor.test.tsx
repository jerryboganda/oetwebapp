import { render, screen } from '@testing-library/react';
import { WritingEditor } from '../writing-editor';

describe('WritingEditor', () => {
  it('shows save-state feedback and respects the font control toggle', () => {
    const { container } = render(
      <WritingEditor
        value="A short practice response"
        onChange={vi.fn()}
        saveStatus="saving"
        fontSize={18}
        onFontSizeChange={vi.fn()}
        showFontSizeControls={false}
      />,
    );

    expect(container).toHaveTextContent(/4\s*words/i);
    expect(screen.getByText('Saving...')).toBeInTheDocument();
    expect(screen.queryByLabelText('Decrease font size')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Increase font size')).not.toBeInTheDocument();
  });
});
