import React, { useState } from "react";
import { Button, Card, CardHeader, Collapse } from "react-bootstrap";
import "prismjs/themes/prism.css";
import { Code } from "phosphor-react";

interface AlertProps {
  message: string;
  type: string; // e.g. 'primary', 'danger', 'success'
}

const LiveAlert: React.FC = () => {
  const [open, setOpen] = useState(false);
  const [alerts, setAlerts] = useState<AlertProps[]>([]);

  const appendAlert = (message: string, type: string) => {
    setAlerts((prev) => [...prev, { message, type }]);
  };

  const removeAlert = (index: number) => {
    setAlerts((prev) => prev.filter((_, i) => i !== index));
  };

  return (
    <Card>
      <CardHeader className="code-header d-flex justify-content-between align-items-center">
        <h5 className="mb-0">Live Alert</h5>
        <a onClick={() => setOpen(!open)} className="cursor-pointer">
          <Code size={30} weight="bold" className="source" />
        </a>
      </CardHeader>

      <div className="card-body">
        <div id="liveAlert">
          {alerts.map((alert, index) => (
            <div
              key={index}
              className={`alert alert-${alert.type} alert-dismissible fade show`}
              role="alert"
            >
              <div>{alert.message}</div>
              <button
                type="button"
                className="btn-close"
                aria-label="Close"
                onClick={() => removeAlert(index)}
              />
            </div>
          ))}
        </div>
        <Button
          variant="primary"
          onClick={() =>
            appendAlert("Hi! Welcome to PolytronX 🎉", "light-primary")
          }
        >
          Show live alert
        </Button>
      </div>

      <Collapse in={open}>
        <div>
          <pre className="livealert mt-3">
            <code className="language-html">
              {`<Card>
  <CardHeader>
    <h5>Live Alert</h5>
  </CardHeader>
  <CardBody>
    <div id="liveAlert"></div>
    <Button variant="primary">Show live alert</Button>
  </CardBody>
</Card>`}
            </code>
          </pre>
        </div>
      </Collapse>
    </Card>
  );
};

export default LiveAlert;
