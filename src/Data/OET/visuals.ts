import type {
  OetVisualActivityItem,
  OetVisualAvatar,
  OetVisualChart,
  OetVisualConfig,
  OetVisualHeroStat,
} from "@/types/oet";

function buildStats(
  stats: Array<
    [
      label: string,
      value: string | number,
      helper: string,
      delta: string,
      icon: string,
      tone: OetVisualConfig["accent"],
    ]
  >
): OetVisualHeroStat[] {
  return stats.map(([label, value, helper, delta, icon, tone]) => ({
    delta,
    helper,
    icon,
    label,
    tone,
    value,
  }));
}

function buildActivities(
  items: Array<
    [
      badge: string,
      title: string,
      meta: string,
      description: string,
      tone: OetVisualConfig["accent"],
    ]
  >
): OetVisualActivityItem[] {
  return items.map(([badge, title, meta, description, tone]) => ({
    badge,
    description,
    meta,
    title,
    tone,
  }));
}

function buildAvatars(
  avatars: Array<
    [avatarUrl: string, name: string, tone: OetVisualConfig["accent"]]
  >
): OetVisualAvatar[] {
  return avatars.map(([avatarUrl, name, tone]) => ({
    avatarUrl,
    name,
    tone,
  }));
}

function buildChart(chart: OetVisualChart): OetVisualChart {
  return chart;
}

export const learnerDashboardVisual: OetVisualConfig = {
  accent: "primary",
  activityItems: buildActivities([
    [
      "Now",
      "Writing sprint is live",
      "45 min focus window",
      "Purpose and organisation are rising fastest when the learner resumes revision immediately.",
      "primary",
    ],
    [
      "Signal",
      "Confidence recovered in Reading",
      "2 consecutive stronger drills",
      "Speed-scanning work is now stable enough to shift energy back toward Writing and Speaking.",
      "success",
    ],
    [
      "Queue",
      "Expert review remains available",
      "1 review credit reserved",
      "Lower-confidence speaking work is already queued for optional expert escalation.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/1.png", "Hannah Musa", "primary"],
    ["/images/avatar/4.png", "Writing Coach", "success"],
    ["/images/avatar/6.png", "Review Partner", "warning"],
  ]),
  chart: buildChart({
    categories: ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
    series: [
      { data: [58, 66, 63, 74, 71, 82], name: "Readiness" },
      { data: [45, 51, 57, 61, 69, 76], name: "Completion" },
    ],
    type: "area",
  }),
  chips: ["Learner control room", "Criterion-first", "Confidence-labeled"],
  heroStats: buildStats([
    [
      "Readiness",
      "78%",
      "Target remains realistic",
      "+8%",
      "activity",
      "primary",
    ],
    ["Study streak", "9 days", "Momentum is visible", "+2", "flash", "warning"],
    [
      "Expert coverage",
      "1 queued",
      "Review safety net stays on",
      "Live",
      "shield",
      "success",
    ],
    ["Hours banked", "12.5h", "This cycle", "+3.2h", "timer", "info"],
  ]),
  recipe: "reporting",
  summary:
    "A high-energy dashboard board that keeps readiness, task pressure, and expert support visible at a glance.",
};

export const learnerGoalsVisual: OetVisualConfig = {
  accent: "info",
  activityItems: buildActivities([
    [
      "Step 1",
      "Profession alignment",
      "Country + role shape task inventory",
      "Choosing the target profession immediately changes the writing and speaking case mix.",
      "info",
    ],
    [
      "Step 2",
      "Score range targets",
      "Each subtest stays independent",
      "The goal profile keeps Writing and Speaking visible instead of collapsing them into one band.",
      "primary",
    ],
    [
      "Step 3",
      "Intensity calibration",
      "Study hours drive the plan",
      "Weekly time availability controls checkpoint cadence and recommendation pressure.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/2.png", "Goal Coach", "info"],
    ["/images/avatar/5.png", "Exam Planner", "primary"],
    ["/images/avatar/8.png", "Support", "warning"],
  ]),
  chart: buildChart({
    categories: ["4h", "6h", "8h", "10h", "12h", "14h"],
    series: [
      { data: [22, 38, 49, 61, 75, 86], name: "Plan confidence" },
      { data: [18, 27, 41, 57, 68, 78], name: "Recovery margin" },
    ],
    type: "bar",
  }),
  chips: ["Command board", "Setup flow", "Adaptive plan ready"],
  heroStats: buildStats([
    [
      "Target date",
      "27 Jun",
      "Next booked exam",
      "96 days",
      "calendar",
      "info",
    ],
    [
      "Weekly load",
      "10h",
      "Recommended intensity",
      "Target",
      "timer",
      "primary",
    ],
    ["Weak subtests", "2", "Writing + Speaking", "Tracked", "flash", "warning"],
    [
      "Country fit",
      "AU",
      "Australia rules applied",
      "Live",
      "globe",
      "success",
    ],
  ]),
  recipe: "command",
  summary:
    "A command-style setup board that turns exam goals into visible operating settings rather than plain form fields.",
};

export const learnerStudyPlanVisual: OetVisualConfig = {
  accent: "success",
  activityItems: buildActivities([
    [
      "Today",
      "Referral letter revision sprint",
      "45 minutes",
      "The highest-yield session stays pinned to the top of the plan feed.",
      "success",
    ],
    [
      "Tomorrow",
      "Empathy speaking drill",
      "30 minutes",
      "Speaking relationship-building remains the clearest secondary weakness.",
      "info",
    ],
    [
      "Weekend",
      "Checkpoint reset",
      "Regenerate focus areas",
      "Study-plan refresh becomes more valuable after the next diagnostic or review result.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/1.png", "Learner", "success"],
    ["/images/avatar/5.png", "Plan Engine", "primary"],
    ["/images/avatar/9.png", "Coach", "warning"],
  ]),
  chart: buildChart({
    categories: ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
    series: [
      { data: [40, 60, 58, 76, 71, 82, 68], name: "Completion" },
      { data: [25, 35, 43, 54, 61, 69, 74], name: "Confidence" },
    ],
    type: "line",
  }),
  chips: ["Reporting board", "Adaptive plan", "Next checkpoint visible"],
  heroStats: buildStats([
    ["Today queued", 2, "Visible and timed", "+1", "activity", "success"],
    ["This week", 4, "Scheduled sessions", "Stable", "stack", "primary"],
    [
      "Checkpoint",
      "29 Mar",
      "Weekend reset",
      "Upcoming",
      "calendar",
      "warning",
    ],
    ["Recovery margin", "68h", "Remaining study bank", "+5h", "shield", "info"],
  ]),
  recipe: "reporting",
  summary:
    "A reporting board that treats the study plan as a live operating system instead of a flat to-do list.",
};

export const learnerAssessmentVisual: OetVisualConfig = {
  accent: "warning",
  activityItems: buildActivities([
    [
      "Prep",
      "Writing diagnostic first",
      "Profession-led case notes",
      "The strongest initial signal still comes from the writing case note selection and opening control.",
      "warning",
    ],
    [
      "Audio",
      "Speaking role card ready",
      "Mic and upload safe",
      "Transcript generation and better-phrasing review remain attached to the first speaking run.",
      "info",
    ],
    [
      "Mock",
      "Full simulation available",
      "When endurance matters more",
      "The full mock stays visible once enough subtest signal has been collected.",
      "primary",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/3.png", "Diagnostic Engine", "warning"],
    ["/images/avatar/7.png", "Speaking Monitor", "info"],
    ["/images/avatar/10.png", "Mock Supervisor", "primary"],
  ]),
  chart: buildChart({
    categories: ["Writing", "Speaking", "Reading", "Listening"],
    series: [
      { data: [62, 54, 73, 77], name: "Current confidence" },
      { data: [74, 68, 81, 84], name: "Target lane" },
    ],
    type: "bar",
  }),
  chips: ["Command board", "Diagnostics", "Mock-ready visuals"],
  heroStats: buildStats([
    ["Signals captured", "4", "Across subtests", "+1", "activity", "warning"],
    [
      "Mock readiness",
      "72%",
      "Enough data to simulate",
      "+6%",
      "chart",
      "primary",
    ],
    ["Audio safe", "Ready", "Mic + upload checks", "On", "microphone", "info"],
    ["Risk flag", "Writing", "Primary bottleneck", "Focus", "flash", "danger"],
  ]),
  recipe: "command",
  summary:
    "A vivid assessment board that makes diagnostics and mocks feel like operating modules, not isolated forms.",
};

export const learnerWritingVisual: OetVisualConfig = {
  accent: "primary",
  activityItems: buildActivities([
    [
      "Focus",
      "Purpose drives score lift",
      "Opening sentence still matters most",
      "Recent writing work improves fastest when purpose is explicit in the first line.",
      "primary",
    ],
    [
      "Pattern",
      "Content trimming still needed",
      "Irrelevant detail creeps in under pressure",
      "Chronology remains overrepresented when the learner loses time.",
      "warning",
    ],
    [
      "Support",
      "Expert escalation available",
      "Confidence-aware review path",
      "Low-confidence letters can still route to expert review without redesigning the workspace.",
      "success",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/1.png", "Hannah Musa", "primary"],
    ["/images/avatar/4.png", "Writing Reviewer", "warning"],
    ["/images/avatar/6.png", "Language Coach", "success"],
  ]),
  chart: buildChart({
    categories: ["Purpose", "Content", "Organisation", "Language", "Genre"],
    series: [
      { data: [74, 61, 68, 71, 66], name: "Current" },
      { data: [82, 78, 80, 79, 76], name: "Target" },
    ],
    type: "bar",
  }),
  chips: ["Writing command deck", "Criterion map", "Revision-ready"],
  heroStats: buildStats([
    [
      "Latest range",
      "350-390",
      "Confidence-labeled",
      "+10",
      "chart",
      "primary",
    ],
    ["Revision mode", "Active", "Autosave protected", "On", "flash", "info"],
    [
      "Expert path",
      "Available",
      "Manual review safety net",
      "Open",
      "shield",
      "success",
    ],
    [
      "Draft health",
      "92%",
      "Recovered and synced",
      "Stable",
      "activity",
      "warning",
    ],
  ]),
  recipe: "reporting",
  summary:
    "A chart-led writing board that keeps criteria, revision pressure, and review safety visible without losing exam focus.",
};

export const learnerSpeakingVisual: OetVisualConfig = {
  accent: "info",
  activityItems: buildActivities([
    [
      "Transcript",
      "Empathy markers under review",
      "Relationship-building remains the live watchpoint",
      "The transcript and better-phrasing loop stays front and center after every speaking run.",
      "info",
    ],
    [
      "Audio",
      "Upload and mic checks are green",
      "Retry-safe pipeline",
      "Speaking work remains resilient on unstable connections and smaller screens.",
      "success",
    ],
    [
      "Review",
      "Human escalation still matters",
      "Moderate confidence threshold",
      "Lower-confidence spoken performance is still routed into expert-friendly review surfaces.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/1.png", "Hannah Musa", "info"],
    ["/images/avatar/5.png", "Transcript QA", "success"],
    ["/images/avatar/6.png", "Expert Reviewer", "warning"],
  ]),
  chart: buildChart({
    categories: [
      "Intelligibility",
      "Fluency",
      "Language",
      "Relationship",
      "Perspective",
    ],
    series: [
      { data: [72, 69, 66, 58, 61], name: "Current" },
      { data: [80, 79, 77, 74, 75], name: "Target" },
    ],
    type: "bar",
  }),
  chips: ["Speaking command deck", "Transcript-led", "Audio-safe"],
  heroStats: buildStats([
    ["Latest range", "340-380", "Moderate confidence", "+8", "chart", "info"],
    [
      "Audio state",
      "Safe",
      "Upload + replay stable",
      "Live",
      "microphone",
      "success",
    ],
    [
      "Transcript flags",
      "6",
      "Inline coaching moments",
      "Tracked",
      "flash",
      "warning",
    ],
    [
      "Expert review",
      "Optional",
      "Escalation available",
      "Open",
      "shield",
      "primary",
    ],
  ]),
  recipe: "reporting",
  summary:
    "A speaking board that feels alive with transcript, audio, and review energy instead of plain result cards.",
};

export const learnerWorkflowVisual: OetVisualConfig = {
  accent: "primary",
  activityItems: buildActivities([
    [
      "Live",
      "Focus workspace active",
      "Central task canvas stays dominant",
      "Decorative density lives in the rails, not over the response area.",
      "primary",
    ],
    [
      "Save",
      "Draft recovery ready",
      "Autosave and accidental-exit protection",
      "All long-form work keeps safety systems visible and close to the user.",
      "info",
    ],
    [
      "Support",
      "Insight rail attached",
      "Tracker, notes, and quality prompts",
      "The right rail keeps the workspace lively without interrupting live completion.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/1.png", "Candidate", "primary"],
    ["/images/avatar/4.png", "AI Guide", "info"],
    ["/images/avatar/6.png", "Expert Option", "warning"],
  ]),
  chart: buildChart({
    categories: ["Prep", "Draft", "Refine", "Check", "Submit"],
    series: [{ data: [24, 48, 61, 74, 88], name: "Completion pulse" }],
    type: "line",
  }),
  chips: ["Workflow workspace", "Rail-first visuals", "Focus preserved"],
  heroStats: buildStats([
    ["Task mode", "Live", "Focus-first canvas", "On", "flash", "primary"],
    [
      "Recovery",
      "Protected",
      "Draft + reconnect safe",
      "Ready",
      "shield",
      "info",
    ],
    ["Coach cues", "4", "Visible in side rail", "Live", "activity", "warning"],
    [
      "Submit gate",
      "Tracked",
      "Checklist remains attached",
      "Safe",
      "chart",
      "success",
    ],
  ]),
  recipe: "workflow",
  summary:
    "A rich workspace recipe that keeps one dominant task canvas while still using animated rails, chips, and tracker panels.",
};

export const expertQueueVisual: OetVisualConfig = {
  accent: "warning",
  activityItems: buildActivities([
    [
      "SLA",
      "Priority speaking review due soon",
      "21 minutes to breach",
      "The queue must foreground urgency, confidence, and assignment density in one glance.",
      "warning",
    ],
    [
      "Mix",
      "Writing queue is heavier today",
      "6 writing vs 4 speaking",
      "Reviewer workload remains visible through both charting and queue segmentation.",
      "info",
    ],
    [
      "Health",
      "Calibration alignment is strong",
      "94% agreement this week",
      "Quality confidence needs to feel operational, not buried in a separate report.",
      "success",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/4.png", "Lead Reviewer", "warning"],
    ["/images/avatar/5.png", "Queue Owner", "info"],
    ["/images/avatar/6.png", "Calibration Lead", "success"],
  ]),
  chart: buildChart({
    categories: ["08:00", "10:00", "12:00", "14:00", "16:00", "18:00"],
    series: [
      { data: [5, 8, 6, 9, 7, 4], name: "Queue load" },
      { data: [2, 3, 4, 4, 3, 2], name: "Priority" },
    ],
    type: "area",
  }),
  chips: ["Expert operations", "SLA visible", "Queue segmentation"],
  heroStats: buildStats([
    ["Assigned now", "10", "Active review items", "+2", "activity", "warning"],
    ["Priority items", "3", "Immediate attention", "Hot", "flash", "danger"],
    [
      "Avg turnaround",
      "7h",
      "Current reviewer pace",
      "-12%",
      "chart",
      "success",
    ],
    ["Alignment", "94%", "Calibration score", "+3%", "shield", "info"],
  ]),
  recipe: "reporting",
  summary:
    "A dense expert operations board focused on queue mix, SLA pressure, and reviewer quality signals.",
};

export const expertWorkflowVisual: OetVisualConfig = {
  accent: "danger",
  activityItems: buildActivities([
    [
      "Review",
      "Rubric rail is pinned",
      "Anchored notes + criteria always visible",
      "The human review workspace should feel heavier and more operational than the learner workspace.",
      "danger",
    ],
    [
      "Audio",
      "Waveform and transcript are linked",
      "Timestamp-led review",
      "Speaking review keeps audio context, issue flags, and note capture within one live board.",
      "info",
    ],
    [
      "Save",
      "Draft completion safe",
      "Partial reviews are preserved",
      "Experts must be able to pause and resume without losing rubric or comment work.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/4.png", "Expert", "danger"],
    ["/images/avatar/5.png", "Calibration", "info"],
    ["/images/avatar/6.png", "Ops", "warning"],
  ]),
  chart: buildChart({
    categories: ["Brief", "Review", "Notes", "Finalize", "Send"],
    series: [{ data: [18, 42, 61, 78, 92], name: "Completion path" }],
    type: "line",
  }),
  chips: ["Review workspace", "Rubric-led", "Draft-safe"],
  heroStats: buildStats([
    [
      "Turnaround",
      "< 8h",
      "Reviewer SLA target",
      "On track",
      "timer",
      "danger",
    ],
    [
      "Confidence route",
      "Low + moderate",
      "Human override active",
      "Live",
      "shield",
      "warning",
    ],
    ["Anchors", "6", "Transcript/comment links", "Pinned", "activity", "info"],
    ["Draft state", "Safe", "Resume anytime", "Protected", "flash", "success"],
  ]),
  recipe: "workflow",
  summary:
    "A heavier review workspace recipe with rubric gravity, timestamp context, and visible operational safeguards.",
};

export const adminOperationsVisual: OetVisualConfig = {
  accent: "primary",
  activityItems: buildActivities([
    [
      "Publish",
      "New writing content is ready",
      "2 revisions awaiting approval",
      "Content operations should feel like a live publishing and analytics system, not a simple CRUD table.",
      "primary",
    ],
    [
      "Quality",
      "Variance watchlist refreshed",
      "4 speaking cases flagged",
      "Analytics and routing need to sit close to the content and experiment surfaces.",
      "warning",
    ],
    [
      "Flags",
      "Mobile low-bandwidth flag enabled",
      "Learner impact is live",
      "Feature flags and rollout traces must feel operational and chart-backed.",
      "success",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/7.png", "Content Ops", "primary"],
    ["/images/avatar/8.png", "Quality Lead", "warning"],
    ["/images/avatar/9.png", "Platform Admin", "success"],
  ]),
  chart: buildChart({
    categories: ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
    series: [
      { data: [14, 18, 16, 22, 24, 20], name: "Published changes" },
      { data: [4, 7, 6, 5, 8, 6], name: "Risk flags" },
    ],
    type: "bar",
  }),
  chips: ["Admin reporting", "Content + ops", "Experiment-aware"],
  heroStats: buildStats([
    ["Published", "24", "Active content items", "+3", "activity", "primary"],
    ["Pending revisions", "6", "Awaiting action", "Live", "flash", "warning"],
    [
      "Quality variance",
      "8.4%",
      "AI-human disagreement",
      "-1.2%",
      "chart",
      "info",
    ],
    [
      "Flags enabled",
      "3",
      "Operational experiments",
      "Tracked",
      "shield",
      "success",
    ],
  ]),
  recipe: "reporting",
  summary:
    "A high-energy admin operations board that merges publishing, analytics, and feature governance into one visual system.",
};

export const adminBuilderVisual: OetVisualConfig = {
  accent: "success",
  activityItems: buildActivities([
    [
      "Build",
      "Metadata rail is attached",
      "Criteria mapping, versioning, and publish state",
      "Task-builder pages should feel like operational workspaces, not plain forms.",
      "success",
    ],
    [
      "Review",
      "Revision history stays adjacent",
      "Version-aware editing board",
      "The builder must keep revisions, criteria, and status context visible during edits.",
      "info",
    ],
    [
      "Guardrails",
      "Validation remains visible",
      "Publish states + routing notes",
      "Operational confidence grows when guardrails look designed-in rather than bolted on.",
      "warning",
    ],
  ]),
  avatars: buildAvatars([
    ["/images/avatar/7.png", "Editor", "success"],
    ["/images/avatar/8.png", "Reviewer", "info"],
    ["/images/avatar/9.png", "Publisher", "warning"],
  ]),
  chart: buildChart({
    categories: ["Structure", "Criteria", "Timing", "Metadata", "Publish"],
    series: [{ data: [22, 44, 63, 76, 88], name: "Builder completion" }],
    type: "line",
  }),
  chips: ["Workflow workspace", "Builder rail", "Revision-aware"],
  heroStats: buildStats([
    [
      "Builder state",
      "Draft",
      "Editable and recoverable",
      "Live",
      "flash",
      "success",
    ],
    [
      "Criteria mapped",
      "5",
      "Selected in current form",
      "+2",
      "activity",
      "primary",
    ],
    [
      "Revision depth",
      "3",
      "Visible beside canvas",
      "Tracked",
      "chart",
      "info",
    ],
    [
      "Publish gate",
      "Pending",
      "Quality checks remain attached",
      "Safe",
      "shield",
      "warning",
    ],
  ]),
  recipe: "workflow",
  summary:
    "A content-builder workspace that feels like a live editorial command center rather than a bare form.",
};
