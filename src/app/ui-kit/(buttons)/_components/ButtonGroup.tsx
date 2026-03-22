import React from "react";
import { Col, Card, CardHeader, CardBody, Row, Button } from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const buttonGroups = [
  {
    class: "btn-primary",
    active: true,
    links: ["Active link", "Link", "Link"],
  },
  {
    class: "btn-outline-secondary",
    active: false,
    links: ["Active link", "Link", "Link"],
  },
  {
    class: "btn-light-success",
    active: false,
    links: ["Active link", "Link", "Link"],
  },
];

const sizes = [
  {
    sizeClass: "btn-group-lg",
    ariaLabel: "Large button group",
    btnClass: "btn-outline-primary",
  },
  {
    sizeClass: "btn-group",
    ariaLabel: "Default button group",
    btnClass: "btn-outline-secondary",
  },
  {
    sizeClass: "btn-group-sm",
    ariaLabel: "Small button group",
    btnClass: "btn-outline-success",
  },
];

const ButtonGroupExample = () => {
  return (
    <>
      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Button Group</h5>
            <a href="#" id="togglerBullet1">
              <IconCode className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Row>
              {buttonGroups.map((group, index) => (
                <Col xs="12" className={index > 0 ? "mt-3" : ""} key={index}>
                  <div className="btn-group">
                    {group.links.map((link, linkIndex) => (
                      <a
                        href="#"
                        key={linkIndex}
                        className={`btn ${group.class} ${
                          group.active && linkIndex === 0 ? "active" : ""
                        }`}
                        aria-current={
                          group.active && linkIndex === 0 ? "page" : undefined
                        }
                      >
                        {link}
                      </a>
                    ))}
                  </div>
                </Col>
              ))}
            </Row>
          </CardBody>

          <UncontrolledCollapseWrapper toggler="#togglerBullet1">
            <pre className="language-html m-0">
              <code className="language-html">
                {`<div className="row">
${buttonGroups
  .map(
    (group) => `  <div className="col-md-6 col-lg-4 col-12">
    <div className="btn-group">
${group.links
  .map(
    (link, index) =>
      `      <a href="#" className="btn ${group.class} ${
        group.active && index === 0 ? "active" : ""
      }" aria-current="${
        group.active && index === 0 ? "page" : ""
      }">${link}</a>`
  )
  .join("\n")}
    </div>
  </div>`
  )
  .join("\n")}
</div>`}
              </code>
            </pre>
          </UncontrolledCollapseWrapper>
        </Card>
      </Col>

      <Col lg={6}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Sizes</h5>
            <a href="#" id="togglerSize">
              <IconCode className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <Row>
              {sizes.map((size, index) => (
                <Col xs="12" className={index > 0 ? "mt-3" : ""} key={index}>
                  <div
                    className={`btn-group ${size.sizeClass}`}
                    role="group"
                    aria-label={size.ariaLabel}
                  >
                    {["Left", "Middle", "Right"].map((btnLabel, btnIndex) => (
                      <Button
                        color={"outline-primary"}
                        key={btnIndex}
                        type="button"
                        className={`btn ${size.btnClass} btn-outline-primary`}
                      >
                        {btnLabel}
                      </Button>
                    ))}
                  </div>
                </Col>
              ))}
            </Row>
          </CardBody>

          <UncontrolledCollapseWrapper toggler="#togglerSize">
            <pre className="language-html m-0">
              <code className="language-html">
                {`<div className="row">
${sizes
  .map(
    (size) => `  <div className="col-lg-4 col-12 mb-3">
    <div className="btn-group ${size.sizeClass}" role="group" aria-label="${size.ariaLabel}">
      <Button type="Button" className="btn ${size.btnClass}">Left</Button>
      <Button type="Button" className="btn ${size.btnClass}">Middle</Button>
      <Button type="Button" className="btn ${size.btnClass}">Right</Button>
    </div>
  </div>`
  )
  .join("\n")}
</div>`}
              </code>
            </pre>
          </UncontrolledCollapseWrapper>
        </Card>
      </Col>
    </>
  );
};

export default ButtonGroupExample;
