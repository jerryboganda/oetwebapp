import React from "react";
import { Alert, Card, CardBody, CardHeader } from "react-bootstrap";
import {
  AlignBottom,
  CheckCircle,
  Download,
  Globe,
  Info,
  Power,
  Warning,
  Wheelchair,
  X,
} from "phosphor-react";
import "prismjs/themes/prism.css";
import { IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const alertList = [
  {
    color: "light-border-primary",
    icon: <Download size={16} className="me-2" />,
    message: "Light-border-primary alert With icons - check it out!",
    type: ' <Download size={16} className="me-2" />',
  },
  {
    color: "light-border-secondary",
    icon: <Wheelchair size={16} className="me-2" />,
    message: "Light-border-secondary alert With icons - check it out!",
    type: '<Wheelchair size={16} className="me-2" /> ',
  },
  {
    color: "light-border-success",
    icon: <CheckCircle size={16} className="me-2" />,
    message: "Light-border-success alert With icons - check it out!",
    type: ' <CheckCircle size={16} className="me-2" />',
  },
  {
    color: "light-border-danger",
    icon: <Power size={16} className="me-2" />,
    message: "Light-border-danger alert With icons - check it out!",
    type: ' <Power size={16} className="me-2" />',
  },
  {
    color: "light-border-warning",
    icon: <Warning size={16} className="me-2" />,
    message: "Light-border-warning alert With icons - check it out!",
    type: '<Warning size={16} className="me-2" /> ',
  },
  {
    color: "light-border-info",
    icon: <Info size={16} className="me-2" />,
    message: "Light-border-info alert With icons - check it out!",
    type: ' <Info size={16} className="me-2" /> ',
  },
  {
    color: "light-border-light",
    icon: <AlignBottom size={16} className="me-2" />,
    message: "Light-border-light alert With icons - check it out!",
    type: ' <AlignBottom size={16} className="me-2" />',
  },
  {
    color: "light-border-dark",
    icon: <Globe size={16} className="me-2" />,
    message: "Light-border-dark alert With icons - check it out!",
    type: '<Globe size={16} className="me-2" /> ',
  },
];

const WithIcon: React.FC = () => {
  return (
    <Card>
      <CardHeader className="d-flex justify-content-between align-items-center code-header">
        <h5 className="mb-0">Alert With Icons</h5>
        <a href="#" id="togglerAlert4">
          <IconCode data-source="blockbtn" className="source" size={32} />
        </a>
      </CardHeader>

      <CardBody>
        {alertList.map((alert, index) => (
          <Alert
            key={index}
            variant={alert.color}
            className="d-flex justify-content-between align-items-center"
          >
            <p className="mb-0 d-flex align-items-center">
              {alert.icon}
              {alert.message}
            </p>
            <X size={16} weight="bold" className="cursor-pointer" />
          </Alert>
        ))}

        <UncontrolledCollapseWrapper toggler="togglerAlert4">
          <div>
            <pre className="">
              <code className="language-html">
                {`<Card>
  <CardHeader>
    <h5>Alert With Icon</h5>
  </CardHeader>
  <CardBody>
${alertList
  .map(
    (alert) =>
      `    <Alert variant="${alert.color}" className="d-flex justify-content-between align-items-center">
      <p className="mb-0 d-flex align-items-center">
       ${alert.type}
        ${alert.message}
      </p>
      <X size={16} weight="bold" className="cursor-pointer" />
    </Alert>`
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

export default WithIcon;
