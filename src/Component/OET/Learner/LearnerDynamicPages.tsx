"use client";

import { useEffect, useState } from "react";
import { Book, Brain, MessageText, PageEdit, Reports } from "iconoir-react";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  FormGroup,
  Input,
  Label,
  Row,
} from "reactstrap";
import {
  AsyncStateAlert,
  ConfidenceBadge,
  KeyValueList,
  OetPageShell,
  OetSectionCard,
  ScoreRangeBadge,
  StickyActionBar,
  WaveformBars,
} from "@/Component/OET/Common/OetShared";
import { mockReports } from "@/Data/OET/mock";
import {
  useAttemptDetailQuery,
  useContentLibraryQuery,
  useEvaluationDetailQuery,
  useSpeakingReviewQuery,
  useWritingDetailQuery,
} from "@/lib/oet/queries";
import { useWritingDraftStore } from "@/lib/oet/stores/writing-draft-store";

function WritingTaskDetailScreen({ taskId }: { taskId: string }) {
  const { data, isLoading } = useWritingDetailQuery(taskId);

  if (isLoading || !data?.content) {
    return (
      <OetPageShell
        description="Preview the task before entering the timed writing player."
        icon={PageEdit}
        mainTitle="Writing Task"
        path={["Writing", "Task Detail"]}
        title="Learner App"
      >
        <Alert color="light">Loading Writing task...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      action={{
        href: "/app/writing/attempt/attempt-writing-1",
        label: "Start attempt",
      }}
      description="Preview the task before entering the timed writing player."
      icon={PageEdit}
      mainTitle={data.content.title}
      path={["Writing", "Task Detail"]}
      title="Learner App"
    >
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Case notes">
            <ul className="mb-0">
              {data.caseNotes.map((item) => (
                <li key={item} className="mb-2">
                  {item}
                </li>
              ))}
            </ul>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Task metadata">
            <KeyValueList
              items={[
                {
                  label: "Profession",
                  value: data.content.professionId ?? "multi",
                },
                { label: "Difficulty", value: data.content.difficulty },
                {
                  label: "Duration",
                  value: `${data.content.durationMinutes} min`,
                },
                {
                  label: "Criteria focus",
                  value: data.content.criteriaFocus.join(", "),
                },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function WritingAttemptScreen({ attemptId }: { attemptId: string }) {
  const { data: attempt, isLoading } = useAttemptDetailQuery(attemptId);
  const { data: writingDetail } = useWritingDetailQuery("writing-task-1");
  const [ReactQuill, setReactQuill] = useState<any>(null);
  const [saveState, setSaveState] = useState<"saved" | "saving">("saved");
  const [fontSize, setFontSize] = useState("16");
  const [distractionFree, setDistractionFree] = useState(false);
  const drafts = useWritingDraftStore((state) => state.drafts);
  const saveDraft = useWritingDraftStore((state) => state.saveDraft);
  const clearDraft = useWritingDraftStore((state) => state.clearDraft);
  const starterValue =
    drafts[attemptId] ??
    "Dear Community Wound Team,\n\nI am writing to refer Mrs Peters for follow-up wound management after discharge...";
  const [content, setContent] = useState(starterValue);

  useEffect(() => {
    Promise.all([import("react-quill-new")]).then(([quill]) => {
      setReactQuill(() => quill.default);
    });
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      saveDraft(attemptId, content);
      setSaveState("saved");
    }, 900);
    setSaveState("saving");
    return () => window.clearTimeout(timer);
  }, [attemptId, content, saveDraft]);

  useEffect(() => {
    const handleUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };

    window.addEventListener("beforeunload", handleUnload);
    return () => {
      window.removeEventListener("beforeunload", handleUnload);
    };
  }, []);

  if (isLoading || !attempt || !ReactQuill) {
    return (
      <OetPageShell
        description="Case notes left, editor center, timer/checklist right, with full mobile authoring support."
        icon={PageEdit}
        mainTitle="Writing Attempt"
        path={["Writing", "Attempt"]}
        title="Learner App"
      >
        <Alert color="light">Loading writing attempt...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Case notes left, editor center, timer/checklist right, with full mobile authoring support."
      icon={PageEdit}
      mainTitle="Writing Attempt"
      path={["Writing", "Attempt"]}
      title="Learner App"
    >
      <Row className={distractionFree ? "justify-content-center" : ""}>
        {!distractionFree ? (
          <Col xl={3}>
            <OetSectionCard title="Case notes">
              <ul className="mb-0">
                {writingDetail?.caseNotes.map((item) => (
                  <li key={item} className="mb-2">
                    {item}
                  </li>
                ))}
              </ul>
            </OetSectionCard>
          </Col>
        ) : null}
        <Col xl={distractionFree ? 12 : 6}>
          <Card>
            <CardHeader className="d-flex flex-wrap justify-content-between gap-2 align-items-center">
              <div>
                <h5 className="mb-1">Response editor</h5>
                <p className="mb-0 text-secondary">
                  Autosave is active. Current draft state: {saveState}.
                </p>
              </div>
              <div className="d-flex flex-wrap gap-2">
                <Input
                  type="select"
                  value={fontSize}
                  onChange={(event) => setFontSize(event.target.value)}
                  style={{ width: "100px" }}
                >
                  <option value="14">14px</option>
                  <option value="16">16px</option>
                  <option value="18">18px</option>
                </Input>
                <Button
                  color="light-secondary"
                  onClick={() => setDistractionFree((current) => !current)}
                >
                  {distractionFree ? "Exit focus" : "Distraction-free"}
                </Button>
              </div>
            </CardHeader>
            <CardBody style={{ fontSize: `${fontSize}px` }}>
              <ReactQuill
                className="trumbowyg-box custom_editor h-300"
                modules={{
                  toolbar: [
                    ["bold", "italic", "underline"],
                    [{ list: "bullet" }],
                    ["clean"],
                  ],
                }}
                onChange={setContent}
                value={content}
              />
            </CardBody>
          </Card>
        </Col>
        {!distractionFree ? (
          <Col xl={3}>
            <OetSectionCard title="Task controls">
              <KeyValueList
                items={[
                  { label: "Mode", value: attempt.mode },
                  { label: "Status", value: attempt.status },
                  { label: "Timer", value: "31 min remaining" },
                  { label: "Draft", value: saveState },
                ]}
              />
              <div className="vstack gap-2 mt-3">
                <Alert color="light-warning" className="mb-0">
                  Check that the opening sentence makes the referral purpose
                  explicit.
                </Alert>
                <Alert color="light-warning" className="mb-0">
                  Keep unnecessary dressing chronology out of the final letter.
                </Alert>
              </div>
            </OetSectionCard>
          </Col>
        ) : null}
      </Row>
      <StickyActionBar
        actions={[
          {
            href: "/app/writing/result/eval-writing-1",
            label: "Submit attempt",
          },
          {
            href: "/app/writing/revision/attempt-writing-1",
            label: "Open revision mode",
          },
        ]}
      />
      <Button
        color="link"
        className="mt-2"
        onClick={() => clearDraft(attemptId)}
      >
        Clear saved draft
      </Button>
    </OetPageShell>
  );
}

function WritingResultScreen({ evaluationId }: { evaluationId: string }) {
  const { data, isLoading } = useEvaluationDetailQuery(evaluationId);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Criterion-first summary and detailed feedback for the latest writing evaluation."
        icon={PageEdit}
        mainTitle="Writing Results"
        path={["Writing", "Results"]}
        title="Learner App"
      >
        <Alert color="light">Loading evaluation...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Criterion-first summary and detailed feedback for the latest writing evaluation."
      icon={PageEdit}
      mainTitle="Writing Results"
      path={["Writing", "Results"]}
      title="Learner App"
    >
      <AsyncStateAlert status={data.status} title="Writing evaluation status" />
      <Card className="mb-4">
        <CardBody>
          <div className="d-flex flex-wrap gap-2 mb-3">
            <ScoreRangeBadge value={data.scoreRange} />
            <ConfidenceBadge value={data.confidence} />
          </div>
          <p className="mb-0 text-secondary">{data.summary}</p>
        </CardBody>
      </Card>
      <Row>
        {data.criterionScores.map((criterion) => (
          <Col xl={6} key={criterion.criterionId}>
            <OetSectionCard title={criterion.criterionId}>
              <p className="text-secondary">{criterion.summary}</p>
              <Badge color="light-primary" className="mb-2">
                Band {criterion.scoreBand}
              </Badge>
              <ul>
                {criterion.improvements.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </OetSectionCard>
          </Col>
        ))}
      </Row>
      <StickyActionBar
        actions={[
          {
            href: "/app/writing/revision/attempt-writing-1",
            label: "Revise this letter",
          },
          {
            href: "/app/reviews",
            label: "Request expert review",
          },
        ]}
      />
    </OetPageShell>
  );
}

function WritingRevisionScreen({ attemptId }: { attemptId: string }) {
  const drafts = useWritingDraftStore((state) => state.drafts);
  const currentDraft = drafts[attemptId] ?? "";

  return (
    <OetPageShell
      description="Compare the original draft with a revised response and a criterion delta summary."
      icon={PageEdit}
      mainTitle="Writing Revision"
      path={["Writing", "Revision Mode"]}
      title="Learner App"
    >
      <Row>
        <Col xl={6}>
          <OetSectionCard title="Original response">
            <p className="text-secondary">
              Dear Community Wound Team, I am writing regarding Mrs Peters who
              was discharged following lower-leg wound care...
            </p>
          </OetSectionCard>
        </Col>
        <Col xl={6}>
          <OetSectionCard title="Current revision">
            <p className="text-secondary">
              {currentDraft ||
                "Your latest saved revision will appear here after editing."}
            </p>
          </OetSectionCard>
        </Col>
      </Row>
      <Row>
        <Col lg={8}>
          <OetSectionCard title="Revision diff summary">
            <div className="vstack gap-2">
              <Alert color="light-success" className="mb-0">
                Opening purpose is now explicit in the first line.
              </Alert>
              <Alert color="light-warning" className="mb-0">
                Social context still appears before the requested action
                summary.
              </Alert>
            </div>
          </OetSectionCard>
        </Col>
        <Col lg={4}>
          <OetSectionCard title="Criterion delta">
            <KeyValueList
              items={[
                { label: "Purpose", value: "+1 band potential" },
                { label: "Content", value: "Needs more trimming" },
                { label: "Organisation", value: "Improved, not locked yet" },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function WritingModelAnswerScreen({ contentId }: { contentId: string }) {
  const { data, isLoading } = useWritingDetailQuery(contentId);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Annotated model answer with paragraph-level rationale and criterion mapping."
        icon={PageEdit}
        mainTitle="Model Answer"
        path={["Writing", "Model Answer"]}
        title="Learner App"
      >
        <Alert color="light">Loading model answer...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Annotated model answer with paragraph-level rationale and criterion mapping."
      icon={PageEdit}
      mainTitle="Model Answer"
      path={["Writing", "Model Answer"]}
      title="Learner App"
    >
      <Row>
        <Col xl={7}>
          <OetSectionCard title="Annotated model answer">
            <p className="text-secondary">
              Dear Community Wound Team, I am writing to refer Mrs Peters for
              ongoing wound review following discharge after lower-leg wound
              treatment...
            </p>
          </OetSectionCard>
        </Col>
        <Col xl={5}>
          <OetSectionCard title="Annotations and rationale">
            <ul className="mb-3">
              {data.modelAnswer.annotations.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
            <ul className="mb-0">
              {data.modelAnswer.rationale.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function SpeakingTaskDetailScreen({ taskId }: { taskId: string }) {
  const { data, isLoading } = useContentLibraryQuery("speaking");
  const task = data?.find((item) => item.id === taskId);

  if (isLoading || !task) {
    return (
      <OetPageShell
        description="Preview the role card and prep notes before launching the speaking task."
        icon={MessageText}
        mainTitle="Speaking Task"
        path={["Speaking", "Task Detail"]}
        title="Learner App"
      >
        <Alert color="light">Loading Speaking task...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      action={{
        href: "/app/speaking/attempt/attempt-speaking-1",
        label: "Start speaking attempt",
      }}
      description="Preview the role card and prep notes before launching the speaking task."
      icon={MessageText}
      mainTitle={task.title}
      path={["Speaking", "Role Card Preview"]}
      title="Learner App"
    >
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Role card">
            <p className="text-secondary">
              Explain pain-management side effects, reassure the patient, and
              check what they already understand before giving advice.
            </p>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Prep controls">
            <KeyValueList
              items={[
                { label: "Duration", value: `${task.durationMinutes} min` },
                { label: "Difficulty", value: task.difficulty },
                { label: "Prep timer", value: "3 min" },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function SpeakingAttemptScreen({ attemptId }: { attemptId: string }) {
  const { data, isLoading } = useAttemptDetailQuery(attemptId);
  const [recordingState, setRecordingState] = useState<
    "idle" | "recording" | "uploaded"
  >("idle");

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Live speaking task with clear recording state, elapsed time, and safe submit controls."
        icon={MessageText}
        mainTitle="Speaking Attempt"
        path={["Speaking", "Attempt"]}
        title="Learner App"
      >
        <Alert color="light">Loading speaking attempt...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Live speaking task with clear recording state, elapsed time, and safe submit controls."
      icon={MessageText}
      mainTitle="Speaking Attempt"
      path={["Speaking", "Attempt"]}
      title="Learner App"
    >
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Role-play workspace">
            <Alert color="light-primary">
              AI interlocutor mode is prepared. The learner can also
              self-practice or use exam simulation timing.
            </Alert>
            <FormGroup>
              <Label for="audioUpload">Upload or replace speaking audio</Label>
              <Input
                id="audioUpload"
                accept="audio/*"
                type="file"
                onChange={() => setRecordingState("uploaded")}
              />
            </FormGroup>
            <div className="d-flex flex-wrap gap-2">
              <Button
                color="primary"
                onClick={() => setRecordingState("recording")}
              >
                Start recording
              </Button>
              <Button
                color="light-secondary"
                onClick={() => setRecordingState("uploaded")}
              >
                Stop and save
              </Button>
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Live state">
            <KeyValueList
              items={[
                { label: "Mode", value: data.mode },
                { label: "Recording state", value: recordingState },
                { label: "Elapsed time", value: "08:42" },
                { label: "Reconnect handling", value: "Retry available" },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
      <StickyActionBar
        actions={[
          {
            href: "/app/speaking/result/eval-speaking-1",
            label: "Submit speaking attempt",
          },
          {
            href: "/app/speaking/review/attempt-speaking-1",
            label: "Open transcript review",
          },
        ]}
      />
    </OetPageShell>
  );
}

function SpeakingResultScreen({ evaluationId }: { evaluationId: string }) {
  const { data, isLoading } = useEvaluationDetailQuery(evaluationId);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Speaking summary stays confidence-labeled and links directly into transcript review."
        icon={MessageText}
        mainTitle="Speaking Result"
        path={["Speaking", "Result"]}
        title="Learner App"
      >
        <Alert color="light">Loading speaking result...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Speaking summary stays confidence-labeled and links directly into transcript review."
      icon={MessageText}
      mainTitle="Speaking Result"
      path={["Speaking", "Result"]}
      title="Learner App"
    >
      <AsyncStateAlert
        status={data.status}
        title="Speaking evaluation status"
      />
      <div className="d-flex flex-wrap gap-2 mb-3">
        <ScoreRangeBadge value={data.scoreRange} />
        <ConfidenceBadge value={data.confidence} />
      </div>
      <p className="text-secondary">{data.summary}</p>
      <StickyActionBar
        actions={[
          {
            href: "/app/speaking/review/attempt-speaking-1",
            label: "Open transcript review",
          },
          { href: "/app/reviews", label: "Request expert review" },
        ]}
      />
    </OetPageShell>
  );
}

function SpeakingReviewScreen({ attemptId }: { attemptId: string }) {
  const { data, isLoading } = useSpeakingReviewQuery(attemptId);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Transcript, waveform, inline markers, and better phrasing all stay reviewable on small screens."
        icon={MessageText}
        mainTitle="Speaking Review"
        path={["Speaking", "Transcript Review"]}
        title="Learner App"
      >
        <Alert color="light">Loading transcript review...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Transcript, waveform, inline markers, and better phrasing all stay reviewable on small screens."
      icon={MessageText}
      mainTitle="Speaking Review"
      path={["Speaking", "Transcript Review"]}
      title="Learner App"
    >
      {data.evaluation ? (
        <AsyncStateAlert
          status={data.evaluation.status}
          title="Transcript and evaluation workflow"
        />
      ) : null}
      <Row>
        <Col xl={5}>
          <OetSectionCard title="Transcript">
            <div className="vstack gap-3">
              {data.transcript.map((segment, index) => (
                <Card
                  className="border"
                  key={`${segment.start}-${segment.end}`}
                >
                  <CardBody>
                    <div className="d-flex justify-content-between gap-3 mb-2">
                      <Badge color="light-primary">
                        {segment.start} - {segment.end}
                      </Badge>
                      <Badge color="light-warning">{segment.issue}</Badge>
                    </div>
                    <p className="mb-0 text-secondary">{segment.text}</p>
                    <div className="mt-3">
                      <WaveformBars
                        bars={[16, 30, 22, 36, 18, 28, 24, 40]}
                        currentIndex={index}
                      />
                    </div>
                  </CardBody>
                </Card>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={7}>
          <OetSectionCard title="Better phrasing">
            <div className="vstack gap-3">
              {data.betterPhrasing.map((item) => (
                <Card key={item.original} className="border">
                  <CardBody>
                    <p className="mb-1">
                      <strong>Original:</strong> {item.original}
                    </p>
                    <p className="mb-1">
                      <strong>Issue:</strong> {item.issue}
                    </p>
                    <p className="mb-1">
                      <strong>Stronger alternative:</strong> {item.better}
                    </p>
                    <p className="mb-0 text-secondary">{item.prompt}</p>
                  </CardBody>
                </Card>
              ))}
            </div>
          </OetSectionCard>
        </Col>
      </Row>
    </OetPageShell>
  );
}

function ReadingTaskScreen({ taskId }: { taskId: string }) {
  const { data } = useContentLibraryQuery("reading");
  const task = data?.find((item) => item.id === taskId);

  return (
    <OetPageShell
      description="Timed reading player with answer persistence and easy item navigation."
      icon={Book}
      mainTitle={task?.title ?? "Reading Task"}
      path={["Reading", "Task"]}
      title="Learner App"
    >
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Items">
            <div className="vstack gap-3">
              {["Question 1", "Question 2", "Question 3"].map(
                (question, index) => (
                  <Card key={question} className="border">
                    <CardBody>
                      <p className="mb-2">
                        {question}: identify the most relevant statement from
                        the triage note.
                      </p>
                      <Input type="select" defaultValue="">
                        <option value="">Select answer</option>
                        <option value={`a-${index}`}>Option A</option>
                        <option value={`b-${index}`}>Option B</option>
                        <option value={`c-${index}`}>Option C</option>
                      </Input>
                    </CardBody>
                  </Card>
                )
              )}
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Reading controls">
            <KeyValueList
              items={[
                { label: "Mode", value: "Practice" },
                { label: "Time remaining", value: "14:20" },
                { label: "Navigation", value: "All items available" },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
      <StickyActionBar
        actions={[{ href: "/app/progress", label: "Submit reading task" }]}
      />
    </OetPageShell>
  );
}

function ListeningTaskScreen({ taskId }: { taskId: string }) {
  const { data } = useContentLibraryQuery("listening");
  const task = data?.find((item) => item.id === taskId);

  return (
    <OetPageShell
      description="Listening player with stable audio controls, answer persistence, and transcript-backed review rules."
      icon={Brain}
      mainTitle={task?.title ?? "Listening Task"}
      path={["Listening", "Task"]}
      title="Learner App"
    >
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Audio and answers">
            <audio controls className="w-100 mb-3">
              <source src="" />
            </audio>
            <div className="vstack gap-3">
              {["Part C Q1", "Part C Q2", "Part C Q3"].map((question) => (
                <Card key={question} className="border">
                  <CardBody>
                    <p className="mb-2">{question}</p>
                    <Input type="select" defaultValue="">
                      <option value="">Select answer</option>
                      <option>Option A</option>
                      <option>Option B</option>
                      <option>Option C</option>
                    </Input>
                  </CardBody>
                </Card>
              ))}
            </div>
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Playback state">
            <KeyValueList
              items={[
                { label: "Mode", value: "Practice" },
                { label: "Mobile playback", value: "Safe controls" },
                { label: "Transcript reveal", value: "Post-submit only" },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
      <StickyActionBar
        actions={[{ href: "/app/progress", label: "Submit listening task" }]}
      />
    </OetPageShell>
  );
}

function MockReportScreen({ mockId }: { mockId: string }) {
  const report = mockReports.find((item) => item.id === mockId);

  return (
    <OetPageShell
      description="Mock report with sub-test breakdown, weakest criterion, prior comparison, and study-plan update CTA."
      icon={Reports}
      mainTitle="Mock Report"
      path={["Mock Center", "Report"]}
      title="Learner App"
    >
      <Row>
        <Col xl={8}>
          <OetSectionCard title="Sub-test breakdown">
            <KeyValueList
              items={
                report?.subtestBreakdown.map((item) => ({
                  label: item.label,
                  value: item.value,
                })) ?? []
              }
            />
          </OetSectionCard>
        </Col>
        <Col xl={4}>
          <OetSectionCard title="Report summary">
            <KeyValueList
              items={[
                {
                  label: "Weakest criterion",
                  value: report?.weakestCriterion ?? "N/A",
                },
                {
                  label: "Strongest area",
                  value: report?.strongestArea ?? "N/A",
                },
                {
                  label: "Comparison",
                  value:
                    report?.comparisonSummary ??
                    "No prior comparison available",
                },
              ]}
            />
          </OetSectionCard>
        </Col>
      </Row>
      <StickyActionBar
        actions={[
          { href: "/app/study-plan", label: "Update study plan focus" },
        ]}
      />
    </OetPageShell>
  );
}

export function WritingTaskDetailPage(props: { taskId: string }) {
  return <WritingTaskDetailScreen {...props} />;
}

export function WritingAttemptPage(props: { attemptId: string }) {
  return <WritingAttemptScreen {...props} />;
}

export function WritingResultPage(props: { evaluationId: string }) {
  return <WritingResultScreen {...props} />;
}

export function WritingRevisionPage(props: { attemptId: string }) {
  return <WritingRevisionScreen {...props} />;
}

export function WritingModelAnswerPage(props: { contentId: string }) {
  return <WritingModelAnswerScreen {...props} />;
}

export function SpeakingTaskDetailPage(props: { taskId: string }) {
  return <SpeakingTaskDetailScreen {...props} />;
}

export function SpeakingAttemptPage(props: { attemptId: string }) {
  return <SpeakingAttemptScreen {...props} />;
}

export function SpeakingResultPage(props: { evaluationId: string }) {
  return <SpeakingResultScreen {...props} />;
}

export function SpeakingReviewPage(props: { attemptId: string }) {
  return <SpeakingReviewScreen {...props} />;
}

export function ReadingTaskPage(props: { taskId: string }) {
  return <ReadingTaskScreen {...props} />;
}

export function ListeningTaskPage(props: { taskId: string }) {
  return <ListeningTaskScreen {...props} />;
}

export function MockReportPage(props: { mockId: string }) {
  return <MockReportScreen {...props} />;
}
