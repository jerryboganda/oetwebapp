import { Col, Card, CardHeader, CardBody } from "reactstrap";
import { IconCode } from "@tabler/icons-react";
import { useState } from "react";

const radiusButtons = [
  { color: "primary", radius: 0, label: "Primary" },
  { color: "secondary", radius: 6, label: "Secondary" },
  { color: "success", radius: 10, label: "Success" },
  { color: "danger", radius: 22, label: "Danger" },
];

const SizeRadiusButton = () => {
  const [isCodeVisible, setIsCodeVisible] = useState(false);
  return (
    <Col xs={12}>
      <Card>
        <CardHeader className="code-header">
          <h5>Radius Button</h5>
          <a
            href="#radiosbtnexample"
            onClick={(e) => {
              e.preventDefault();
              setIsCodeVisible(!isCodeVisible);
            }}
          >
            <IconCode data-source="radiosbtn" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <div className="app-btn-list">
            {radiusButtons.map((button, index) => (
              <button
                key={index}
                type="button"
                className={`btn btn-${button.color} b-r-${button.radius}`}
              >
                {button.label}
              </button>
            ))}
          </div>
        </CardBody>
        <pre
          className={`radiosbtn mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="radiosbtnexample"
        >
          <code className="language-html">
            {`
<div class="app-btn-list">
  ${radiusButtons
    .map(
      (button) => `
    <button type="button" class="btn btn-${button.color} b-r-${button.radius}">${button.label}</button>
  `
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

export default SizeRadiusButton;
