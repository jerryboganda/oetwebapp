import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LearnerWorkspaceContainer } from '../learner-workspace-container';

describe('LearnerWorkspaceContainer', () => {
  it('renders the dashboard gutter container classes', () => {
    render(
      <LearnerWorkspaceContainer>
        <div>Workspace Content</div>
      </LearnerWorkspaceContainer>,
    );

    const container = screen.getByTestId('learner-workspace-container');

    expect(container).toHaveClass('w-full');
    expect(container).toHaveClass('max-w-[1200px]');
    expect(container).toHaveClass('mx-auto');
    expect(container).toHaveClass('px-4');
    expect(container).toHaveClass('sm:px-6');
    expect(container).toHaveClass('lg:px-8');
    expect(container).toHaveClass('py-6');
  });
});
