import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PathwayBoard } from '@/components/domain/listening/PathwayBoard';

describe('PathwayBoard', () => {
  it('renders 12 stage tiles with statuses', () => {
    const stageKeys = [
      'diagnostic',
      'foundation_partA',
      'foundation_partB',
      'foundation_partC',
      'drill_partA',
      'drill_partB',
      'drill_partC',
      'minitest_partA',
      'minitest_partBC',
      'fullpaper_paper',
      'fullpaper_cbt',
      'exam_simulation',
    ];
    const stages = stageKeys.map((stageKey, i) => ({
      stageKey,
      status: (i < 2 ? 'Completed' : i === 2 ? 'InProgress' : i === 3 ? 'Unlocked' : 'Locked') as
        | 'Completed'
        | 'InProgress'
        | 'Unlocked'
        | 'Locked',
      bestScaledScore: i < 2 ? 380 : null,
      attemptsCount: i < 3 ? 1 : 0,
    }));
    render(<PathwayBoard stages={stages} />);
    expect(screen.getAllByText(/^Stage \d+$/i)).toHaveLength(12);
    expect(screen.getAllByText(/Completed/i).length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText(/Locked/i).length).toBeGreaterThanOrEqual(8);
  });

  it('links only actionable stages to their supplied exact launch URLs', () => {
    render(<PathwayBoard stages={[
      {
        stageKey: 'diagnostic',
        status: 'Completed',
        bestScaledScore: 360,
        attemptsCount: 1,
        actionHref: '/listening/player/lp-001?mode=diagnostic&pathwayStage=diagnostic',
      },
      {
        stageKey: 'foundation_partA',
        status: 'InProgress',
        bestScaledScore: 320,
        attemptsCount: 1,
        actionHref: '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partA',
      },
      {
        stageKey: 'foundation_partB',
        status: 'Unlocked',
        bestScaledScore: null,
        attemptsCount: 0,
        actionHref: '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partB',
      },
      {
        stageKey: 'foundation_partC',
        status: 'Locked',
        bestScaledScore: null,
        attemptsCount: 0,
        actionHref: '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partC',
      },
    ]} />);

    expect(screen.queryByRole('link', { name: /Diagnostic/i })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Continue Foundation - Part A/i })).toHaveAttribute(
      'href',
      '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partA',
    );
    expect(screen.getByRole('link', { name: /Start Foundation - Part B/i })).toHaveAttribute(
      'href',
      '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partB',
    );
    expect(screen.queryByRole('link', { name: /Foundation - Part C/i })).not.toBeInTheDocument();
  });
});
