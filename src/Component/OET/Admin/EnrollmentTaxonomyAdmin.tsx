"use client";

import { useMemo, useState } from "react";
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Input,
  Label,
  Row,
  Table,
} from "reactstrap";
import { useEnrollmentTaxonomyStore } from "@/lib/oet/stores/enrollment-taxonomy-store";
import type {
  EnrollmentSession,
  ExamType,
  Profession,
  TargetCountry,
} from "@/types/oet";

type Section = "overview" | "exams" | "professions" | "sessions" | "countries";

interface Props {
  defaultSection?: Section;
}

const examSeed: Omit<ExamType, "id"> = {
  code: "",
  description: "",
  label: "",
  status: "active",
};

const professionSeed: Omit<Profession, "id"> = {
  countryTargets: [],
  description: "",
  examTypeIds: [],
  label: "",
  status: "active",
};

const sessionSeed: Omit<EnrollmentSession, "id"> = {
  capacity: 30,
  currency: "USD",
  deliveryMode: "online",
  description: "",
  endDate: "",
  enrollmentOpen: true,
  examTypeId: "",
  name: "",
  priceLabel: "$0",
  professionIds: [],
  seatsRemaining: 30,
  startDate: "",
  status: "upcoming",
  timezone: "Asia/Karachi",
};

const targetCountrySeed: Omit<TargetCountry, "id"> = {
  label: "",
  status: "active",
};

const titleMap: Record<Section, string> = {
  overview: "Overview",
  exams: "Exam Types",
  professions: "Professions",
  sessions: "Sessions",
  countries: "Target Countries",
};

function multiValues(select: HTMLSelectElement) {
  return Array.from(select.selectedOptions).map((option) => option.value);
}

const EnrollmentTaxonomyAdmin = ({ defaultSection = "overview" }: Props) => {
  const store = useEnrollmentTaxonomyStore();
  const [section, setSection] = useState<Section>(defaultSection);
  const [examId, setExamId] = useState<string | null>(null);
  const [professionId, setProfessionId] = useState<string | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [targetCountryId, setTargetCountryId] = useState<string | null>(null);
  const [examDraft, setExamDraft] = useState(examSeed);
  const [professionDraft, setProfessionDraft] = useState(professionSeed);
  const [sessionDraft, setSessionDraft] = useState(sessionSeed);
  const [targetCountryDraft, setTargetCountryDraft] =
    useState(targetCountrySeed);

  const stats = useMemo(
    () => [
      { label: "Exam types", value: store.examTypes.length },
      { label: "Professions", value: store.professions.length },
      { label: "Sessions", value: store.sessions.length },
      { label: "Target countries", value: store.targetCountries.length },
      {
        label: "Open sessions",
        value: store.sessions.filter((item) => item.enrollmentOpen).length,
      },
    ],
    [
      store.examTypes.length,
      store.professions.length,
      store.sessions,
      store.targetCountries.length,
    ]
  );

  const clearAll = () => {
    setExamId(null);
    setProfessionId(null);
    setSessionId(null);
    setTargetCountryId(null);
    setExamDraft(examSeed);
    setProfessionDraft(professionSeed);
    setSessionDraft(sessionSeed);
    setTargetCountryDraft(targetCountrySeed);
  };

  return (
    <div className="vstack gap-4">
      <div className="d-flex flex-wrap justify-content-between gap-2">
        <div>
          <h5 className="mb-1">Enrollment Taxonomy</h5>
          <p className="mb-0 text-secondary">
            These changes persist in the browser and immediately power the
            sign-up wizard.
          </p>
        </div>
        <Button
          color="light-secondary"
          onClick={() => {
            store.reset();
            clearAll();
          }}
        >
          Reset mock defaults
        </Button>
      </div>

      <div className="d-flex flex-wrap gap-2">
        {(Object.keys(titleMap) as Section[]).map((item) => (
          <Button
            key={item}
            color={section === item ? "primary" : "light-secondary"}
            onClick={() => setSection(item)}
          >
            {titleMap[item]}
          </Button>
        ))}
      </div>

      {section === "overview" ? (
        <Row>
          {stats.map((item) => (
            <Col key={item.label} lg={3} md={6}>
              <Card className="h-100">
                <CardBody>
                  <p className="mb-2 text-secondary">{item.label}</p>
                  <h3 className="mb-0">{item.value}</h3>
                </CardBody>
              </Card>
            </Col>
          ))}
        </Row>
      ) : null}

      {section === "exams" ? (
        <Row>
          <Col xl={4}>
            <Card>
              <CardHeader>
                {examId ? "Edit exam type" : "Create exam type"}
              </CardHeader>
              <CardBody className="vstack gap-3">
                <div>
                  <Label>Label</Label>
                  <Input
                    value={examDraft.label}
                    onChange={(e) =>
                      setExamDraft({ ...examDraft, label: e.target.value })
                    }
                  />
                </div>
                <div>
                  <Label>Code</Label>
                  <Input
                    value={examDraft.code}
                    onChange={(e) =>
                      setExamDraft({
                        ...examDraft,
                        code: e.target.value.toUpperCase(),
                      })
                    }
                  />
                </div>
                <div>
                  <Label>Description</Label>
                  <Input
                    type="textarea"
                    rows={3}
                    value={examDraft.description}
                    onChange={(e) =>
                      setExamDraft({
                        ...examDraft,
                        description: e.target.value,
                      })
                    }
                  />
                </div>
                <div>
                  <Label>Status</Label>
                  <Input
                    type="select"
                    value={examDraft.status}
                    onChange={(e) =>
                      setExamDraft({
                        ...examDraft,
                        status: e.target.value as ExamType["status"],
                      })
                    }
                  >
                    <option value="active">Active</option>
                    <option value="inactive">Inactive</option>
                  </Input>
                </div>
                <div className="d-flex gap-2 justify-content-end">
                  <Button
                    color="light-secondary"
                    onClick={() => {
                      setExamId(null);
                      setExamDraft(examSeed);
                    }}
                  >
                    Clear
                  </Button>
                  <Button
                    color="primary"
                    onClick={() => {
                      if (!examDraft.label.trim() || !examDraft.code.trim())
                        return;
                      examId
                        ? store.updateExamType(examId, examDraft)
                        : store.createExamType(examDraft);
                      setExamId(null);
                      setExamDraft(examSeed);
                    }}
                  >
                    {examId ? "Update" : "Create"}
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xl={8}>
            <Card>
              <CardHeader>Exam type inventory</CardHeader>
              <CardBody className="p-0">
                <Table responsive className="align-middle mb-0">
                  <thead>
                    <tr>
                      <th>Label</th>
                      <th>Code</th>
                      <th>Status</th>
                      <th className="text-end">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {store.examTypes.map((item) => (
                      <tr key={item.id}>
                        <td>
                          <div className="fw-semibold">{item.label}</div>
                          <div className="text-secondary small">
                            {item.description}
                          </div>
                        </td>
                        <td>{item.code}</td>
                        <td>
                          <Badge
                            color={
                              item.status === "active"
                                ? "light-success"
                                : "light-secondary"
                            }
                          >
                            {item.status}
                          </Badge>
                        </td>
                        <td className="text-end">
                          <div className="d-inline-flex gap-2">
                            <Button
                              size="sm"
                              color="light-primary"
                              onClick={() => {
                                setExamId(item.id);
                                setExamDraft({
                                  code: item.code,
                                  description: item.description,
                                  label: item.label,
                                  status: item.status,
                                });
                              }}
                            >
                              Edit
                            </Button>
                            <Button
                              size="sm"
                              color="light-danger"
                              onClick={() => store.deleteExamType(item.id)}
                            >
                              Delete
                            </Button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              </CardBody>
            </Card>
          </Col>
        </Row>
      ) : null}

      {section === "professions" ? (
        <Row>
          <Col xl={4}>
            <Card>
              <CardHeader>
                {professionId ? "Edit profession" : "Create profession"}
              </CardHeader>
              <CardBody className="vstack gap-3">
                <div>
                  <Label>Label</Label>
                  <Input
                    value={professionDraft.label}
                    onChange={(e) =>
                      setProfessionDraft({
                        ...professionDraft,
                        label: e.target.value,
                      })
                    }
                  />
                </div>
                <div>
                  <Label>Description</Label>
                  <Input
                    type="textarea"
                    rows={3}
                    value={professionDraft.description ?? ""}
                    onChange={(e) =>
                      setProfessionDraft({
                        ...professionDraft,
                        description: e.target.value,
                      })
                    }
                  />
                </div>
                <div>
                  <Label>Exam types</Label>
                  <Input
                    type="select"
                    multiple
                    value={professionDraft.examTypeIds}
                    onChange={(e) =>
                      setProfessionDraft({
                        ...professionDraft,
                        examTypeIds: multiValues(
                          e.currentTarget as unknown as HTMLSelectElement
                        ),
                      })
                    }
                  >
                    {store.examTypes.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.label}
                      </option>
                    ))}
                  </Input>
                </div>
                <div>
                  <Label>Country targets</Label>
                  <Input
                    type="select"
                    multiple
                    value={professionDraft.countryTargets}
                    onChange={(e) =>
                      setProfessionDraft({
                        ...professionDraft,
                        countryTargets: multiValues(
                          e.currentTarget as unknown as HTMLSelectElement
                        ),
                      })
                    }
                  >
                    {store.targetCountries.map((item) => (
                      <option key={item.id} value={item.label}>
                        {item.label}
                      </option>
                    ))}
                  </Input>
                </div>
                <div>
                  <Label>Status</Label>
                  <Input
                    type="select"
                    value={professionDraft.status}
                    onChange={(e) =>
                      setProfessionDraft({
                        ...professionDraft,
                        status: e.target.value as Profession["status"],
                      })
                    }
                  >
                    <option value="active">Active</option>
                    <option value="inactive">Inactive</option>
                  </Input>
                </div>
                <div className="d-flex gap-2 justify-content-end">
                  <Button
                    color="light-secondary"
                    onClick={() => {
                      setProfessionId(null);
                      setProfessionDraft(professionSeed);
                    }}
                  >
                    Clear
                  </Button>
                  <Button
                    color="primary"
                    onClick={() => {
                      if (
                        !professionDraft.label.trim() ||
                        !professionDraft.examTypeIds.length
                      )
                        return;
                      professionId
                        ? store.updateProfession(professionId, professionDraft)
                        : store.createProfession(professionDraft);
                      setProfessionId(null);
                      setProfessionDraft(professionSeed);
                    }}
                  >
                    {professionId ? "Update" : "Create"}
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xl={8}>
            <Card>
              <CardHeader>Profession inventory</CardHeader>
              <CardBody className="p-0">
                <Table responsive className="align-middle mb-0">
                  <thead>
                    <tr>
                      <th>Profession</th>
                      <th>Exam types</th>
                      <th>Targets</th>
                      <th className="text-end">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {store.professions.map((item) => (
                      <tr key={item.id}>
                        <td>
                          <div className="fw-semibold">{item.label}</div>
                          <div className="text-secondary small">
                            {item.description}
                          </div>
                        </td>
                        <td>
                          {item.examTypeIds.map((examIdValue) => (
                            <Badge
                              key={examIdValue}
                              color="light-primary"
                              className="me-1"
                            >
                              {store.examTypes.find(
                                (exam) => exam.id === examIdValue
                              )?.label ?? examIdValue}
                            </Badge>
                          ))}
                        </td>
                        <td>
                          {item.countryTargets.join(", ") || "No targets"}
                        </td>
                        <td className="text-end">
                          <div className="d-inline-flex gap-2">
                            <Button
                              size="sm"
                              color="light-primary"
                              onClick={() => {
                                setProfessionId(item.id);
                                setProfessionDraft({
                                  countryTargets: item.countryTargets,
                                  description: item.description ?? "",
                                  examTypeIds: item.examTypeIds,
                                  label: item.label,
                                  status: item.status,
                                });
                              }}
                            >
                              Edit
                            </Button>
                            <Button
                              size="sm"
                              color="light-danger"
                              onClick={() => store.deleteProfession(item.id)}
                            >
                              Delete
                            </Button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              </CardBody>
            </Card>
          </Col>
        </Row>
      ) : null}

      {section === "sessions" ? (
        <Row>
          <Col xl={4}>
            <Card>
              <CardHeader>
                {sessionId ? "Edit session" : "Create session"}
              </CardHeader>
              <CardBody className="vstack gap-3">
                <div>
                  <Label>Name</Label>
                  <Input
                    value={sessionDraft.name}
                    onChange={(e) =>
                      setSessionDraft({ ...sessionDraft, name: e.target.value })
                    }
                  />
                </div>
                <div>
                  <Label>Exam type</Label>
                  <Input
                    type="select"
                    value={sessionDraft.examTypeId}
                    onChange={(e) =>
                      setSessionDraft({
                        ...sessionDraft,
                        examTypeId: e.target.value,
                      })
                    }
                  >
                    <option value="">Select exam type</option>
                    {store.examTypes.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.label}
                      </option>
                    ))}
                  </Input>
                </div>
                <div>
                  <Label>Professions</Label>
                  <Input
                    type="select"
                    multiple
                    value={sessionDraft.professionIds}
                    onChange={(e) =>
                      setSessionDraft({
                        ...sessionDraft,
                        professionIds: multiValues(
                          e.currentTarget as unknown as HTMLSelectElement
                        ),
                      })
                    }
                  >
                    {store.professions
                      .filter((item) =>
                        sessionDraft.examTypeId
                          ? item.examTypeIds.includes(sessionDraft.examTypeId)
                          : true
                      )
                      .map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.label}
                        </option>
                      ))}
                  </Input>
                </div>
                <div className="d-grid gap-3">
                  <div>
                    <Label>Price label</Label>
                    <Input
                      value={sessionDraft.priceLabel}
                      onChange={(e) =>
                        setSessionDraft({
                          ...sessionDraft,
                          priceLabel: e.target.value,
                        })
                      }
                    />
                  </div>
                  <div>
                    <Label>Dates</Label>
                    <div className="d-flex gap-2">
                      <Input
                        type="date"
                        value={sessionDraft.startDate}
                        onChange={(e) =>
                          setSessionDraft({
                            ...sessionDraft,
                            startDate: e.target.value,
                          })
                        }
                      />
                      <Input
                        type="date"
                        value={sessionDraft.endDate}
                        onChange={(e) =>
                          setSessionDraft({
                            ...sessionDraft,
                            endDate: e.target.value,
                          })
                        }
                      />
                    </div>
                  </div>
                  <div>
                    <Label>Delivery mode</Label>
                    <Input
                      type="select"
                      value={sessionDraft.deliveryMode}
                      onChange={(e) =>
                        setSessionDraft({
                          ...sessionDraft,
                          deliveryMode: e.target
                            .value as EnrollmentSession["deliveryMode"],
                        })
                      }
                    >
                      <option value="online">Online</option>
                      <option value="hybrid">Hybrid</option>
                      <option value="in-person">In person</option>
                    </Input>
                  </div>
                  <div>
                    <Label>Status</Label>
                    <Input
                      type="select"
                      value={sessionDraft.status}
                      onChange={(e) =>
                        setSessionDraft({
                          ...sessionDraft,
                          status: e.target.value as EnrollmentSession["status"],
                        })
                      }
                    >
                      <option value="upcoming">Upcoming</option>
                      <option value="open">Open</option>
                      <option value="closed">Closed</option>
                      <option value="completed">Completed</option>
                    </Input>
                  </div>
                  <div>
                    <Label>Description</Label>
                    <Input
                      type="textarea"
                      rows={3}
                      value={sessionDraft.description}
                      onChange={(e) =>
                        setSessionDraft({
                          ...sessionDraft,
                          description: e.target.value,
                        })
                      }
                    />
                  </div>
                  <div className="d-flex gap-2">
                    <Input
                      type="number"
                      value={sessionDraft.capacity}
                      onChange={(e) =>
                        setSessionDraft({
                          ...sessionDraft,
                          capacity: Number(e.target.value || 0),
                        })
                      }
                      placeholder="Capacity"
                    />
                    <Input
                      type="number"
                      value={sessionDraft.seatsRemaining}
                      onChange={(e) =>
                        setSessionDraft({
                          ...sessionDraft,
                          seatsRemaining: Number(e.target.value || 0),
                        })
                      }
                      placeholder="Seats remaining"
                    />
                  </div>
                </div>
                <div className="form-check">
                  <Input
                    id="taxonomy-open"
                    type="checkbox"
                    checked={sessionDraft.enrollmentOpen}
                    onChange={(e) =>
                      setSessionDraft({
                        ...sessionDraft,
                        enrollmentOpen: e.target.checked,
                      })
                    }
                  />
                  <Label htmlFor="taxonomy-open" check>
                    Enrollment open
                  </Label>
                </div>
                <div className="d-flex gap-2 justify-content-end">
                  <Button
                    color="light-secondary"
                    onClick={() => {
                      setSessionId(null);
                      setSessionDraft(sessionSeed);
                    }}
                  >
                    Clear
                  </Button>
                  <Button
                    color="primary"
                    onClick={() => {
                      if (
                        !sessionDraft.name.trim() ||
                        !sessionDraft.examTypeId ||
                        !sessionDraft.professionIds.length
                      )
                        return;
                      sessionId
                        ? store.updateSession(sessionId, sessionDraft)
                        : store.createSession(sessionDraft);
                      setSessionId(null);
                      setSessionDraft(sessionSeed);
                    }}
                  >
                    {sessionId ? "Update" : "Create"}
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xl={8}>
            <Card>
              <CardHeader>Session inventory</CardHeader>
              <CardBody className="p-0">
                <Table responsive className="align-middle mb-0">
                  <thead>
                    <tr>
                      <th>Session</th>
                      <th>Exam</th>
                      <th>Dates</th>
                      <th>Availability</th>
                      <th className="text-end">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {store.sessions.map((item) => (
                      <tr key={item.id}>
                        <td>
                          <div className="fw-semibold">{item.name}</div>
                          <div className="text-secondary small">
                            {item.priceLabel} · {item.deliveryMode}
                          </div>
                        </td>
                        <td>
                          {store.examTypes.find(
                            (exam) => exam.id === item.examTypeId
                          )?.label ?? item.examTypeId}
                        </td>
                        <td>
                          {item.startDate && item.endDate
                            ? `${item.startDate} -> ${item.endDate}`
                            : "Dates not set"}
                        </td>
                        <td>
                          <Badge
                            color={
                              item.enrollmentOpen
                                ? "light-success"
                                : "light-secondary"
                            }
                          >
                            {item.enrollmentOpen
                              ? `${item.seatsRemaining}/${item.capacity} seats`
                              : "Closed"}
                          </Badge>
                        </td>
                        <td className="text-end">
                          <div className="d-inline-flex gap-2">
                            <Button
                              size="sm"
                              color="light-primary"
                              onClick={() => {
                                setSessionId(item.id);
                                setSessionDraft({
                                  capacity: item.capacity,
                                  currency: item.currency,
                                  deliveryMode: item.deliveryMode,
                                  description: item.description,
                                  endDate: item.endDate,
                                  enrollmentOpen: item.enrollmentOpen,
                                  examTypeId: item.examTypeId,
                                  name: item.name,
                                  priceLabel: item.priceLabel,
                                  professionIds: item.professionIds,
                                  seatsRemaining: item.seatsRemaining,
                                  startDate: item.startDate,
                                  status: item.status,
                                  timezone: item.timezone,
                                });
                              }}
                            >
                              Edit
                            </Button>
                            <Button
                              size="sm"
                              color="light-danger"
                              onClick={() => store.deleteSession(item.id)}
                            >
                              Delete
                            </Button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              </CardBody>
            </Card>
          </Col>
        </Row>
      ) : null}

      {section === "countries" ? (
        <Row>
          <Col xl={4}>
            <Card>
              <CardHeader>
                {targetCountryId
                  ? "Edit target country"
                  : "Create target country"}
              </CardHeader>
              <CardBody className="vstack gap-3">
                <div>
                  <Label>Country name</Label>
                  <Input
                    value={targetCountryDraft.label}
                    onChange={(e) =>
                      setTargetCountryDraft({
                        ...targetCountryDraft,
                        label: e.target.value,
                      })
                    }
                    placeholder="United Kingdom"
                  />
                </div>
                <div>
                  <Label>Status</Label>
                  <Input
                    type="select"
                    value={targetCountryDraft.status}
                    onChange={(e) =>
                      setTargetCountryDraft({
                        ...targetCountryDraft,
                        status: e.target.value as TargetCountry["status"],
                      })
                    }
                  >
                    <option value="active">Active</option>
                    <option value="inactive">Inactive</option>
                  </Input>
                </div>
                <div className="d-flex gap-2 justify-content-end">
                  <Button
                    color="light-secondary"
                    onClick={() => {
                      setTargetCountryId(null);
                      setTargetCountryDraft(targetCountrySeed);
                    }}
                  >
                    Clear
                  </Button>
                  <Button
                    color="primary"
                    onClick={() => {
                      if (!targetCountryDraft.label.trim()) return;
                      targetCountryId
                        ? store.updateTargetCountry(
                            targetCountryId,
                            targetCountryDraft
                          )
                        : store.createTargetCountry(targetCountryDraft);
                      setTargetCountryId(null);
                      setTargetCountryDraft(targetCountrySeed);
                    }}
                  >
                    {targetCountryId ? "Update" : "Create"}
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xl={8}>
            <Card>
              <CardHeader>Target country inventory</CardHeader>
              <CardBody className="p-0">
                <Table responsive className="align-middle mb-0">
                  <thead>
                    <tr>
                      <th>Country</th>
                      <th>Status</th>
                      <th className="text-end">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {store.targetCountries.map((item) => (
                      <tr key={item.id}>
                        <td className="fw-semibold">{item.label}</td>
                        <td>
                          <Badge
                            color={
                              item.status === "active"
                                ? "light-success"
                                : "light-secondary"
                            }
                          >
                            {item.status}
                          </Badge>
                        </td>
                        <td className="text-end">
                          <div className="d-inline-flex gap-2">
                            <Button
                              size="sm"
                              color="light-primary"
                              onClick={() => {
                                setTargetCountryId(item.id);
                                setTargetCountryDraft({
                                  label: item.label,
                                  status: item.status,
                                });
                              }}
                            >
                              Edit
                            </Button>
                            <Button
                              size="sm"
                              color="light-danger"
                              onClick={() => store.deleteTargetCountry(item.id)}
                            >
                              Delete
                            </Button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              </CardBody>
            </Card>
          </Col>
        </Row>
      ) : null}
    </div>
  );
};

export default EnrollmentTaxonomyAdmin;
