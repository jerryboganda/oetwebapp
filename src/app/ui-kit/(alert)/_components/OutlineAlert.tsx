import React from "react";
import { Alert, Card, CardBody, CardHeader } from "react-bootstrap";
import "prismjs/themes/prism.css";
import { IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const outlineAlerts = [
  { type: "outline-primary", message: "Outline-alert primary - check it out!" },
  {
    type: "outline-secondary",
    message: "Outline-alert secondary - check it out!",
  },
  { type: "outline-success", message: "Outline-alert success - check it out!" },
  { type: "outline-danger", message: "Outline-alert danger - check it out!" },
  { type: "outline-warning", message: "Outline-alert warning - check it out!" },
  { type: "outline-info", message: "Outline-alert info - check it out!" },
  { type: "outline-light", message: "Outline-alert light - check it out!" },
  { type: "outline-dark", message: "Outline-alert dark - check it out!" },
];

const OutlineAlert: React.FC = () => {
  return (
    <Card>
      <CardHeader className="d-flex justify-content-between align-items-center code-header">
        <h5 className="mb-0">Outline Alerts</h5>
        <a href="#" id="togglerAlert3">
          <IconCode data-source="blockbtn" className="source" size={32} />
        </a>
      </CardHeader>
      <CardBody>
        {outlineAlerts.map((alert, index) => (
          <Alert key={index} variant={alert.type} role="alert">
            {alert.message}
          </Alert>
        ))}

        <UncontrolledCollapseWrapper toggler="togglerAlert3">
          <div>
            <pre className="">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Outline Alerts</h5>
  </CardHeader>
  <CardBody>
${outlineAlerts
  .map(
    (alert) =>
      `    <Alert variant="${alert.type}" role="alert">${alert.message}</Alert>`
  )
  .join("\n")}
  </CardBody>
</Card>`}
              </code>
            </pre>
          </div>
        </UncontrolledCollapseWrapper>
      </CardBody>
    </Card>
  );
};

export default OutlineAlert;
