import { Col, Row, Card, CardHeader, CardBody } from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import { useState } from "react";

const buttonGroups = [
  [
    { color: "primary", label: "Button" },
    { color: "secondary", label: "Button" },
  ],
  [
    { color: "outline-primary", label: "Button" },
    { color: "outline-secondary", label: "Button" },
  ],
  [
    { color: "light-primary", label: "Button" },
    { color: "light-secondary", label: "Button" },
  ],
];

const BlockButtons = () => {
  const [isCodeVisible, setIsCodeVisible] = useState(false);
  return (
    <Col xs={12}>
      <Card>
        <CardHeader className="code-header">
          <h5>Block Buttons</h5>
          <a
            href="#blockbtnexample"
            onClick={(e) => {
              e.preventDefault();
              setIsCodeVisible(!isCodeVisible);
            }}
          >
            <IconCode data-source="blockbtn" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <Row>
            {buttonGroups.map((group, index) => (
              <Col md={6} lg={4} xs={12} className="app-btn-list" key={index}>
                <div className="d-grid gap-2">
                  {group.map((button, btnIndex) => (
                    <button
                      key={btnIndex}
                      className={`btn btn-${button.color} w-100`}
                      type="button"
                    >
                      {button.label}
                    </button>
                  ))}
                </div>
              </Col>
            ))}
          </Row>
        </CardBody>

        <pre
          className={`blockbtn mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="blockbtnexample"
        >
          <code className="language-html">
            {`
<div class="row">
    ${buttonGroups
      .map(
        (group) => `
    <div class="col-md-6 col-lg-4 col-12">
        <div class="d-grid gap-2">
            ${group
              .map(
                (button) =>
                  `<button class="btn btn-${button.color} w-100" type="button">${button.label}</button>`
              )
              .join("\n")}
        </div>
    </div>`
      )
      .join("\n")}
</div>
            `}
          </code>
        </pre>
      </Card>
    </Col>
  );
};

export default BlockButtons;
