"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import {
  Activity,
  DataTransferBoth,
  Folder,
  PageEdit,
  PageSearch,
  PrivacyPolicy,
  Settings,
  Shield,
  UserBag,
  WarningTriangle,
} from "iconoir-react";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import {
  Alert,
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
import EnrollmentTaxonomyAdmin from "@/Component/OET/Admin/EnrollmentTaxonomyAdmin";
import {
  KeyValueList,
  OetMetricGrid,
  OetPageShell,
  OetSectionCard,
} from "@/Component/OET/Common/OetShared";
import { getContentRevisionsById } from "@/Data/OET/mock";
import { bindReactstrapInput } from "@/lib/forms/reactstrap";
import { useAdminOpsQuery, useContentLibraryQuery } from "@/lib/oet/queries";
import {
  contentBuilderSchema,
  type ContentBuilderFormValues,
} from "@/lib/oet/schemas";

function ContentLibraryScreen() {
  const { data, isLoading } = useAdminOpsQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Published, draft, and archived content with revisions and saved filters."
        icon={Folder}
        mainTitle="Content Library"
        path={["Content Library"]}
        title="Admin CMS"
      >
        <Alert color="light">Loading content library...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      action={{ href: "/cms/content/new", label: "Create content" }}
      description="Published, draft, and archived content with revisions and saved filters."
      icon={Folder}
      mainTitle="Content Library"
      path={["Content Library"]}
      title="Admin CMS"
    >
      <CustomDataTable
        columns={[
          { key: "title", header: "Title" },
          { key: "subtest", header: "Sub-test" },
          { key: "profession", header: "Profession" },
          { key: "difficulty", header: "Difficulty" },
          { key: "status", header: "Status" },
          { key: "revisions", header: "Revisions" },
        ]}
        data={data.content}
        description="Final integrated version can layer saved filters and bulk actions on top of the same table structure."
        onEdit={(item) => {
          window.location.href = `/cms/content/${item.id}`;
        }}
        title="Content inventory"
      />
    </OetPageShell>
  );
}

function ContentBuilderScreen() {
  const { data, isLoading } = useContentLibraryQuery();
  const [saved, setSaved] = useState(false);
  const form = useForm<ContentBuilderFormValues>({
    defaultValues: {
      criteriaFocus: [],
      difficulty: "target",
      durationMinutes: 45,
      metadataNotes: "",
      professionId: "",
      subtest: "writing",
      title: "",
    },
    resolver: zodResolver(contentBuilderSchema),
  });

  useEffect(() => {
    if (!saved) {
      return;
    }
    const timeout = window.setTimeout(() => setSaved(false), 2500);
    return () => window.clearTimeout(timeout);
  }, [saved]);

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Task builder for profession, criteria mapping, difficulty, duration, and rubric metadata."
        icon={PageEdit}
        mainTitle="Task Builder"
        path={["Task Builder"]}
        title="Admin CMS"
      >
        <Alert color="light">Loading task builder...</Alert>
      </OetPageShell>
    );
  }

  const professions = Array.from(
    new Set(data.map((item) => item.professionId).filter(Boolean))
  ) as string[];

  return (
    <OetPageShell
      description="Task builder for profession, criteria mapping, difficulty, duration, and rubric metadata."
      icon={PageEdit}
      mainTitle="Task Builder"
      path={["Task Builder"]}
      title="Admin CMS"
    >
      {saved ? <Alert color="success">Content draft saved.</Alert> : null}
      <Card>
        <CardHeader>
          <h5 className="mb-1">Create or version content</h5>
          <p className="mb-0 text-secondary">
            This form uses the final shared validation stack: React Hook Form
            and Zod.
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
                  <Label for="title">Title</Label>
                  <Input
                    id="title"
                    {...bindReactstrapInput(form.register("title"))}
                    invalid={!!form.formState.errors.title}
                  />
                  <FormFeedback>
                    {form.formState.errors.title?.message}
                  </FormFeedback>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="subtest">Sub-test</Label>
                  <Input
                    id="subtest"
                    type="select"
                    {...bindReactstrapInput(form.register("subtest"))}
                  >
                    <option value="writing">Writing</option>
                    <option value="speaking">Speaking</option>
                    <option value="reading">Reading</option>
                    <option value="listening">Listening</option>
                  </Input>
                </FormGroup>
              </Col>
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
                    {professions.map((profession) => (
                      <option key={profession} value={profession}>
                        {profession}
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
                  <Label for="difficulty">Difficulty</Label>
                  <Input
                    id="difficulty"
                    type="select"
                    {...bindReactstrapInput(form.register("difficulty"))}
                  >
                    <option value="foundation">Foundation</option>
                    <option value="target">Target</option>
                    <option value="stretch">Stretch</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="durationMinutes">Estimated duration</Label>
                  <Input
                    id="durationMinutes"
                    type="number"
                    {...bindReactstrapInput(
                      form.register("durationMinutes", { valueAsNumber: true })
                    )}
                    invalid={!!form.formState.errors.durationMinutes}
                  />
                  <FormFeedback>
                    {form.formState.errors.durationMinutes?.message}
                  </FormFeedback>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="criteriaFocus">Criteria focus</Label>
                  <Input
                    id="criteriaFocus"
                    type="select"
                    multiple
                    onChange={(event) => {
                      const select =
                        event.currentTarget as unknown as HTMLSelectElement;
                      const values = Array.from(select.selectedOptions).map(
                        (option) => option.value
                      );
                      form.setValue("criteriaFocus", values, {
                        shouldDirty: true,
                        shouldValidate: true,
                      });
                    }}
                  >
                    {[
                      "purpose",
                      "content",
                      "clarity",
                      "relationship",
                      "patient-perspective",
                    ].map((criterion) => (
                      <option key={criterion} value={criterion}>
                        {criterion}
                      </option>
                    ))}
                  </Input>
                </FormGroup>
              </Col>
              <Col xs={12}>
                <FormGroup>
                  <Label for="metadataNotes">
                    Model answer or rubric notes
                  </Label>
                  <Input
                    id="metadataNotes"
                    type="textarea"
                    rows={4}
                    {...bindReactstrapInput(form.register("metadataNotes"))}
                    invalid={!!form.formState.errors.metadataNotes}
                  />
                  <FormFeedback>
                    {form.formState.errors.metadataNotes?.message}
                  </FormFeedback>
                </FormGroup>
              </Col>
            </Row>
            <div className="d-flex justify-content-end">
              <Button color="primary" type="submit">
                Save draft
              </Button>
            </div>
          </Form>
        </CardBody>
      </Card>
    </OetPageShell>
  );
}

function SimpleKeyValueScreen({
  description,
  icon,
  items,
  mainTitle,
  path,
}: {
  description: string;
  icon: React.ElementType;
  items: Array<{ label: string; value: string }>;
  mainTitle: string;
  path: string[];
}) {
  return (
    <OetPageShell
      description={description}
      icon={icon}
      mainTitle={mainTitle}
      path={path}
      title="Admin CMS"
    >
      <OetSectionCard title={mainTitle}>
        <KeyValueList items={items} />
      </OetSectionCard>
    </OetPageShell>
  );
}

function OpsOverviewScreen() {
  const { data, isLoading } = useAdminOpsQuery();

  if (isLoading || !data) {
    return (
      <OetPageShell
        description="Operational overview for review routing, analytics, and admin work."
        icon={WarningTriangle}
        mainTitle="Review Ops"
        path={["Review Ops"]}
        title="Admin CMS"
      >
        <Alert color="light">Loading review ops...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Operational overview for review routing, analytics, and admin work."
      icon={WarningTriangle}
      mainTitle="Review Ops"
      path={["Review Ops"]}
      title="Admin CMS"
    >
      <OetMetricGrid
        items={data.qualityAnalytics.map((item) => ({
          label: item.label,
          value: item.value,
        }))}
      />
    </OetPageShell>
  );
}

function ContentDetailScreen({ contentId }: { contentId: string }) {
  const { data, isLoading } = useAdminOpsQuery();
  const content = data?.content.find((item) => item.id === contentId);

  if (isLoading || !content) {
    return (
      <OetPageShell
        description="Content metadata and operational state."
        icon={Folder}
        mainTitle="Content Detail"
        path={["Content Library", "Content Detail"]}
        title="Admin CMS"
      >
        <Alert color="light">Loading content detail...</Alert>
      </OetPageShell>
    );
  }

  return (
    <OetPageShell
      description="Content metadata and operational state."
      icon={Folder}
      mainTitle={content.title}
      path={["Content Library", "Content Detail"]}
      title="Admin CMS"
    >
      <OetSectionCard title="Metadata">
        <KeyValueList
          items={[
            { label: "Sub-test", value: content.subtest },
            { label: "Profession", value: content.profession },
            { label: "Difficulty", value: content.difficulty },
            { label: "Status", value: content.status },
          ]}
        />
      </OetSectionCard>
    </OetPageShell>
  );
}

function ContentRevisionsScreen({ contentId }: { contentId: string }) {
  const revisions = getContentRevisionsById(contentId);

  return (
    <OetPageShell
      description="Version history remains directly accessible from the content surface."
      icon={Folder}
      mainTitle="Content Revisions"
      path={["Content Library", "Revisions"]}
      title="Admin CMS"
    >
      <CustomDataTable
        columns={[
          { key: "revisionId", header: "Revision" },
          { key: "author", header: "Author" },
          { key: "createdAt", header: "Created" },
          { key: "summary", header: "Summary" },
        ]}
        data={revisions}
        description="Revision and version history stays close to the task builder."
        title="Revision history"
      />
    </OetPageShell>
  );
}

const adminStaticRoutes = new Set([
  "",
  "content",
  "content/new",
  "taxonomy",
  "taxonomy/countries",
  "taxonomy/exams",
  "taxonomy/professions",
  "taxonomy/sessions",
  "criteria",
  "ai-config",
  "review-ops",
  "analytics/quality",
  "users",
  "billing",
  "flags",
  "audit-logs",
]);

export function isAdminStaticRoute(slug?: string[]): boolean {
  return adminStaticRoutes.has(slug?.join("/") ?? "");
}

export function AdminStaticPage({ slug }: { slug?: string[] }) {
  const key = slug?.join("/") ?? "";

  switch (key) {
    case "":
    case "content":
      return <ContentLibraryScreen />;
    case "content/new":
      return <ContentBuilderScreen />;
    case "taxonomy":
      return (
        <OetPageShell
          description="Frontend-managed enrollment taxonomy that also powers the sign-up wizard."
          icon={DataTransferBoth}
          mainTitle="Taxonomy"
          path={["Taxonomy"]}
          title="Admin CMS"
        >
          <EnrollmentTaxonomyAdmin defaultSection="overview" />
        </OetPageShell>
      );
    case "taxonomy/exams":
      return (
        <OetPageShell
          description="Manage available exam types for enrollment."
          icon={DataTransferBoth}
          mainTitle="Exam Types"
          path={["Taxonomy", "Exam Types"]}
          title="Admin CMS"
        >
          <EnrollmentTaxonomyAdmin defaultSection="exams" />
        </OetPageShell>
      );
    case "taxonomy/countries":
      return (
        <OetPageShell
          description="Manage the target-country taxonomy that powers profession mappings and signup."
          icon={DataTransferBoth}
          mainTitle="Target Countries"
          path={["Taxonomy", "Target Countries"]}
          title="Admin CMS"
        >
          <EnrollmentTaxonomyAdmin defaultSection="countries" />
        </OetPageShell>
      );
    case "taxonomy/professions":
      return (
        <OetPageShell
          description="Manage profession designations and their target markets."
          icon={DataTransferBoth}
          mainTitle="Professions"
          path={["Taxonomy", "Professions"]}
          title="Admin CMS"
        >
          <EnrollmentTaxonomyAdmin defaultSection="professions" />
        </OetPageShell>
      );
    case "taxonomy/sessions":
      return (
        <OetPageShell
          description="Manage live and upcoming enrollment cohorts."
          icon={DataTransferBoth}
          mainTitle="Sessions"
          path={["Taxonomy", "Sessions"]}
          title="Admin CMS"
        >
          <EnrollmentTaxonomyAdmin defaultSection="sessions" />
        </OetPageShell>
      );
    case "criteria":
      return (
        <SimpleKeyValueScreen
          description="Rubrics and criteria mapping remain visible for content governance."
          icon={PrivacyPolicy}
          items={[
            { label: "Writing criteria", value: "6 official criteria mapped" },
            {
              label: "Speaking criteria",
              value: "Clinical + linguistic mapped",
            },
          ]}
          mainTitle="Criteria"
          path={["Criteria"]}
        />
      );
    case "ai-config":
      return (
        <SimpleKeyValueScreen
          description="Active model version, thresholds, routing rules, and experiment flags."
          icon={Settings}
          items={[
            { label: "Active model", value: "oet-eval-v3.2" },
            {
              label: "Confidence route",
              value: "Expert review below moderate",
            },
            {
              label: "Experiment flag",
              value: "Speaking empathy prompt enabled",
            },
          ]}
          mainTitle="AI Config"
          path={["AI Config"]}
        />
      );
    case "review-ops":
      return <OpsOverviewScreen />;
    case "analytics/quality":
      return (
        <SimpleKeyValueScreen
          description="Quality analytics stay close to ops and content teams."
          icon={Activity}
          items={[
            { label: "AI-human disagreement", value: "8.4%" },
            {
              label: "Risk cases",
              value: "4 high-variance speaking evaluations",
            },
          ]}
          mainTitle="Quality Analytics"
          path={["Analytics", "Quality"]}
        />
      );
    case "users":
      return (
        <SimpleKeyValueScreen
          description="User operations across learner, expert, and admin accounts."
          icon={UserBag}
          items={[
            { label: "Active learners", value: "128" },
            { label: "Active experts", value: "12" },
            { label: "Admins", value: "3" },
          ]}
          mainTitle="User Ops"
          path={["User Ops"]}
        />
      );
    case "billing":
      return (
        <SimpleKeyValueScreen
          description="Billing operations, invoice state, and credit tracking."
          icon={UserBag}
          items={[
            { label: "Paid this week", value: "$1,240" },
            { label: "Pending invoices", value: "7" },
            { label: "Credit adjustments", value: "3 pending reviews" },
          ]}
          mainTitle="Billing Ops"
          path={["Billing Ops"]}
        />
      );
    case "flags":
      return (
        <SimpleKeyValueScreen
          description="Operational feature flags remain explicit and auditable."
          icon={Shield}
          items={[
            { label: "speaking_better_phrase_v1", value: "Enabled" },
            { label: "writing_confidence_routing", value: "Enabled" },
          ]}
          mainTitle="Feature Flags"
          path={["Feature Flags"]}
        />
      );
    case "audit-logs":
      return (
        <SimpleKeyValueScreen
          description="Audit activity for content and AI configuration changes."
          icon={PageSearch}
          items={[
            { label: "Latest publish", value: "22 Mar 2026 09:14" },
            { label: "Threshold change", value: "21 Mar 2026 16:40" },
          ]}
          mainTitle="Audit Logs"
          path={["Audit Logs"]}
        />
      );
    default:
      return <ContentLibraryScreen />;
  }
}

export const AdminContentDetailPage = ContentDetailScreen;
export const AdminContentRevisionsPage = ContentRevisionsScreen;
