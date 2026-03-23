import { describe, expect, it } from "vitest";
import { getOetVisualConfig } from "@/lib/oet/visual";

describe("OET visual recipe mapping", () => {
  it("maps learner overview routes to reporting and command boards", () => {
    expect(
      getOetVisualConfig({
        mainTitle: "Dashboard",
        path: ["Dashboard"],
        title: "Learner App",
      }).recipe
    ).toBe("reporting");

    expect(
      getOetVisualConfig({
        mainTitle: "Goal Setup",
        path: ["Goal Setup"],
        title: "Learner App",
      }).recipe
    ).toBe("command");
  });

  it("maps task-heavy pages to workflow workspaces", () => {
    expect(
      getOetVisualConfig({
        mainTitle: "Writing Attempt",
        path: ["Writing", "Attempt"],
        title: "Learner App",
      }).recipe
    ).toBe("workflow");

    expect(
      getOetVisualConfig({
        mainTitle: "Speaking Review",
        path: ["Review Queue", "Speaking Review"],
        title: "Expert Console",
      }).recipe
    ).toBe("workflow");
  });

  it("provides rich visual metadata for expert and admin boards", () => {
    const expertQueue = getOetVisualConfig({
      mainTitle: "Review Queue",
      path: ["Review Queue"],
      title: "Expert Console",
    });
    const adminContent = getOetVisualConfig({
      mainTitle: "Content Library",
      path: ["Content Library"],
      title: "Admin CMS",
    });

    expect(expertQueue.recipe).toBe("reporting");
    expect(expertQueue.heroStats.length).toBeGreaterThanOrEqual(3);
    expect(expertQueue.activityItems.length).toBeGreaterThanOrEqual(3);

    expect(adminContent.recipe).toBe("reporting");
    expect(adminContent.chart.categories.length).toBeGreaterThan(0);
    expect(adminContent.chart.series.length).toBeGreaterThan(0);
  });

  it("falls back to a usable visual preset for unmapped routes", () => {
    const fallback = getOetVisualConfig({
      mainTitle: "Unknown Screen",
      path: ["Mystery"],
      title: "Learner App",
    });

    expect(fallback.recipe).toBe("command");
    expect(fallback.heroStats.length).toBeGreaterThanOrEqual(3);
    expect(fallback.chips.length).toBeGreaterThan(0);
  });
});
