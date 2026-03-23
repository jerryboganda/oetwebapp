import {
  adminBuilderVisual,
  adminOperationsVisual,
  expertQueueVisual,
  expertWorkflowVisual,
  learnerAssessmentVisual,
  learnerDashboardVisual,
  learnerGoalsVisual,
  learnerSpeakingVisual,
  learnerStudyPlanVisual,
  learnerWorkflowVisual,
  learnerWritingVisual,
} from "@/Data/OET/visuals";
import type { OetVisualConfig } from "@/types/oet";

interface OetVisualContextInput {
  mainTitle: string;
  path: string[];
  title: string;
}

function cloneConfig(config: OetVisualConfig): OetVisualConfig {
  return {
    ...config,
    activityItems: config.activityItems.map((item) => ({ ...item })),
    avatars: config.avatars.map((avatar) => ({ ...avatar })),
    chart: {
      ...config.chart,
      categories: [...config.chart.categories],
      series: config.chart.series.map((series) => ({
        ...series,
        data: [...series.data],
      })),
    },
    chips: [...config.chips],
    heroStats: config.heroStats.map((stat) => ({ ...stat })),
  };
}

function normalize(value: string): string {
  return value.trim().toLowerCase();
}

function getSurface(title: string): "admin" | "expert" | "learner" {
  const normalizedTitle = normalize(title);
  if (normalizedTitle.includes("admin")) {
    return "admin";
  }
  if (normalizedTitle.includes("expert")) {
    return "expert";
  }
  return "learner";
}

function getPathKey(path: string[]): string {
  return path.map((segment) => normalize(segment)).join("/");
}

export function getOetVisualConfig(
  input: OetVisualContextInput
): OetVisualConfig {
  const surface = getSurface(input.title);
  const key = getPathKey(input.path);
  const title = normalize(input.mainTitle);

  if (surface === "learner") {
    if (
      key === "dashboard" ||
      key.includes("study plan") ||
      key.includes("readiness") ||
      key.includes("progress") ||
      key.includes("billing")
    ) {
      return cloneConfig(
        key === "dashboard" ? learnerDashboardVisual : learnerStudyPlanVisual
      );
    }

    if (
      key.includes("onboarding") ||
      key.includes("goal setup") ||
      key.includes("diagnostic") ||
      key.includes("mock center") ||
      key.endsWith("report")
    ) {
      return cloneConfig(
        key.includes("goal setup")
          ? learnerGoalsVisual
          : learnerAssessmentVisual
      );
    }

    if (
      title.includes("attempt") ||
      title.includes("review") ||
      title.includes("task") ||
      title.includes("model answer")
    ) {
      return cloneConfig(learnerWorkflowVisual);
    }

    if (key.startsWith("writing")) {
      return cloneConfig(learnerWritingVisual);
    }

    if (key.startsWith("speaking")) {
      return cloneConfig(learnerSpeakingVisual);
    }

    if (key.startsWith("reading") || key.startsWith("listening")) {
      return cloneConfig(learnerWorkflowVisual);
    }

    if (key === "settings" || key === "reviews" || key === "history") {
      return cloneConfig(learnerStudyPlanVisual);
    }
  }

  if (surface === "expert") {
    if (
      key === "review queue" ||
      key === "calibration" ||
      key === "metrics" ||
      key === "schedule"
    ) {
      return cloneConfig(expertQueueVisual);
    }

    if (title.includes("review") || title.includes("learner")) {
      return cloneConfig(
        title.includes("learner") ? expertQueueVisual : expertWorkflowVisual
      );
    }
  }

  if (surface === "admin") {
    if (
      key === "task builder" ||
      key === "content library/content detail" ||
      key === "content library/revisions" ||
      title.includes("task builder") ||
      title.includes("content detail") ||
      title.includes("content revisions")
    ) {
      return cloneConfig(adminBuilderVisual);
    }

    return cloneConfig(adminOperationsVisual);
  }

  return cloneConfig(learnerGoalsVisual);
}
