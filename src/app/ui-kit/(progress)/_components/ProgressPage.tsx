import React from "react";
import "prismjs/themes/prism.css";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Progress,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase, IconCode, IconTrash } from "@tabler/icons-react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faXmark } from "@fortawesome/free-solid-svg-icons";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const progressData = [
  { color: "primary", value: 12.5 },
  { color: "secondary", value: 25 },
  { color: "success", value: 37.5 },
  { color: "danger", value: 50 },
  { color: "warning", value: 62.5 },
  { color: "info", value: 75 },
  { color: "light", value: 82.5 },
  { color: "dark", value: 95 },
];

const progressItems = [
  {
    value: 100,
    color: "primary",
    text: "Loading data...",
    icon: "spinner",
    bgColor: "light-primary",
  },
  {
    value: 75,
    color: "secondary",
    text: "75% Processing",
    bgColor: "light-secondary",
  },
  {
    value: 52,
    color: "success",
    text: "52% Updating..",
    badge: "1 Min",
    bgColor: "light-success",
  },
  {
    value: 15,
    color: "danger",
    text: "Deleting data (85% remain)",
    icon: "trash",
    badge: "1 Min left",
    bgColor: "light-danger",
  },
];

const ProgressPage = () => {
  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Progress"
        title="Ui Kits"
        path={["Progress"]}
        Icon={IconBriefcase}
      />
      <PrismCodeWrapper>
        <Row className="progress-rtl">
          <Col md={6}>
            <Card>
              <CardHeader className="code-header">
                <h5>Progress Bars Basic</h5>
                <a id="togglerProgress">
                  <IconCode className="source cursor-pointer" size={32} />
                </a>
              </CardHeader>
              <CardBody>
                <div className="row">
                  <div className="d-flex gap-3 flex-wrap">
                    {progressData.map((bar, index) => (
                      <Progress
                        key={index}
                        value={bar.value}
                        color={bar.color}
                        className="w-100"
                      />
                    ))}
                  </div>
                </div>
              </CardBody>

              {/* Collapsible HTML source preview */}
              <UncontrolledCollapseWrapper toggler="#togglerProgress">
                <pre>
                  <code className="language-html">
                    {progressData
                      .map(
                        (bar) => `
                <div className="progress w-100" role="progressbar" aria-valuenow="${bar.value}" aria-valuemin="0" aria-valuemax="100">
                  <div className="progress-bar bg-${bar.color}" style="width: ${bar.value}%"></div>
                </div>`
                      )
                      .join("\n")}
                  </code>
                </pre>
              </UncontrolledCollapseWrapper>
            </Card>
          </Col>
          <Col md={6}>
            <Card>
              <CardHeader className="code-header d-flex justify-content-between">
                <h5 className="txt-ellipsis">Progress Bars Light With Text</h5>
                <a id="togglerProgressLight">
                  <IconCode className="source cursor-pointer" size={32} />
                </a>
              </CardHeader>
              <CardBody>
                <div className="row">
                  <div className="d-flex gap-3 flex-wrap">
                    {progressData.map((bar, index) => (
                      <div
                        key={index}
                        className="progress w-100"
                        role="progressbar"
                        aria-valuenow={bar.value}
                        aria-valuemin={0}
                        aria-valuemax={100}
                      >
                        <div
                          className={`progress-bar bg-light-${bar.color}`}
                          style={{ width: `${bar.value}%` }}
                        >
                          {`${bar.value}%`}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </CardBody>

              {/* UncontrolledCollapse for Code Preview */}
              <UncontrolledCollapseWrapper toggler="#togglerProgressLight">
                <pre>
                  <code className="language-html">
                    {progressData
                      .map(
                        (bar) => `
<div className="progress w-100" role="progressbar" aria-valuenow="${bar.value}" aria-valuemin="0" aria-valuemax="100">
  <div className="progress-bar bg-light-${bar.color}" style="width: ${bar.value}%"> ${bar.value}% </div>
</div>`
                      )
                      .join("\n")}
                  </code>
                </pre>
              </UncontrolledCollapseWrapper>
            </Card>
          </Col>
          <Col md={6}>
            <Card>
              <CardHeader className="code-header d-flex justify-content-between">
                <h5 className="txt-ellipsis">
                  Striped Progress With Animation
                </h5>
                <a id="togglerProgressStriped">
                  <IconCode className="source cursor-pointer" size={32} />
                </a>
              </CardHeader>
              <CardBody>
                <div className="row">
                  <div className="d-flex gap-3 flex-wrap">
                    {progressData.map((bar, index) => (
                      <div
                        key={index}
                        className="progress w-100"
                        role="progressbar"
                        aria-valuenow={bar.value}
                        aria-valuemin={0}
                        aria-valuemax={100}
                      >
                        <div
                          className={`progress-bar bg-${bar.color} progress-bar-striped progress-bar-animated`}
                          style={{ width: `${bar.value}%` }}
                        >
                          {`${bar.value}%`}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </CardBody>

              {/* UncontrolledCollapse for Code Preview */}
              <UncontrolledCollapseWrapper toggler="#togglerProgressStriped">
                <pre>
                  <code className="language-html">
                    {progressData
                      .map(
                        (bar) => `
<div className="progress w-100" role="progressbar" aria-valuenow="${bar.value}" aria-valuemin="0" aria-valuemax="100">
  <div className="progress-bar bg-${bar.color} progress-bar-striped progress-bar-animated" style="width: ${bar.value}%"> ${bar.value}% </div>
</div>`
                      )
                      .join("\n")}
                  </code>
                </pre>
              </UncontrolledCollapseWrapper>
            </Card>
          </Col>
          <Col md={6}>
            <Card>
              <CardHeader className="code-header d-flex justify-content-between">
                <h5>Progress Sizes</h5>
                <a id="togglerProgressSizes">
                  <IconCode className="source cursor-pointer" size={32} />
                </a>
              </CardHeader>
              <CardBody>
                <div className="card-body d-flex flex-column gap-3">
                  {progressData.map((bar, index) => (
                    <div key={index} className={`progress h-${index + 5}`}>
                      <div
                        className={`progress-bar bg-${bar.color} h-${index + 5}`}
                        role="progressbar"
                        aria-valuenow={bar.value}
                        aria-valuemin={0}
                        aria-valuemax={100}
                        style={{ width: `${bar.value}%` }}
                      >
                        {` ${bar.value}%`}
                      </div>
                    </div>
                  ))}
                </div>
              </CardBody>

              {/* UncontrolledCollapse for Code Preview */}
              <UncontrolledCollapseWrapper toggler="#togglerProgressSizes">
                <pre>
                  <code className="language-html">
                    {progressData
                      .map(
                        (bar, index) => `
  <div className="progress h-${index + 5}">
    <div className="progress-bar bg-${bar.color} h-${index + 5}" role="progressbar" aria-valuenow="${bar.value}" aria-valuemin="0" aria-valuemax="100" style="width: ${bar.value}%"> ${bar.value}% </div>
  </div>`
                      )
                      .join("\n")}
                  </code>
                </pre>
              </UncontrolledCollapseWrapper>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader className="code-header">
                <h5>The Real Time Example</h5>
                <a id="togglerProgressReal">
                  <IconCode className="source cursor-pointer" size={32} />
                </a>
              </CardHeader>
              <CardBody>
                <Row>
                  {progressItems.map((item, index) => (
                    <Col md={6} className="mb-3" key={index}>
                      <div className={`progress-box bg-${item.bgColor} w-100`}>
                        <div className="progress-content">
                          <div>
                            <div className="left d-flex align-items-center">
                              {item.icon === "spinner" && (
                                <span
                                  className="spinner-border spinner-border-sm me-2 ms-2"
                                  role="status"
                                  aria-hidden="true"
                                ></span>
                              )}
                              {item.icon === "close" && (
                                <FontAwesomeIcon
                                  icon={faXmark}
                                  className="me-1 ms-1"
                                />
                              )}
                              {item.icon === "trash" && (
                                <IconTrash size={20} className="me-1 ms-1" />
                              )}
                              {item.text}
                            </div>
                            <div className="right">
                              {item.badge && (
                                <span className={`badge text-bg-${item.color}`}>
                                  {item.badge}
                                </span>
                              )}
                            </div>
                          </div>
                        </div>
                        <Progress
                          value={item.value}
                          color={item.color}
                          className="w-100 h-5"
                          aria-valuenow={item.value}
                          aria-valuemin={0}
                          aria-valuemax={100}
                        />
                      </div>
                    </Col>
                  ))}
                </Row>
                <UncontrolledCollapseWrapper toggler="#togglerProgressReal">
                  <pre>
                    <code className="language-html">
                      {progressItems
                        .map(
                          (item) => `
<div className="d-flex gap-3 flex-wrap">
          <div className="progress-box bg-${item.bgColor} w-100">
            <div className="progress-content">
              <div>
                <div className="left d-flex align-items-center">
                  ${
                    item.icon === "spinner"
                      ? '<span className="spinner-border spinner-border-sm me-2 ms-2" role="status" aria-hidden="true"></span>'
                      : ""
                  }
                  ${
                    item.icon === "close"
                      ? '<FontAwesomeIcon icon={faXmark} className="me-1 ms-1"/>'
                      : ""
                  }
                  ${
                    item.icon === "trash"
                      ? '<IconTrash size={20} className="me-1 ms-1" />'
                      : ""
                  }
                  ${item.text}
                </div>
                <div className="right">
                  ${item.badge ? `<span class="badge text-bg-${item.color}">${item.badge}</span>` : ""}
                </div>
              </div>
            </div>
            <div className="progress w-100 h-5" role="progressbar" aria-valuenow="${item.value}" aria-valuemax="100">
              <div className="progress-bar bg-${item.color} h-5" style="width: ${item.value}%">${item.value}%</div>
            </div>
          </div>
        </div>`
                        )
                        .join("\n")}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </PrismCodeWrapper>
    </Container>
  );
};

export default ProgressPage;
