import React from "react";
import { Col, Card, CardHeader, CardBody, Row } from "reactstrap";
import { IconCode } from "@tabler/icons-react";

const buttonGroups = [
  {
    groupClass: "btn-secondary",
    buttons: ["Button", "Button", "Button"],
  },
  {
    groupClass: "btn-outline-secondary",
    buttons: ["Button", "Button", "Button"],
  },
  {
    groupClass: "btn-light-secondary",
    buttons: ["Button", "Button", "Button"],
  },
];

const radioButtons = [
  { id: "vbtn-radio1", label: "Radio 1" },
  { id: "vbtn-radio2", label: "Radio 2" },
  { id: "vbtn-radio3", label: "Radio 3" },
];

const VerticalButtonCard = () => {
  const [isCodeVisible, setIsCodeVisible] = React.useState(false);
  return (
    <Col xs={12}>
      <Card>
        <CardHeader className="code-header">
          <h5>Button Vertical</h5>
          <a
            href="#buttonverticalexample"
            onClick={(e) => {
              e.preventDefault();
              setIsCodeVisible(!isCodeVisible);
            }}
          >
            <IconCode data-source="btnvertical" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <Row className="btn-responsive">
            {buttonGroups.map((group, index) => (
              <Col className="m-2" xs="auto" key={index}>
                <div
                  className="btn-group-vertical"
                  role="group"
                  aria-label={`Vertical button group ${index + 1}`}
                >
                  {group.buttons.map((button, btnIndex) => (
                    <button
                      type="button"
                      className={`btn ${group.groupClass}`}
                      key={btnIndex}
                    >
                      {button}
                    </button>
                  ))}
                </div>
              </Col>
            ))}
            <Col className="m-2" xs="auto">
              <div
                className="btn-group-vertical"
                role="group"
                aria-label="Vertical radio toggle button group"
              >
                {radioButtons.map((radio, index) => (
                  <React.Fragment key={radio.id}>
                    <input
                      type="radio"
                      className="btn-check"
                      name="vbtn-radio"
                      id={radio.id}
                      defaultChecked={index === 0}
                    />
                    <label
                      className="btn btn-outline-secondary"
                      htmlFor={radio.id}
                    >
                      {radio.label}
                    </label>
                  </React.Fragment>
                ))}
              </div>
            </Col>
          </Row>
        </CardBody>
        <pre
          className={`btnvertical mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="buttonverticalexample"
        >
          <code className="language-html">
            {`
<div class="row">
    ${buttonGroups
      .map(
        (group) => `
    <div class="col-lg-4 col-12 mb-3">
        <div class="btn-group-vertical" role="group" aria-label="Vertical button group">
            ${group.buttons
              .map(
                (button) => `
            <button type="button" class="btn ${group.groupClass}">${button}</button>`
              )
              .join("")}
        </div>
    </div>`
      )
      .join("")}
    <div class="col-lg-4 col-12 mb-3">
        <div class="btn-group-vertical ms-2" role="group" aria-label="Vertical radio toggle button group">
            ${radioButtons
              .map(
                (radio) => `
            <input type="radio" class="btn-check" name="vbtn-radio" id="${radio.id}">
            <label class="btn btn-outline-secondary" for="${radio.id}">${radio.label}</label>`
              )
              .join("")}
        </div>
    </div>
</div>
            `}
          </code>
        </pre>
      </Card>
    </Col>
  );
};

export default VerticalButtonCard;
