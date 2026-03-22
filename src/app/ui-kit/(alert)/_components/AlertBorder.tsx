import React from "react";
import { Alert, Card, CardBody, CardHeader } from "react-bootstrap";
import "prismjs/themes/prism.css";
import { IconCode } from "@tabler/icons-react";
import { UncontrolledCollapse } from "reactstrap";

const borderAlerts = [
  {
    color: "border-primary",
    message: "Alert with left slide border - check out!",
  },
  {
    color: "border-secondary",
    message: "Alert with left slide border - check out!",
  },
  {
    color: "border-success",
    message: "Alert with left slide border - check out!",
  },
];

const AlertBorder: React.FC = () => {
  return (
    <Card>
      <CardHeader className="code-header d-flex justify-content-between align-items-center">
        <h5 className="mb-0">Alert With Left Border</h5>
        <a href="#" id="togglerAlert5">
          <IconCode data-source="blockbtn" className="source" size={32} />
        </a>
      </CardHeader>

      <CardBody>
        {borderAlerts.map((alert, index) => (
          <Alert key={index} variant={alert.color} role="alert">
            {alert.message}
          </Alert>
        ))}

        <UncontrolledCollapse toggler="togglerAlert5">
          <div>
            <pre className="">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Alert With Left Border</h5>
  </CardHeader>
  <CardBody>
${borderAlerts
  .map(
    (alert) =>
      `    <Alert variant="${alert.color}" role="alert">${alert.message}</Alert>`
  )
  .join("\n")}
  </CardBody>
</Card>`}
              </code>
            </pre>
          </div>
        </UncontrolledCollapse>
      </CardBody>
    </Card>
  );
};

export default AlertBorder;
