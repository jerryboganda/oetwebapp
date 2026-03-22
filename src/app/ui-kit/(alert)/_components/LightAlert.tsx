import React from "react";
import { Card, CardBody, CardHeader, Alert } from "react-bootstrap";
import "prismjs/themes/prism.css";
import { IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const lightAlerts = [
  {
    type: "primary",
    text: "Light primary color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "secondary",
    text: "Light secondary color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "success",
    text: "Light success color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "danger",
    text: "Light danger color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "warning",
    text: "Light warning color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "info",
    text: "Light info color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "light",
    text: "Light light color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
  {
    type: "dark",
    text: "Light dark color alert with",
    linkText: "an example link",
    linkHref: "#",
  },
];

const LightAlert = () => {
  return (
    <Card>
      <CardHeader className="code-header">
        <h5 className="mb-0">Light Alert With Link color</h5>

        <a href="#" id="togglerAlert2">
          <IconCode data-source="blockbtn" className="source" size={32} />
        </a>
      </CardHeader>

      <CardBody>
        {lightAlerts.map((alert, index) => (
          <Alert key={index} variant={`light-${alert.type}`} role="alert">
            {alert.text}{" "}
            <Alert.Link href={alert.linkHref}>{alert.linkText}</Alert.Link> -
            Check it!
          </Alert>
        ))}

        <UncontrolledCollapseWrapper toggler="togglerAlert2">
          <pre className="lightalert mt-3">
            <code className="language-html" tabIndex={1}>
              {`<Card>
  <CardHeader>
    <h5>Light Alert With Link color</h5>
  </CardHeader>
  <CardBody>
${lightAlerts
  .map(
    (alert) => `    <Alert color="light-${alert.type}">
      ${alert.text}<Alert.Link href="${alert.linkHref}">${alert.linkText}</Alert.Link> - Check it!
    </Alert>`
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

export default LightAlert;
