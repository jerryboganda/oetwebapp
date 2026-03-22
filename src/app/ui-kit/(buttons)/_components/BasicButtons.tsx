import React from "react";
import { IconCode } from "@tabler/icons-react";
import { Card, CardHeader, CardBody, Button, Col } from "reactstrap";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const basicButtons = [
  "primary",
  "secondary",
  "success",
  "warning",
  "danger",
  "info",
  "light",
  "dark",
  "link",
];

const outlineButtons = [
  "primary",
  "secondary",
  "success",
  "warning",
  "danger",
  "info",
  "light",
  "dark",
];

const lightButtonData = [
  { type: "primary", label: "Primary" },
  { type: "secondary", label: "Secondary" },
  { type: "success", label: "Success" },
  { type: "danger", label: "Danger" },
  { type: "warning", label: "Warning" },
  { type: "info", label: "Info" },
  { type: "light", label: "Light" },
  { type: "dark", label: "Dark" },
  { type: "link", label: "Link" },
];

const ButtonShowcase: React.FC = () => {
  return (
    <Col xs={12}>
      {/* Basic Buttons */}
      <Card>
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5 className="mb-0">Basic Buttons</h5>
          <a href="#" id="toggleBasicBtn">
            <IconCode className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <div className="d-flex flex-wrap gap-2">
            {basicButtons.map((variant) => (
              <Button key={variant} color={variant}>
                {variant.charAt(0).toUpperCase() + variant.slice(1)}
              </Button>
            ))}
          </div>

          <UncontrolledCollapseWrapper toggler="#toggleBasicBtn">
            <pre className="language-html mt-3">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Basic Buttons</h5>
  </CardHeader>
  <CardBody>
${basicButtons
  .map(
    (variant) =>
      `    <Button color="${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)}</Button>`
  )
  .join("\n")}
  </CardBody>
</Card>`}
              </code>
            </pre>
          </UncontrolledCollapseWrapper>
        </CardBody>
      </Card>

      {/* Outline Buttons */}
      <Card>
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5 className="mb-0">Outline Buttons</h5>
          <a href="#" id="toggleOutlineBtn">
            <IconCode className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <div className="d-flex flex-wrap gap-2">
            {outlineButtons.map((variant) => (
              <Button key={variant} color={variant} outline>
                {variant.charAt(0).toUpperCase() + variant.slice(1)}
              </Button>
            ))}
          </div>

          <UncontrolledCollapseWrapper toggler="#toggleOutlineBtn">
            <pre className="language-html mt-3">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Outline Buttons</h5>
  </CardHeader>
  <CardBody>
${outlineButtons
  .map(
    (variant) =>
      `    <Button color="${variant}" outline>${variant.charAt(0).toUpperCase() + variant.slice(1)}</Button>`
  )
  .join("\n")}
  </CardBody>
</Card>`}
              </code>
            </pre>
          </UncontrolledCollapseWrapper>
        </CardBody>
      </Card>

      {/* Light Buttons */}
      <Card>
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5 className="mb-0">Light Buttons</h5>
          <a href="#" id="toggleLightBtn">
            <IconCode className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <div className="d-flex flex-wrap gap-2">
            {lightButtonData.map((btn, index) => (
              <Button
                key={index}
                color={`light-${btn.type}`}
                type="button"
                className="me-2"
              >
                {btn.label}
              </Button>
            ))}
          </div>

          <UncontrolledCollapseWrapper toggler="#toggleLightBtn">
            <pre className="language-html mt-3">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Light Buttons</h5>
  </CardHeader>
  <CardBody>
${lightButtonData
  .map(
    (btn) =>
      `    <Button color="light-${btn.type}" type="button">${btn.label}</Button>`
  )
  .join("\n")}
  </CardBody>
</Card>`}
              </code>
            </pre>
          </UncontrolledCollapseWrapper>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ButtonShowcase;
