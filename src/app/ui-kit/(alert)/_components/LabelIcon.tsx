import React from "react";
import { Alert, Card, CardBody, CardHeader } from "react-bootstrap";
import { DownloadSimple } from "phosphor-react";
import "prismjs/themes/prism.css";
import { IconCode } from "@tabler/icons-react";
import { UncontrolledCollapse } from "reactstrap";

const alertData = [
  {
    color: "primary",
    icon: <DownloadSimple size={20} weight="bold" />,
    message: "Primary label alert - check out!",
  },
  {
    color: "secondary",
    icon: <DownloadSimple size={20} weight="bold" />,
    message: "Secondary label alert - check out!!",
  },
  {
    color: "success",
    icon: <DownloadSimple size={20} weight="bold" />,
    message: "Success label alert - check out!",
  },
];

const AlertIcon: React.FC = () => {
  return (
    <Card>
      <CardHeader className="code-header d-flex justify-content-between align-items-center">
        <h5 className="mb-0">Alert With Label Icons</h5>
        <a href="#" id="togglerAlert6">
          <IconCode data-source="blockbtn" className="source" size={32} />
        </a>
      </CardHeader>

      <CardBody>
        {alertData.map((alert, index) => (
          <Alert
            key={index}
            className={`alert-label alert-label-${alert.color}`}
            role="alert"
          >
            <p className="mb-0 d-flex align-items-center gap-2">
              <span className={`label-icon label-icon-${alert.color}`}>
                {alert.icon}
              </span>
              {alert.message}
            </p>
          </Alert>
        ))}

        <UncontrolledCollapse toggler="togglerAlert6">
          <div>
            <pre className="">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Alert With Label Icons</h5>
  </CardHeader>
  <CardBody>
${alertData
  .map(
    (
      alert
    ) => `    <div className="alert alert-label alert-label-${alert.color}" role="alert">
      <p className="mb-0 d-flex align-items-center gap-2">
        <span className="label-icon label-icon-${alert.color}">
          <DownloadSimple size={20} weight="bold" />
        </span>
        ${alert.message}
      </p>
    </div>`
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

export default AlertIcon;
