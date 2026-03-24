"use client";

import {
  Activity,
  Calendar,
  MenuScale,
  Shield,
  UserCircle,
  XrayView,
} from "iconoir-react";
import { Alert, Badge, Card, CardBody, Col, Row } from "reactstrap";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";
import {
  KeyValueList,
  OetMetricGrid,
  OetPageShell,
  OetSectionCard,
  WaveformBars,
} from "@/Component/OET/Common/OetShared";
import {
  assignedLearners,
  calibrationCases,
  expertMetrics,
} from "@/Data/OET/mock";
import {
  useExpertQueueQuery,
  useExpertWorkspaceQuery,
} from "@/lib/oet/queries";

function QueueScreen() {
  const { data, isLoading } = useExpertQueueQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Dense queue view with filters, priority, AI confidence, and SLA visibility."
        icon={MenuScale}
        mainTitle="Review Queue"
        path={["Review Queue"]}
        title="Expert Console"
      >
        <Alert color="light">Loading review queue...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Dense queue view with filters, priority, AI confidence, and SLA visibility."
      icon={MenuScale}
      mainTitle="Review Queue"
      path={["Review Queue"]}
      title="Expert Console"
    >
      <CustomDataTable
        columns={[
          { key: "reviewId", header: "Review ID" },
          { key: "learner", header: "Learner" },
          { key: "profession", header: "Profession" },
          { key: "subtest", header: "Sub-test" },
          { key: "aiConfidence", header: "AI confidence" },
          { key: "priority", header: "Priority" },
          { key: "slaDue", header: "SLA due" },
          { key: "status", header: "Status" },
        ]}
        data={data}
        description="Filters remain visible by default in the final integrated console; this mock view focuses on the core queue data."
        onEdit={(item) => {
          window.location.href =
            item.subtest === "Writing"
              ? `/reviewer/review/writing/${item.reviewId}`
              : `/reviewer/review/speaking/${item.reviewId}`;
        }}
        title="Assigned review queue"
      />
    </OetPageShell>
  );
}

function CalibrationScreen() {
  return (
    <OetPageShell
      description="Benchmark cases, reviewer alignment, and disagreement notes."
      icon={Shield}
      mainTitle="Calibration"
      path={["Calibration"]}
      title="Expert Console"
    >
      <Row>
        {calibrationCases.map((item) => (
          <Col md={6} key={item.caseId}>
            <OetSectionCard title={item.caseId}>
              <KeyValueList
                items={[
                  { label: "Learner", value: item.learner },
                  { label: "Benchmark", value: item.benchmark },
                  { label: "Alignment", value: item.reviewerAlignment },
                ]}
              />
              <p className="text-secondary mt-3 mb-0">{item.note}</p>
            </OetSectionCard>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function MetricsScreen() {
  return (
    <OetPageShell
      description="Reviewer performance and throughput metrics."
      icon={Activity}
      mainTitle="Metrics"
      path={["Metrics"]}
      title="Expert Console"
    >
      <OetMetricGrid
        items={[
          { label: "Cases this week", value: expertMetrics.casesThisWeek },
          {
            label: "Average turnaround",
            value: expertMetrics.averageTurnaround,
          },
          {
            label: "Calibration alignment",
            value: expertMetrics.calibrationAlignment,
          },
          { label: "Overdue cases", value: expertMetrics.overdueCases },
        ]}
      />
    </OetPageShell>
  );
}

function ScheduleScreen() {
  return (
    <OetPageShell
      description="Availability and scheduled review blocks."
      icon={Calendar}
      mainTitle="Schedule"
      path={["Schedule"]}
      title="Expert Console"
    >
      <Row>
        {[
          "Monday 09:00-13:00",
          "Wednesday 14:00-18:00",
          "Saturday 10:00-14:00",
        ].map((slot) => (
          <Col md={4} key={slot}>
            <Card>
              <CardBody>
                <h6>{slot}</h6>
                <p className="text-secondary mb-0">
                  Available for standard and priority review coverage.
                </p>
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </OetPageShell>
  );
}

function WritingReviewScreen({ reviewRequestId }: { reviewRequestId: string }) {
  const { data, isLoading } = useExpertWorkspaceQuery(reviewRequestId);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Split workspace for case notes, learner response, AI draft feedback, rubric, and final comment."
        icon={XrayView}
        mainTitle="Writing Review"
        path={["Review Queue", "Writing Review"]}
        title="Expert Console"
      >
        <Alert color="light">Loading writing review workspace...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Split workspace for case notes, learner response, AI draft feedback, rubric, and final comment."
      icon={XrayView}
      mainTitle="Writing Review"
      path={["Review Queue", "Writing Review"]}
      title="Expert Console"
    >
      <Row>
        <Col xl={4}>
          <OetSectionCard title="Submission context">
            <KeyValueList
              items={[
                {
                  label: "Review ID",
                  value: data.review?.id ?? reviewRequestId,
                },
                {
                  label: "Learner",
                  value: data.review?.learnerId ?? "Unknown",
                },
                { label: "Task", value: data.content?.title ?? "Writing task" },
              ]}
            />
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Learner response">
            <p className="text-secondary mb-0">
              Dear Community Wound Team, I am writing to refer Mrs Peters for
              ongoing wound review following discharge...
            </p>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Rubric and final comment">
            <div className="vstack gap-2">
              <Badge color="light-primary">Purpose</Badge>
              <Badge color="light-primary">Content</Badge>
              <Badge color="light-primary">Organisation</Badge>
              <Alert color="light-warning" className="mb-0">
                Save draft review before sending final comments.
              </Alert>
            </div>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function SpeakingReviewScreen({
  reviewRequestId,
}: {
  reviewRequestId: string;
}) {
  const { data, isLoading } = useExpertWorkspaceQuery(reviewRequestId);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Role card, audio/waveform, transcript, AI flags, and rubric in one review workspace."
        icon={XrayView}
        mainTitle="Speaking Review"
        path={["Review Queue", "Speaking Review"]}
        title="Expert Console"
      >
        <Alert color="light">Loading speaking review workspace...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Role card, audio/waveform, transcript, AI flags, and rubric in one review workspace."
      icon={XrayView}
      mainTitle="Speaking Review"
      path={["Review Queue", "Speaking Review"]}
      title="Expert Console"
    >
      <Row>
        <Col xl={4}>
          <OetSectionCard title="Role card and rubric">
            <KeyValueList
              items={[
                {
                  label: "Review ID",
                  value: data.review?.id ?? reviewRequestId,
                },
                {
                  label: "Task",
                  value: data.content?.title ?? "Speaking task",
                },
                {
                  label: "Priority",
                  value: data.review?.priority ?? "standard",
                },
              ]}
            />
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Waveform and transcript">
            <WaveformBars
              bars={[18, 28, 34, 20, 30, 24, 38, 26]}
              currentIndex={3}
            />
            <div className="vstack gap-2 mt-3">
              {data.transcript.slice(0, 2).map((segment) => (
                <Alert color="light-warning" key={segment.start}>
                  {segment.start}: {segment.issue}
                </Alert>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="AI and human notes">
            <Alert color="light-warning">
              Timestamp anchoring and playback-speed controls stay in this pane.
            </Alert>
            <Alert color="light-primary" className="mb-0">
              Final human response remains distinct from AI flags.
            </Alert>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function LearnerContextScreen({ learnerId }: { learnerId: string }) {
  const learner = assignedLearners.find((item) => item.id === learnerId);

  return (
    <OetPageShell
      description="Reviewer-facing learner context required for fair and fast decisions."
      icon={UserCircle}
      mainTitle={learner?.fullName ?? "Assigned learner"}
      path={["Assigned Learners", learner?.fullName ?? "Learner"]}
      title="Expert Console"
    >
      <OetSectionCard title="Learner context">
        <KeyValueList
          items={[
            { label: "Learner ID", value: learner?.id ?? learnerId },
            { label: "Profession", value: learner?.professionId ?? "Unknown" },
            { label: "Email", value: learner?.email ?? "Unknown" },
          ]}
        />
      </OetSectionCard>
    </OetPageShell>
  );
}

const expertStaticRoutes = new Set([
  "",
  "queue",
  "calibration",
  "metrics",
  "schedule",
]);

export function isExpertStaticRoute(slug?: string[]): boolean {
  return expertStaticRoutes.has(slug?.join("/") ?? "");
}

export function ExpertStaticPage({ slug }: { slug?: string[] }) {
  switch (slug?.join("/") ?? "") {
    case "":
    case "queue":
      return <QueueScreen />;
    case "calibration":
      return <CalibrationScreen />;
    case "metrics":
      return <MetricsScreen />;
    case "schedule":
      return <ScheduleScreen />;
    default:
      return <QueueScreen />;
  }
}

export const ExpertWritingReviewPage = WritingReviewScreen;
export const ExpertSpeakingReviewPage = SpeakingReviewScreen;
export const ExpertLearnerPage = LearnerContextScreen;
