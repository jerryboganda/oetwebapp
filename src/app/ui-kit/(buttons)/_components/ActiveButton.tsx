import React, { useState } from "react";
import { Col, Card, CardHeader, CardBody, Row } from "reactstrap";
import { IconDownload, IconAlertTriangle, IconCode } from "@tabler/icons-react";

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

const ActiveButton = () => {
  const [isCodeVisible, setIsCodeVisible] = useState(false);
  return (
    <Col xs={12}>
      <Card>
        <CardHeader className="code-header">
          <h5>Active Buttons</h5>
          <a
            href="#activebuttonexample"
            onClick={(e) => {
              e.preventDefault();
              setIsCodeVisible(!isCodeVisible);
            }}
          >
            <IconCode data-source="activebtn" className="source" size={32} />
          </a>
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
                      className={`btn ${item.btnClass} active`}
                    >
                      {item.icon} {item.label}
                    </button>
                  ))}
              </Col>
            ))}
          </Row>
        </CardBody>
        <pre
          className={`activebtn mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="activebuttonexample"
        >
          <code className="language-html">
            {`
<div class="row">
    <div class="col-md-6 col-lg-4 col-12 mb-3 app-btn-list">
        <button type="button" class="btn btn-primary active">
            <i class="ti ti-download"></i> Primary
        </button>
        <button type="button" class="btn btn-secondary d-lg-inline-flex align-items-center active">
            Secondary <i class="ti ti-alert-triangle m-s-3"></i>
        </button>
    </div>
    <div class="col-md-6 col-lg-4 col-12 mb-3 app-btn-list">
        <button type="button" class="btn btn-outline-primary active">
            <i class="ti ti-download"></i> Primary
        </button>
        <button type="button" class="btn btn-outline-secondary d-lg-inline-flex align-items-center active">
            Secondary <i class="ti ti-alert-triangle m-s-3"></i>
        </button>
    </div>
    <div class="col-md-6 col-lg-4 col-12 mb-3 app-btn-list">
        <button type="button" class="btn btn-light-primary active">
            <i class="ti ti-download"></i> Primary
        </button>
        <button type="button" class="btn btn-light-secondary d-lg-inline-flex align-items-center active">
            Secondary <i class="ti ti-alert-triangle m-s-3"></i>
        </button>
    </div>
</div>
            `}
          </code>
        </pre>
      </Card>
    </Col>
  );
};

export default ActiveButton;
