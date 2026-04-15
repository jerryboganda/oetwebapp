import { render, screen } from '@testing-library/react';
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
    expect(container).toHaveClass('py-2');
    expect(container).toHaveClass('sm:py-4');
    expect(container).toHaveClass('lg:py-6');
  });

  it('does not apply double horizontal padding (shell removed its px)', () => {
    render(
      <LearnerWorkspaceContainer>
        <div>Content</div>
      </LearnerWorkspaceContainer>,
    );

    const container = screen.getByTestId('learner-workspace-container');
    // Container owns the horizontal padding, not the shell
    expect(container).toHaveClass('px-4');
    expect(container).toHaveClass('sm:px-6');
    expect(container).toHaveClass('lg:px-8');
  });

  it('supports custom className merge', () => {
    render(
      <LearnerWorkspaceContainer className="space-y-8">
        <div>Content</div>
      </LearnerWorkspaceContainer>,
    );

    const container = screen.getByTestId('learner-workspace-container');
    expect(container).toHaveClass('space-y-8');
  });
});
