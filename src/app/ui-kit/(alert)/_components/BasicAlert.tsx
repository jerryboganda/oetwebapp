import React from "react";
import { Alert, Card, CardBody, CardHeader } from "react-bootstrap";
import "prismjs/themes/prism.css";
import { IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const alerts = [
  { type: "primary", message: "Simple primary alert - check it out!" },
  { type: "secondary", message: "Simple secondary alert - check it out!" },
  { type: "success", message: "Simple success alert - check it out!" },
  { type: "danger", message: "Simple danger alert - check it out!" },
  { type: "warning", message: "Simple warning alert - check it out!" },
  { type: "info", message: "Simple info alert - check it out!" },
  { type: "light", message: "Simple light alert - check it out!" },
  { type: "dark", message: "Simple dark alert - check it out!" },
];

const BasicAlert: React.FC = () => {
  return (
    <Card>
      <CardHeader className="code-header d-flex justify-content-between align-items-center">
        <h5 className="mb-0">Basic Alerts</h5>
        <a href="#" id="togglerAlert1">
          <IconCode data-source="blockbtn" className="source" size={32} />
        </a>
      </CardHeader>

      <CardBody>
        {alerts.map((alert, index) => (
          <Alert key={index} variant={alert.type} role="alert">
            {alert.message}
          </Alert>
        ))}

        <UncontrolledCollapseWrapper toggler="#togglerAlert1">
          <pre className="simpalalert mt-3">
            <code className="language-html">
              {`<Card>
  <CardHeader>
    <h5>Basic Alerts</h5>
  </CardHeader>
  <CardBody>
${alerts
  .map(
    (alert) =>
      `    <Alert variant="${alert.type}" role="alert">${alert.message}</Alert>`
  )
  .join("\n")}
  </CardBody>
</Card>`}
            </code>
          </pre>
        </UncontrolledCollapseWrapper>
      </CardBody>
    </Card>
  );
};

export default BasicAlert;
