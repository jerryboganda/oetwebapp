import React from "react";
import { Col, Card, CardHeader, CardBody, Row } from "reactstrap";
import { IconDownload, IconAlertTriangle } from "@tabler/icons-react";

const buttonData = [
  {
    btnClass: "btn-primary",
    icon: <IconDownload size={18} />,
    label: "Primary",
  },
  {
    btnClass: "btn-secondary",
    icon: <IconAlertTriangle size={18} className="m-s-3" />,
    label: "Secondary",
  },
  {
    btnClass: "btn-outline-primary",
    icon: <IconDownload size={18} />,
    label: "Primary",
  },
  {
    btnClass: "btn-outline-secondary",
    icon: <IconAlertTriangle size={18} className="m-s-3" />,
    label: "Secondary",
  },
  {
    btnClass: "btn-light-primary",
    icon: <IconDownload size={18} />,
    label: "Primary",
  },
  {
    btnClass: "btn-light-secondary",
    icon: <IconAlertTriangle size={18} className="m-s-3" />,
    label: "Secondary",
  },
];

const DisabledButtonCard = () => {
  return (
    <Col xs={12}>
      <Card>
        <CardHeader>
          <h5>Disable Buttons</h5>
        </CardHeader>
        <CardBody>
          <Row>
            {["primary", "secondary", "light"].map((type, index) => (
              <Col md={6} lg={4} xs={12} className="app-btn-list" key={index}>
                {buttonData
                  .filter((button) => button.btnClass.includes(type))
                  .map((item, idx) => (
                    <button
                      key={idx}
                      type="button"
                      className={`btn ${item.btnClass} disabled`}
                    >
                      {item.icon} {item.label}
                    </button>
                  ))}
              </Col>
            ))}
          </Row>
        </CardBody>
        <pre className="radious collapse mt-3" id="radiousexample">
          <code className="language-html"></code>
        </pre>
      </Card>
    </Col>
  );
};

export default DisabledButtonCard;
