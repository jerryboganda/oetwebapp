import React from "react";
import { IconDownload, IconAlertTriangle, IconCode } from "@tabler/icons-react";
import { Button, Card, CardBody, CardHeader, Col, Row } from "reactstrap";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const buttonStyles = [
  {
    primary: "btn btn-primary",
    secondary: "btn btn-secondary d-lg-inline-flex align-items-center",
  },
  {
    primary: "btn btn-outline-primary",
    secondary: "btn btn-outline-secondary d-lg-inline-flex align-items-center",
  },
  {
    primary: "btn btn-light-primary",
    secondary: "btn btn-light-secondary d-lg-inline-flex align-items-center",
  },
];

const IconButton = () => {
  const prismCode = `<div class="row app-btn-list">
${buttonStyles
  .map(
    (style) => `  <div class="col-md-6 col-lg-4">
    <Button type="button" className="${style.primary}">
      <IconDownload size={18} className="me-2" /> Primary
    </Button>
    <Button type="button" className="${style.secondary}">
      Secondary <IconAlertTriangle size={18} className="ms-2" />
    </Button>
  </div>`
  )
  .join("\n")}
</div>`;

  return (
    <Col xs="12">
      <Card className="button-view">
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5 className="mb-0">Icon Button</h5>
          <a href="#" id="togglerIconButtons">
            <IconCode className="source" size={32} />
          </a>
        </CardHeader>

        <CardBody>
          <Row className="app-btn-list">
            {buttonStyles.map((style, index) => (
              <Col key={index} md={6} lg={4} className=" mb-3 ">
                <Button
                  type="button"
                  color={"primary"}
                  className={`${style.primary} me-2`}
                >
                  <IconDownload size={18} className="me-2" />
                  Primary
                </Button>
                <Button
                  type="button"
                  color={"secondary"}
                  className={style.secondary}
                >
                  Secondary <IconAlertTriangle size={18} className="ms-2" />
                </Button>
              </Col>
            ))}
          </Row>
        </CardBody>

        <UncontrolledCollapseWrapper toggler="#togglerIconButtons">
          <pre className="language-html m-0">
            <code className="language-html">{prismCode}</code>
          </pre>
        </UncontrolledCollapseWrapper>
      </Card>
    </Col>
  );
};

export default IconButton;
