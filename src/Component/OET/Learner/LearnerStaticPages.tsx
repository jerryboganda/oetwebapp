"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import {
  Book,
  Brain,
  Calendar,
  ClipboardCheck,
  Dollar,
  HomeAlt,
  LeaderboardStar,
  MessageText,
  PageEdit,
  Reports,
  Settings,
  UserScan,
  WarningTriangle,
} from "iconoir-react";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Form,
  FormFeedback,
  FormGroup,
  Input,
  Label,
  Row,
} from "reactstrap";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";
import {
  ConfidenceBadge,
  EmptyStateCard,
  KeyValueList,
  OetMetricGrid,
  OetPageShell,
  OetSectionCard,
  RecommendedActionStrip,
  ScoreRangeBadge,
} from "@/Component/OET/Common/OetShared";
import LearnerSettingsWorkspace from "@/Component/OET/Learner/Settings/LearnerSettingsWorkspace";
import { dashboardSummary } from "@/Data/OET/mock";
import { bindReactstrapInput } from "@/lib/forms/reactstrap";
import {
  useContentLibraryQuery,
  useLearnerDashboardQuery,
  useLearnerGoalsQuery,
  useProgressQuery,
  useReadinessQuery,
  useStudyPlanQuery,
  useSubscriptionQuery,
} from "@/lib/oet/queries";
import {
  learnerGoalSchema,
  type LearnerGoalFormValues,
} from "@/lib/oet/schemas";
import { speakingCriteria, writingCriteria } from "@/Data/OET/mock";
import type { OetSubtest } from "@/types/oet";

function formatSubtestLabel(subtest: OetSubtest): string {
  return subtest.charAt(0).toUpperCase() + subtest.slice(1);
}

function LearnerDashboardScreen() {
  const { data, isLoading } = useLearnerDashboardQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Your command center for diagnostics, next tasks, and review signals."
        icon={HomeAlt}
        mainTitle="Dashboard"
        path={["Dashboard"]}
        title="Learner App"
      >
        <Alert color="light">Loading dashboard...</Alert>
      </OetPageShell>
    );
  }

  const latestEvaluation = data.evaluations[0];

  return (
    <OetPageShell
      description="Your command center for diagnostics, next tasks, and review signals."
      icon={HomeAlt}
      mainTitle="Dashboard"
      path={["Dashboard"]}
      title="Learner App"
    >
      <RecommendedActionStrip
        href="/learner/writing/attempt/attempt-writing-1"
        label="Resume Writing"
        summary="Purpose and organisation remain your fastest route to a higher Writing band."
        title="Resume referral letter revision sprint"
      />

      <OetMetricGrid
        items={[
          {
            helper: "Count down to your next booked date.",
            label: "Next exam date",
            value: dashboardSummary.nextExamDate,
          },
          {
            helper: "Keeps the dashboard useful even on low-activity days.",
            label: "Today",
            value: dashboardSummary.todayCompletionTarget,
          },
          {
            helper: "Momentum stays visible without feeling playful.",
            label: "Momentum",
            value: dashboardSummary.streakMomentum,
          },
          {
            helper: "Human review remains available for lower-confidence work.",
            label: "Pending expert reviews",
            value: dashboardSummary.pendingExpertReviews,
          },
        ]}
      />

      <Row>
        <Col xl={8}>
          <OetSectionCard
            description="These are the specific tasks the current plan is prioritizing."
            title="Today's tasks"
          >
            <div className="vstack gap-3">
              {data.studyPlan.today.map((task) => (
                <Card key={task.id} className="border">
                  <CardBody>
                    <div className="d-flex flex-wrap justify-content-between gap-3">
                      <div>
                        <Badge color="light-primary" className="mb-2">
                          {formatSubtestLabel(task.subtest)}
                        </Badge>
                        <h6 className="mb-1">{task.title}</h6>
                        <p className="mb-0 text-secondary">{task.reason}</p>
                      </div>
                      <div className="text-md-end">
                        <p className="mb-1">{task.durationMinutes} min</p>
                        <Button
                          tag={Link}
                          href={
                            task.subtest === "writing"
                              ? "/learner/writing/attempt/attempt-writing-1"
                              : task.subtest === "speaking"
                                ? "/learner/speaking/attempt/attempt-speaking-1"
                                : task.subtest === "reading"
                                  ? "/learner/reading/task/reading-task-1"
                                  : "/learner/listening/task/listening-task-1"
                          }
                          color="primary"
                          size="sm"
                        >
                          Start now
                        </Button>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard
            description="Readiness stays confidence-labeled so estimates do not overpromise."
            title="Latest evaluated submission"
          >
            {latestEvaluation ? (
              <div className="vstack gap-3">
                <h6 className="mb-0">
                  {dashboardSummary.latestEvaluatedSubmission}
                </h6>
                <div className="d-flex flex-wrap gap-2">
                  <ScoreRangeBadge value={latestEvaluation.scoreRange} />
                  <ConfidenceBadge value={latestEvaluation.confidence} />
                </div>
                <p className="mb-0 text-secondary">
                  {latestEvaluation.summary}
                </p>
                <Button
                  tag={Link}
                  href={`/learner/${latestEvaluation.subtest}/result/${latestEvaluation.id}`}
                  color="primary"
                >
                  View latest feedback
                </Button>
              </div>
            ) : (
              <EmptyStateCard
                ctaHref="/learner/diagnostic"
                ctaLabel="Start diagnostic"
                summary="Once your first evaluated task is complete, your latest feedback will surface here."
                title="No evaluated submission yet"
              />
            )}
          </OetSectionCard>
        </Col>
      </Row>

      <Row>
        <Col lg={6}>
          <OetSectionCard
            description="Criterion-first feedback stays front and center."
            title="Weak criteria"
          >
            <div className="vstack gap-2">
              {[
                "Purpose needs to appear earlier in writing openings.",
                "Speaking empathy markers drop when the task becomes more instructional.",
                "Reading Part A accuracy weakens during final-minute scanning.",
              ].map((item) => (
                <Alert key={item} color="light-warning" className="mb-0">
                  {item}
                </Alert>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col lg={6}>
          <OetSectionCard
            description="The next full mock stays visible without overwhelming the daily plan."
            title="Next mock recommendation"
          >
            <KeyValueList
              items={[
                {
                  label: "Recommended mock",
                  value: dashboardSummary.nextMockRecommendation,
                },
                {
                  label: "Weakest link",
                  value: data.readiness.weakestLink,
                },
                {
                  label: "Remaining study hours",
                  value: `${data.readiness.remainingStudyHours}h`,
                },
              ]}
            />
            <Button
              tag={Link}
              href="/learner/mocks/mock-1"
              color="primary"
              className="mt-3"
            >
              Open mock report
            </Button>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function OnboardingScreen() {
  return (
    <OetPageShell
      action={{ href: "/learner/goals", label: "Begin setup" }}
      description="Set expectations for the diagnostic, study plan, and profession-specific OET workflow."
      icon={ClipboardCheck}
      mainTitle="Onboarding"
      path={["Onboarding"]}
      title="Learner App"
    >
      <Row>
        {[
          {
            body: "You will complete a profession-aware diagnostic before the plan adapts.",
            title: "Diagnostic first",
          },
          {
            body: "Writing and Speaking stay criterion-first, not generic language scoring.",
            title: "Criterion-based feedback",
          },
          {
            body: "Every plan item shows duration and why it is recommended.",
            title: "Time-poor learner UX",
          },
        ].map((item, index) => (
          <Col lg={4} key={item.title}>
            <Card className="h-100">
              <CardBody>
                <Badge color="light-primary" className="mb-3">
                  Step {index + 1}
                </Badge>
                <h5>{item.title}</h5>
                <p className="text-secondary mb-0">{item.body}</p>
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function GoalsScreen() {
  const { data, isLoading } = useLearnerGoalsQuery();
  const [saved, setSaved] = useState(false);

  const form = useForm<LearnerGoalFormValues>({
    defaultValues: {
      examDate: "",
      previousAttempts: "",
      professionId: "",
      targetCountry: "",
      targetListening: "",
      targetReading: "",
      targetSpeaking: "",
      targetWriting: "",
      weakSubtests: [],
      weeklyStudyHours: 6,
    },
    resolver: zodResolver(learnerGoalSchema),
  });

  useEffect(() => {
    if (!data) {
      return;
    }

    form.reset({
      examDate: data.goal.examDate ?? "",
      previousAttempts: data.goal.previousAttempts.join(", "),
      professionId: data.goal.professionId,
      targetCountry: data.goal.targetCountry ?? "",
      targetListening: data.goal.subtestTargets.listening ?? "",
      targetReading: data.goal.subtestTargets.reading ?? "",
      targetSpeaking: data.goal.subtestTargets.speaking ?? "",
      targetWriting: data.goal.subtestTargets.writing ?? "",
      weakSubtests: data.goal.weakSubtests,
      weeklyStudyHours: data.goal.weeklyStudyHours,
    });
  }, [data, form]);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Profession, target date, score goals, and study hours feed the initial plan."
        icon={Calendar}
        mainTitle="Goal Setup"
        path={["Goal Setup"]}
        title="Learner App"
      >
        <Alert color="light">Loading goal setup...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Profession, target date, score goals, and study hours feed the initial plan."
      icon={Calendar}
      mainTitle="Goal Setup"
      path={["Goal Setup"]}
      title="Learner App"
    >
      {saved ? (
        <Alert color="success">
          Goal preferences saved. The study plan can now regenerate from this
          target profile.
        </Alert>
      ) : null}
      <Row>
        <Col xl={8}>
          <Card>
            <CardHeader>
              <h5 className="mb-1">Exam profile</h5>
              <p className="mb-0 text-secondary">
                Partial progress can be saved, and sub-test targets remain
                optional but recommended.
              </p>
            </CardHeader>
            <CardBody>
              <Form
                className="app-form"
                onSubmit={form.handleSubmit(() => {
                  setSaved(true);
                })}
              >
                <Row>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="professionId">Profession</Label>
                      <Input
                        id="professionId"
                        type="select"
                        {...bindReactstrapInput(form.register("professionId"))}
                        invalid={!!form.formState.errors.professionId}
                      >
                        <option value="">Select profession</option>
                        {data.professions.map((profession) => (
                          <option key={profession.id} value={profession.id}>
                            {profession.label}
                          </option>
                        ))}
                      </Input>
                      <FormFeedback>
                        {form.formState.errors.professionId?.message}
                      </FormFeedback>
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="examDate">Target exam date</Label>
                      <Input
                        id="examDate"
                        type="date"
                        {...bindReactstrapInput(form.register("examDate"))}
                      />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="targetWriting">Writing target</Label>
                      <Input
                        id="targetWriting"
                        {...bindReactstrapInput(form.register("targetWriting"))}
                        placeholder="e.g. 350-400"
                      />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="targetSpeaking">Speaking target</Label>
                      <Input
                        id="targetSpeaking"
                        {...bindReactstrapInput(
                          form.register("targetSpeaking")
                        )}
                        placeholder="e.g. 360-410"
                      />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="targetReading">Reading target</Label>
                      <Input
                        id="targetReading"
                        {...bindReactstrapInput(form.register("targetReading"))}
                        placeholder="e.g. 370-420"
                      />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="targetListening">Listening target</Label>
                      <Input
                        id="targetListening"
                        {...bindReactstrapInput(
                          form.register("targetListening")
                        )}
                        placeholder="e.g. 380-430"
                      />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="weeklyStudyHours">Study hours per week</Label>
                      <Input
                        id="weeklyStudyHours"
                        type="number"
                        {...bindReactstrapInput(
                          form.register("weeklyStudyHours", {
                            valueAsNumber: true,
                          })
                        )}
                        invalid={!!form.formState.errors.weeklyStudyHours}
                      />
                      <FormFeedback>
                        {form.formState.errors.weeklyStudyHours?.message}
                      </FormFeedback>
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="targetCountry">
                        Target country or organization
                      </Label>
                      <Input
                        id="targetCountry"
                        {...bindReactstrapInput(form.register("targetCountry"))}
                        placeholder="e.g. Australia"
                      />
                    </FormGroup>
                  </Col>
                </Row>
                <div className="d-flex justify-content-end gap-2">
                  <Button
                    type="button"
                    color="light-secondary"
                    onClick={() => form.reset()}
                  >
                    Reset
                  </Button>
                  <Button type="submit" color="primary">
                    Save goals
                  </Button>
                </div>
              </Form>
            </CardBody>
          </Card>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Why these fields matter">
            <div className="vstack gap-3">
              <Alert color="light-primary" className="mb-0">
                Profession selection changes Writing and Speaking task
                inventories.
              </Alert>
              <Alert color="light-primary" className="mb-0">
                Weekly study hours drive the intensity recommendation and
                checkpoint cadence.
              </Alert>
              <Alert color="light-primary" className="mb-0">
                Sub-test targets stay independent because OET reports scores
                separately by skill.
              </Alert>
            </div>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function DiagnosticScreen() {
  return (
    <OetPageShell
      action={{
        href: "/learner/diagnostic/results",
        label: "Open last results",
      }}
      description="A four-part diagnostic estimates readiness and shapes the first study week."
      icon={ClipboardCheck}
      mainTitle="Diagnostic"
      path={["Diagnostic"]}
      title="Learner App"
    >
      <RecommendedActionStrip
        href="/learner/writing/tasks/writing-task-1"
        label="Start Writing diagnostic"
        summary="The first learner flow begins with a profession-specific Writing task and clear confidence messaging."
        title="Training estimate, not an official OET score"
      />
      <Row>
        {[
          {
            href: "/learner/writing/tasks/writing-task-1",
            summary:
              "Case notes left, editor center, timer and checklist right.",
            title: "Writing diagnostic",
          },
          {
            href: "/learner/speaking/task/speaking-task-1",
            summary: "Mic check, role card, and async transcript flow.",
            title: "Speaking diagnostic",
          },
          {
            href: "/learner/reading/task/reading-task-1",
            summary: "Timed item navigation with answer persistence.",
            title: "Reading diagnostic",
          },
          {
            href: "/learner/listening/task/listening-task-1",
            summary: "Stable audio controls and mobile-safe playback behavior.",
            title: "Listening diagnostic",
          },
        ].map((item) => (
          <Col lg={6} key={item.title}>
            <Card className="h-100">
              <CardBody>
                <h5>{item.title}</h5>
                <p className="text-secondary">{item.summary}</p>
                <Button tag={Link} href={item.href} color="primary">
                  Open flow
                </Button>
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function DiagnosticResultsScreen() {
  const { data, isLoading } = useReadinessQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Actionable first-plan output with confidence-labeled estimates."
        icon={WarningTriangle}
        mainTitle="Diagnostic Results"
        path={["Diagnostic", "Results"]}
        title="Learner App"
      >
        <Alert color="light">Loading diagnostic results...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      action={{ href: "/learner/study-plan", label: "Open study plan" }}
      description="Actionable first-plan output with confidence-labeled estimates."
      icon={WarningTriangle}
      mainTitle="Diagnostic Results"
      path={["Diagnostic", "Results"]}
      title="Learner App"
    >
      <OetMetricGrid
        items={[
          { label: "Overall message", value: data.overallLabel },
          { label: "Weakest link", value: data.weakestLink },
          { label: "Remaining study", value: `${data.remainingStudyHours}h` },
          { label: "Target date", value: data.examDate },
        ]}
      />
    </OetPageShell>
  );
}

function StudyPlanScreen() {
  const { data, isLoading } = useStudyPlanQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Tasks stay actionable from the plan itself so the learner does not need extra navigation."
        icon={Calendar}
        mainTitle="Study Plan"
        path={["Study Plan"]}
        title="Learner App"
      >
        <Alert color="light">Loading study plan...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Tasks stay actionable from the plan itself so the learner does not need extra navigation."
      icon={Calendar}
      mainTitle="Study Plan"
      path={["Study Plan"]}
      title="Learner App"
    >
      <RecommendedActionStrip
        href="/learner/writing/attempt/attempt-writing-1"
        label="Start now"
        summary={
          data.today[0]?.reason ?? "Keep the first task clear and visible."
        }
        title={data.today[0]?.title ?? "No task scheduled"}
      />
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Today">
            <div className="vstack gap-3">
              {data.today.map((task) => (
                <Card key={task.id} className="border">
                  <CardBody>
                    <div className="d-flex flex-wrap justify-content-between gap-3">
                      <div>
                        <Badge color="light-primary" className="mb-2">
                          {formatSubtestLabel(task.subtest)}
                        </Badge>
                        <h6 className="mb-1">{task.title}</h6>
                        <p className="mb-0 text-secondary">{task.reason}</p>
                      </div>
                      <div className="text-md-end">
                        <p className="mb-1">{task.dueDate}</p>
                        <div className="d-flex flex-wrap gap-2 justify-content-md-end">
                          <Button color="primary" size="sm">
                            Start now
                          </Button>
                          <Button color="light-secondary" size="sm">
                            Reschedule
                          </Button>
                          <Button color="light-secondary" size="sm">
                            Swap
                          </Button>
                        </div>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Checkpoint">
            <KeyValueList
              items={[
                { label: "Checkpoint", value: data.checkpoint.title },
                { label: "Date", value: data.checkpoint.date },
                { label: "Summary", value: data.checkpoint.summary },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function WritingOverviewScreen() {
  return (
    <OetPageShell
      action={{ href: "/learner/writing/tasks", label: "Open task library" }}
      description="Recommended task, criterion drill library, expert review credits, and past submissions."
      icon={PageEdit}
      mainTitle="Writing"
      path={["Writing"]}
      title="Learner App"
    >
      <RecommendedActionStrip
        href="/learner/writing/tasks/writing-task-1"
        label="Open recommended task"
        summary="Current plan is pushing Purpose and Organisation improvements in a nursing referral letter."
        title="Referral to community wound clinic"
      />
      <Row>
        <Col lg={7}>
          <OetSectionCard title="Writing criteria focus">
            <div className="d-flex flex-wrap gap-2">
              {writingCriteria.map((criterion) => (
                <Badge color="light-primary" key={criterion.id}>
                  {criterion.name}
                </Badge>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col lg={5}>
          <OetSectionCard title="Expert review credits">
            <KeyValueList
              items={[
                { label: "Available credits", value: "3" },
                { label: "Latest request", value: "Referral letter review" },
                { label: "Turnaround", value: "Standard: 24h" },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function WritingLibraryScreen() {
  const { data, isLoading } = useContentLibraryQuery("writing");

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Task cards remain filterable by profession, difficulty, criteria, and mode."
        icon={PageEdit}
        mainTitle="Writing Task Library"
        path={["Writing", "Task Library"]}
        title="Learner App"
      >
        <Alert color="light">Loading Writing task library...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Task cards remain filterable by profession, difficulty, criteria, and mode."
      icon={PageEdit}
      mainTitle="Writing Task Library"
      path={["Writing", "Task Library"]}
      title="Learner App"
    >
      <Row>
        {data.map((task) => (
          <Col lg={6} key={task.id}>
            <Card className="h-100">
              <CardBody>
                <div className="d-flex flex-wrap gap-2 mb-3">
                  <Badge color="light-primary">{task.professionId}</Badge>
                  <Badge color="light-secondary">{task.difficulty}</Badge>
                  <Badge color="light-warning">
                    {task.durationMinutes} min
                  </Badge>
                </div>
                <h5>{task.title}</h5>
                <p className="text-secondary">{task.scenarioType}</p>
                <div className="d-flex flex-wrap gap-2 mb-3">
                  {task.criteriaFocus.map((criterion) => (
                    <Badge key={criterion} color="light-primary">
                      {criterion}
                    </Badge>
                  ))}
                </div>
                <Button
                  tag={Link}
                  href={`/learner/writing/tasks/${task.id}`}
                  color="primary"
                >
                  View task
                </Button>
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function SpeakingOverviewScreen() {
  return (
    <OetPageShell
      action={{
        href: "/learner/speaking/tasks",
        label: "Open role-play library",
      }}
      description="Recommended role-play, common improvement issues, empathy drills, and expert review access."
      icon={MessageText}
      mainTitle="Speaking"
      path={["Speaking"]}
      title="Learner App"
    >
      <RecommendedActionStrip
        href="/learner/speaking/mic-check"
        label="Run mic check"
        summary="Keep the recording path stable before starting the next speaking task."
        title="Post-operative pain discussion"
      />
      <Row>
        <Col lg={7}>
          <OetSectionCard title="Speaking criteria focus">
            <div className="d-flex flex-wrap gap-2">
              {speakingCriteria.map((criterion) => (
                <Badge color="light-primary" key={criterion.id}>
                  {criterion.name}
                </Badge>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col lg={5}>
          <OetSectionCard title="Common issues to improve">
            <div className="vstack gap-2">
              <Alert color="light-warning" className="mb-0">
                Acknowledge patient concern before switching into instructions.
              </Alert>
              <Alert color="light-warning" className="mb-0">
                Ask a checking question before long explanations.
              </Alert>
            </div>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function SpeakingTaskLibraryScreen() {
  const { data, isLoading } = useContentLibraryQuery("speaking");

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Role plays stay filterable by profession, difficulty, criteria focus, and duration."
        icon={MessageText}
        mainTitle="Speaking Task Library"
        path={["Speaking", "Task Library"]}
        title="Learner App"
      >
        <Alert color="light">Loading Speaking tasks...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Role plays stay filterable by profession, difficulty, criteria focus, and duration."
      icon={MessageText}
      mainTitle="Speaking Task Library"
      path={["Speaking", "Task Library"]}
      title="Learner App"
    >
      <Row>
        {data.map((task) => (
          <Col lg={6} key={task.id}>
            <Card className="h-100">
              <CardBody>
                <div className="d-flex flex-wrap gap-2 mb-3">
                  <Badge color="light-primary">{task.professionId}</Badge>
                  <Badge color="light-secondary">{task.difficulty}</Badge>
                </div>
                <h5>{task.title}</h5>
                <p className="text-secondary">{task.scenarioType}</p>
                <Button
                  tag={Link}
                  href={`/learner/speaking/task/${task.id}`}
                  color="primary"
                >
                  Preview role card
                </Button>
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function MicCheckScreen() {
  return (
    <OetPageShell
      action={{
        href: "/learner/speaking/task/speaking-task-1",
        label: "Open speaking task",
      }}
      description="Verify microphone access, recording, playback, and device readiness before the task."
      icon={MessageText}
      mainTitle="Mic Check"
      path={["Speaking", "Mic Check"]}
      title="Learner App"
    >
      <Row>
        {[
          "Microphone permission detected",
          "Recording input detected",
          "Playback confirmed",
          "Noise level acceptable",
        ].map((item, index) => (
          <Col md={6} key={item}>
            <Alert color={index === 3 ? "light-warning" : "light-success"}>
              {item}
            </Alert>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function ReadingOverviewScreen() {
  return (
    <OetPageShell
      action={{
        href: "/learner/reading/task/reading-task-1",
        label: "Start reading task",
      }}
      description="Part A/B/C entry points, speed drills, accuracy drills, explanations, and mock sets."
      icon={Book}
      mainTitle="Reading"
      path={["Reading"]}
      title="Learner App"
    >
      <OetMetricGrid
        items={[
          {
            label: "Current estimate",
            value: "370-410",
            helper: "Reading remains a relative strength.",
          },
          {
            label: "Next drill",
            value: "Part A speed calibration",
            helper: "Focus on faster elimination.",
          },
          {
            label: "Latest issue cluster",
            value: "Keyword scanning drift",
            helper: "Part A only.",
          },
          {
            label: "Mode",
            value: "Practice + exam",
            helper: "Both remain available.",
          },
        ]}
      />
    </OetPageShell>
  );
}

function ListeningOverviewScreen() {
  return (
    <OetPageShell
      action={{
        href: "/learner/listening/task/listening-task-1",
        label: "Start listening task",
      }}
      description="Part-based practice, transcript-backed review, distractor drills, and mock sets."
      icon={Brain}
      mainTitle="Listening"
      path={["Listening"]}
      title="Learner App"
    >
      <OetMetricGrid
        items={[
          {
            label: "Current estimate",
            value: "380-420",
            helper: "Listening is stable.",
          },
          {
            label: "Focus",
            value: "Part C distractors",
            helper: "Late-section summary traps remain.",
          },
          {
            label: "Playback strategy",
            value: "Mobile-safe",
            helper: "Controls stay stable on smaller screens.",
          },
          {
            label: "Next drill",
            value: "Consultant discussion review",
            helper: "Transcript-backed analysis.",
          },
        ]}
      />
    </OetPageShell>
  );
}

function MockCenterScreen() {
  return (
    <OetPageShell
      action={{
        href: "/learner/mocks/mock-1",
        label: "Open latest mock report",
      }}
      description="Sub-test mocks, full mocks, previous reports, and the next recommended simulation."
      icon={Reports}
      mainTitle="Mock Center"
      path={["Mock Center"]}
      title="Learner App"
    >
      <Row>
        <Col lg={4}>
          <OetSectionCard title="Sub-test mocks">
            <p className="text-secondary">
              Writing, Speaking, Reading, and Listening mocks remain available
              independently.
            </p>
          </OetSectionCard>
        </Col>
        <Col lg={4}>
          <OetSectionCard title="Full mocks">
            <p className="text-secondary">
              Use strict timer mode and optionally attach expert review
              requests.
            </p>
          </OetSectionCard>
        </Col>
        <Col lg={4}>
          <OetSectionCard title="Previous reports">
            <p className="text-secondary">
              Comparison summaries surface the weakest criterion and prior
              change.
            </p>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function ReadinessScreen() {
  const { data, isLoading } = useReadinessQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Readiness stays evidence-backed, target-date aware, and confidence-labeled."
        icon={LeaderboardStar}
        mainTitle="Readiness"
        path={["Readiness"]}
        title="Learner App"
      >
        <Alert color="light">Loading readiness...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Readiness stays evidence-backed, target-date aware, and confidence-labeled."
      icon={LeaderboardStar}
      mainTitle="Readiness"
      path={["Readiness"]}
      title="Learner App"
    >
      <OetMetricGrid
        items={[
          {
            label: "Target-date risk",
            value: "Manageable",
            helper: data.overallLabel,
          },
          {
            label: "Weakest link",
            value: data.weakestLink,
            helper: "Primary blocker",
          },
          {
            label: "Study remaining",
            value: `${data.remainingStudyHours}h`,
            helper: "At current intensity",
          },
          {
            label: "Exam date",
            value: data.examDate,
            helper: "Target date",
          },
        ]}
      />
      <Row>
        {data.subtests.map((item) => (
          <Col md={6} key={item.subtest}>
            <OetSectionCard
              title={`${formatSubtestLabel(item.subtest)} evidence`}
            >
              <div className="d-flex flex-wrap gap-2 mb-3">
                <ScoreRangeBadge value={item.scoreRange} />
                <ConfidenceBadge value={item.confidence} />
              </div>
              <p className="mb-0 text-secondary">{item.readinessLabel}</p>
            </OetSectionCard>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function ProgressScreen() {
  const { data, isLoading } = useProgressQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Trend views stay focused on sub-tests, criteria, completion, and submission volume."
        icon={LeaderboardStar}
        mainTitle="Progress"
        path={["Progress"]}
        title="Learner App"
      >
        <Alert color="light">Loading progress...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Trend views stay focused on sub-tests, criteria, completion, and submission volume."
      icon={LeaderboardStar}
      mainTitle="Progress"
      path={["Progress"]}
      title="Learner App"
    >
      <Row>
        {[
          { title: "Sub-test trend", rows: data.subtestTrend },
          { title: "Criterion trend", rows: data.criterionTrend },
          { title: "Completion trend", rows: data.completionTrend },
          { title: "Submission volume", rows: data.submissionVolume },
        ].map((section) => (
          <Col xl={6} key={section.title}>
            <OetSectionCard title={section.title}>
              <KeyValueList
                items={section.rows.map((row) => ({
                  label: row.label,
                  value: row.value,
                }))}
              />
            </OetSectionCard>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function ReviewsScreen() {
  const { data, isLoading } = useLearnerDashboardQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Review requests preserve the full learner context, AI findings, and turnaround status."
        icon={UserScan}
        mainTitle="Reviews"
        path={["Reviews"]}
        title="Learner App"
      >
        <Alert color="light">Loading review requests...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Review requests preserve the full learner context, AI findings, and turnaround status."
      icon={UserScan}
      mainTitle="Reviews"
      path={["Reviews"]}
      title="Learner App"
    >
      <CustomDataTable
        columns={[
          { key: "id", header: "Review ID" },
          { key: "subtest", header: "Sub-test" },
          { key: "priority", header: "Priority" },
          { key: "status", header: "Status" },
          { key: "dueAt", header: "Due" },
        ]}
        data={data.reviews}
        description="Writing and Speaking review requests stay visible with status and due time."
        onEdit={(item) => {
          window.location.href =
            item.subtest === "writing"
              ? "/learner/writing/result/eval-writing-1"
              : "/learner/speaking/review/attempt-speaking-1";
        }}
        title="Review requests"
      />
    </OetPageShell>
  );
}

function HistoryScreen() {
  const rows = [
    {
      attemptDate: "22 Mar 2026",
      reviewStatus: "In review",
      scoreEstimate: "350-390",
      subtest: "Writing",
      taskName: "Referral to community wound clinic",
    },
    {
      attemptDate: "22 Mar 2026",
      reviewStatus: "Queued",
      scoreEstimate: "340-380",
      subtest: "Speaking",
      taskName: "Post-operative pain discussion",
    },
    {
      attemptDate: "21 Mar 2026",
      reviewStatus: "Completed",
      scoreEstimate: "370-410",
      subtest: "Reading",
      taskName: "Emergency triage quick scan",
    },
  ];

  return (
    <OetPageShell
      description="Submission history supports reopening feedback, comparing attempts, and requesting reviews."
      icon={Reports}
      mainTitle="Submission History"
      path={["Submission History"]}
      title="Learner App"
    >
      <CustomDataTable
        columns={[
          { key: "taskName", header: "Task" },
          { key: "subtest", header: "Sub-test" },
          { key: "attemptDate", header: "Attempt date" },
          { key: "scoreEstimate", header: "Score estimate" },
          { key: "reviewStatus", header: "Review status" },
        ]}
        data={rows}
        description="Latest submissions across all OET sub-tests."
        onEdit={(item) => {
          window.location.href =
            item.subtest === "Writing"
              ? "/learner/writing/result/eval-writing-1"
              : item.subtest === "Speaking"
                ? "/learner/speaking/review/attempt-speaking-1"
                : item.subtest === "Reading"
                  ? "/learner/reading/task/reading-task-1"
                  : "/learner/listening/task/listening-task-1";
        }}
        title="Submission history"
      />
    </OetPageShell>
  );
}

function BillingScreen() {
  const { data, isLoading } = useSubscriptionQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Plan, renewal, credits, invoices, and extra review purchase options all remain visible."
        icon={Dollar}
        mainTitle="Billing"
        path={["Billing"]}
        title="Learner App"
      >
        <Alert color="light">Loading billing...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Plan, renewal, credits, invoices, and extra review purchase options all remain visible."
      icon={Dollar}
      mainTitle="Billing"
      path={["Billing"]}
      title="Learner App"
    >
      <OetMetricGrid
        items={[
          {
            label: "Current plan",
            value: data.subscription.currentPlan,
            helper: "Active subscription tier",
          },
          {
            label: "Next renewal",
            value: data.subscription.renewalDate,
            helper: "Billing date",
          },
          {
            label: "Review credits",
            value: data.wallet.available,
            helper: "Available now",
          },
          {
            label: "Reserved credits",
            value: data.wallet.reserved,
            helper: "Pending usage",
          },
        ]}
      />
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Invoices">
            <KeyValueList
              items={data.subscription.invoices.map((invoice) => ({
                label: `${invoice.id} · ${invoice.issuedAt}`,
                value: `${invoice.amountLabel} · ${invoice.status}`,
              }))}
            />
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Upgrade options">
            <div className="vstack gap-2">
              <Button color="primary">Upgrade plan</Button>
              <Button color="light-secondary">Purchase extra reviews</Button>
            </div>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function SettingsScreen() {
  return (
    <OetPageShell
      description="Profile, goals, notifications, privacy, accessibility, low-bandwidth mode, and audio preferences."
      icon={Settings}
      layoutMode="lean"
      mainTitle="Settings"
      path={["Settings"]}
      title="Learner App"
    >
      <LearnerSettingsWorkspace />
    </OetPageShell>
  );
}

const learnerStaticRoutes = new Set([
  "",
  "dashboard",
  "onboarding",
  "goals",
  "diagnostic",
  "diagnostic/results",
  "study-plan",
  "writing",
  "writing/tasks",
  "speaking",
  "speaking/tasks",
  "speaking/mic-check",
  "reading",
  "listening",
  "mocks",
  "readiness",
  "progress",
  "reviews",
  "history",
  "billing",
  "settings",
]);

export function isLearnerStaticRoute(slug?: string[]): boolean {
  return learnerStaticRoutes.has(slug?.join("/") ?? "");
}

export function LearnerStaticPage({ slug }: { slug?: string[] }) {
  switch (slug?.join("/") ?? "") {
    case "":
    case "dashboard":
      return <LearnerDashboardScreen />;
    case "onboarding":
      return <OnboardingScreen />;
    case "goals":
      return <GoalsScreen />;
    case "diagnostic":
      return <DiagnosticScreen />;
    case "diagnostic/results":
      return <DiagnosticResultsScreen />;
    case "study-plan":
      return <StudyPlanScreen />;
    case "writing":
      return <WritingOverviewScreen />;
    case "writing/tasks":
      return <WritingLibraryScreen />;
    case "speaking":
      return <SpeakingOverviewScreen />;
    case "speaking/tasks":
      return <SpeakingTaskLibraryScreen />;
    case "speaking/mic-check":
      return <MicCheckScreen />;
    case "reading":
      return <ReadingOverviewScreen />;
    case "listening":
      return <ListeningOverviewScreen />;
    case "mocks":
      return <MockCenterScreen />;
    case "readiness":
      return <ReadinessScreen />;
    case "progress":
      return <ProgressScreen />;
    case "reviews":
      return <ReviewsScreen />;
    case "history":
      return <HistoryScreen />;
    case "billing":
      return <BillingScreen />;
    case "settings":
      return <SettingsScreen />;
    default:
      return (
        <OetPageShell
          description="This learner route is not mapped yet."
          icon={HomeAlt}
          mainTitle="Dashboard"
          path={["Dashboard"]}
          title="Learner App"
        >
          <EmptyStateCard
            ctaHref="/learner/dashboard"
            ctaLabel="Back to dashboard"
            summary="The requested learner screen could not be resolved."
            title="Unknown learner route"
          />
        </OetPageShell>
      );
  }
}
