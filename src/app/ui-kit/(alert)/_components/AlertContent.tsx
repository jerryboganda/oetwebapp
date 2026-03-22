import React, { useState } from "react";
import { Alert, Card, CardBody, CardHeader, Collapse } from "react-bootstrap";
import "prismjs/themes/prism.css";
import { Code, X } from "phosphor-react";

interface AlertItem {
  variant: string;
  heading: string;
  message: string;
  footer: string;
}

const initialAlerts: AlertItem[] = [
  {
    variant: "primary",
    heading: "Well done!",
    message:
      "Aww yeah, you successfully read this important alert message. This example text is going to run a bit longer so that you can see how spacing within an alert works with this kind of content.",
    footer:
      "Whenever you need to, be sure to use margin utilities to keep things nice and tidy.",
  },
  {
    variant: "secondary",
    heading: "Heads up!",
    message:
      "This is another alert. It demonstrates usage of spacing and content layout inside alerts.",
    footer: "Use margin utilities as needed.",
  },
  {
    variant: "success",
    heading: "Congratulations!",
    message:
      "You’ve successfully triggered a success alert with additional content and a dismiss icon.",
    footer: "Feel free to customize this layout further.",
  },
];

const AlertContent: React.FC = () => {
  const [open, setOpen] = useState(false);
  const [alerts, setAlerts] = useState<AlertItem[]>(initialAlerts);

  const dismissAlert = (index: number) => {
    setAlerts((prev) => prev.filter((_, i) => i !== index));
  };

  return (
    <Card>
      <CardHeader className="code-header d-flex justify-content-between align-items-center">
        <h5 className="mb-0">Alert Additional Content</h5>
        <a onClick={() => setOpen(!open)} className="cursor-pointer">
          <Code size={30} weight="bold" className="source" />
        </a>
      </CardHeader>
      <CardBody>
        {alerts.map((alert, index) => (
          <Alert key={index} variant={alert.variant}>
            <h3 className="alert-heading d-flex justify-content-between align-items-center">
              {alert.heading}
              <X
                size={21}
                weight="bold"
                className="cursor-pointer"
                onClick={() => dismissAlert(index)}
              />
            </h3>
            <p>{alert.message}</p>
            <hr />
            <p className="mb-0">{alert.footer}</p>
          </Alert>
        ))}

        <Collapse in={open}>
          <div>
            <pre className="mt-3">
              <code className="language-html">{`<Card>
  <CardHeader>
    <h5>Alert Additional Content</h5>
  </CardHeader>
  <CardBody>
${initialAlerts
  .map(
    (alert) => `<Alert variant="${alert.variant}">
  <h3 className="alert-heading d-flex justify-content-between align-items-center">
    ${alert.heading}
    <X size={21} weight="bold" className="cursor-pointer" />
  </h3>
  <p>${alert.message}</p>
  <hr />
  <p className="mb-0">${alert.footer}</p>
</Alert>`
  )
  .join("\n\n")}
  </CardBody>
</Card>`}</code>
            </pre>
          </div>
        </Collapse>
      </CardBody>
    </Card>
  );
};

export default AlertContent;
