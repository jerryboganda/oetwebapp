import React from "react";
import "prismjs/themes/prism.css";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase, IconCode } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const backgroundColors = [
  { name: "primary", text: "text-white", bgColor: 900 },
  { name: "secondary", text: "text-white", bgColor: 500 },
  { name: "success", text: "text-white", bgColor: 500 },
  { name: "danger", text: "text-white", bgColor: 500 },
  { name: "warning", text: "text-white", bgColor: 500 },
  { name: "info", text: "text-white", bgColor: 500 },
  { name: "light", text: "text-dark", bgColor: 500 },
  { name: "dark", text: "text-white", bgColor: 500 },
];

const backgroundColorsShades = [
  "bg-primary-900",
  "bg-primary-800",
  "bg-primary-700",
  "bg-primary-600",
  "bg-primary-500",
  "bg-primary-400",
  "bg-primary-300",
];

const BackgroundPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Background"
          title="Ui Kits"
          path={["Background"]}
          Icon={IconBriefcase}
        />
        <PrismCodeWrapper>
          <Row className="gap-4">
            <Col xs={12}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Background Colors</h5>
                  <a id="togglerbgColor">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {backgroundColors.map((color) => (
                      <span
                        key={color.name}
                        className={`py-2 px-3 bg-${color.name} ${color.text} rounded f-w-500`}
                      >
                        bg-{color.name}
                      </span>
                    ))}
                  </div>
                </CardBody>
                <UncontrolledCollapseWrapper toggler="#togglerbgColor">
                  <pre className="diamondbullet">
                    <code className="language-html">
                      {`
              <div className="d-flex gap-2 flex-wrap">
                ${backgroundColors
                  .map(
                    (color) =>
                      `<span className="py-2 px-3 bg-${color.name} ${color.text} rounded f-w-500">bg-${color.name}</span>`
                  )
                  .join("\n  ")}
              </div>
                              `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>

            {/* Background Color Shades */}
            <Col xs={12}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Background Color Shades</h5>
                  <a id="togglerBgShades">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {backgroundColorsShades.map((bgColor) => (
                      <span
                        key={bgColor}
                        className={`py-2 px-3 ${bgColor} rounded f-w-500`}
                      >
                        {bgColor}
                      </span>
                    ))}
                  </div>
                </CardBody>
                <UncontrolledCollapseWrapper toggler="#togglerBgShades">
                  <pre className="diamondbullet">
                    <code className="language-html">
                      {`
<div className="d-flex gap-2 flex-wrap">
  ${backgroundColorsShades
    .map(
      (bgColor) =>
        `<span className="py-2 px-3 ${bgColor} rounded f-w-500">${bgColor}</span>`
    )
    .join("\n  ")}
</div>
                `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>

            {/* Outline Background */}
            <Col xs={12}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Outline Background</h5>
                  <a id="togglerBgOutline">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {backgroundColors.map((color) => (
                      <span
                        key={color.name}
                        className={`py-2 px-3 bg-outline-${color.name} rounded f-w-500`}
                      >
                        bg-outline-{color.name}
                      </span>
                    ))}
                  </div>
                </CardBody>
                <UncontrolledCollapseWrapper toggler="#togglerBgOutline">
                  <pre className="diamondbullet">
                    <code className="language-html">
                      {`
<div className="d-flex gap-2 flex-wrap">
  ${backgroundColors
    .map(
      (color) =>
        `<span className="py-2 px-3 bg-outline-${color.name} rounded f-w-500">bg-outline-${color.name}</span>`
    )
    .join("\n  ")}
</div>
                `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>

            {/* Soft Background */}
            <Col xs={12}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Soft Background</h5>
                  <a id="togglerBgSoft">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {backgroundColors.map((color) => (
                      <span
                        key={color.name}
                        className={`py-2 px-3 bg-light-${color.name} rounded f-w-500`}
                      >
                        bg-light-{color.name}
                      </span>
                    ))}
                  </div>
                </CardBody>
                <UncontrolledCollapseWrapper toggler="#togglerBgSoft">
                  <pre className="diamondbullet">
                    <code className="language-html">
                      {`
<div className="d-flex gap-2 flex-wrap">
  ${backgroundColors
    .map(
      (color) =>
        `<span className="py-2 px-3 bg-light-${color.name} rounded f-w-500">bg-light-${color.name}</span>`
    )
    .join("\n  ")}
</div>
                `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>
          </Row>
        </PrismCodeWrapper>
      </Container>
    </div>
  );
};

export default BackgroundPage;
