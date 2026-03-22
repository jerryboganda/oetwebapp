import React from "react";
import { IconDownload, IconAlertTriangle, IconCode } from "@tabler/icons-react";
import { Button, Card, CardBody, CardHeader, Col, Row } from "reactstrap";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

// Button data with class strings only
const buttonStyles = [
  {
    primary: "btn btn-primary rounded-pill",
    secondary:
      "btn btn-secondary rounded-pill d-lg-inline-flex align-items-center",
  },
  {
    primary: "btn btn-outline-primary",
    secondary:
      "btn btn-outline-secondary rounded-pill d-lg-inline-flex align-items-center",
  },
  {
    primary: "btn btn-light-primary",
    secondary:
      "btn btn-light-secondary rounded-pill d-lg-inline-flex align-items-center",
  },
];

const RadiusButton = () => {
  // PrismJS raw HTML code block
  const prismCode = `<div class="row app-btn-list">
${buttonStyles
  .map(
    (style) => `  <div class="col-md-6 col-lg-4">
    <Button type="button" class="${style.primary}">
        <IconDownload size={18} className="me-2" />Primary
    </Button>
    <Button type="button" class="${style.secondary}">
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
          <h5 className="mb-0">Radious</h5>
          <a href="#" id="toggleIconBtn">
            <IconCode className="source" size={32} />
          </a>
        </CardHeader>

        <CardBody>
          <Row className="app-btn-list">
            {buttonStyles.map((style, index) => (
              <Col md={6} lg={4} xs={12} key={index}>
                <Button
                  type="button"
                  color={"primary"}
                  className={`${style.primary} me-2 b-r-22`}
                >
                  <IconDownload size={18} className="me-2" />
                  Primary
                </Button>
                {""}
                <Button type="button" className={style.secondary}>
                  Secondary <IconAlertTriangle size={18} className="ms-2" />
                </Button>
              </Col>
            ))}
          </Row>
        </CardBody>

        <UncontrolledCollapseWrapper toggler="#toggleIconBtn">
          <pre className="language-html mt-3 mb-0">
            <code className="language-html">{prismCode}</code>
          </pre>
        </UncontrolledCollapseWrapper>
      </Card>
    </Col>
  );
};

export default RadiusButton;
